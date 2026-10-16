using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using Greener.Dataverse.Monitor.Configuration;
using Greener.Dataverse.Monitor.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Greener.Dataverse.Monitor.Services;

public sealed class DataverseMonitorWorker : BackgroundService
{
    private sealed record ConfiguredQuery(
        string Name,
        string Method,
        string UrlPath,
        Uri RequestUri,
        string? ContentType,
        string? RequestBody,
        string? BatchBoundary);

    private readonly HttpClient _httpClient;
    private readonly ITokenProvider _tokenProvider;
    private readonly ICsvLogger _csvLogger;
    private readonly ILogger<DataverseMonitorWorker> _logger;
    private readonly MonitorOptions _options;
    private readonly IReadOnlyList<ConfiguredQuery> _queries;

    public DataverseMonitorWorker(
        IHttpClientFactory httpClientFactory,
        ITokenProvider tokenProvider,
        ICsvLogger csvLogger,
        IOptions<MonitorOptions> options,
        ILogger<DataverseMonitorWorker> logger)
    {
        _httpClient = httpClientFactory.CreateClient("DataverseMonitor");
        _tokenProvider = tokenProvider;
        _csvLogger = csvLogger;
        _logger = logger;
        _options = options.Value;
        _queries = BuildQueries(_options);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var stopAtUtc = ResolveStopAtUtc(_options.StopAfterMinutes);

        _csvLogger.EnsureHeader();
        _logger.LogInformation(
            "Dataverse monitor started with {QueryCount} querie(s), every {IntervalMinutes} min, stop after {StopAfterMinutes} min",
            _queries.Count,
            _options.IntervalMinutes,
            _options.StopAfterMinutes);
        if (stopAtUtc.HasValue)
        {
            _logger.LogInformation("Stop time configured at {StopAtUtc}.", stopAtUtc.Value.ToString("u"));
        }

        foreach (var query in _queries)
        {
            _logger.LogInformation("Configured query: {QueryName} => {Method} {UrlPath}", query.Name, query.Method, query.UrlPath);
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            if (stopAtUtc.HasValue && DateTimeOffset.UtcNow >= stopAtUtc.Value)
            {
                _logger.LogInformation("Configured stop time reached. Dataverse monitor stops now.");
                break;
            }

            var cycleStart = DateTimeOffset.UtcNow;
            await ExecuteCycleAsync(stoppingToken);

            if (stopAtUtc.HasValue && DateTimeOffset.UtcNow >= stopAtUtc.Value)
            {
                _logger.LogInformation("Configured stop time reached after cycle. Dataverse monitor stops now.");
                break;
            }

            var wait = TimeSpan.FromMinutes(_options.IntervalMinutes) - (DateTimeOffset.UtcNow - cycleStart);
            if (wait > TimeSpan.Zero)
            {
                if (stopAtUtc.HasValue)
                {
                    var remaining = stopAtUtc.Value - DateTimeOffset.UtcNow;
                    if (remaining <= TimeSpan.Zero)
                    {
                        _logger.LogInformation("Configured stop time reached before next cycle. Dataverse monitor stops now.");
                        break;
                    }

                    if (wait > remaining)
                    {
                        wait = remaining;
                    }
                }

                await Task.Delay(wait, stoppingToken);
            }
        }
    }

    private async Task ExecuteCycleAsync(CancellationToken cancellationToken)
    {
        foreach (var query in _queries)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            await ExecuteQueryAsync(query, cancellationToken);
        }
    }

    private async Task ExecuteQueryAsync(ConfiguredQuery query, CancellationToken cancellationToken)
    {
        var attempt = 0;
        var stopwatch = Stopwatch.StartNew();

        while (!cancellationToken.IsCancellationRequested)
        {
            attempt++;
            HttpResponseMessage? response = null;
            try
            {
                var accessToken = await _tokenProvider.GetAccessTokenAsync(cancellationToken);
                using var request = BuildRequest(query, accessToken);

                var ttfbWatch = Stopwatch.StartNew();
                response = await _httpClient.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken);
                ttfbWatch.Stop();

                var retryAfter = ParseRetryAfter(response);
                if (response.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable)
                {
                    var throttleRow = new CsvRow
                    {
                        TimestampUtc = DateTimeOffset.UtcNow,
                        QueryName = query.Name,
                        EventType = "throttle",
                        Method = query.Method,
                        Url = query.RequestUri.ToString(),
                        StatusCode = (int)response.StatusCode,
                        TtfbMs = Math.Round(ttfbWatch.Elapsed.TotalMilliseconds, 3),
                        TotalMs = double.NaN,
                        HttpVersion = response.Version.ToString(),
                        Server = response.Headers.TryGetValues("Server", out var throttleServerValues) ? string.Join(" ", throttleServerValues) : string.Empty,
                        RetryAfterSec = retryAfter?.TotalSeconds,
                        Error = $"Throttled on attempt {attempt}/{_options.MaxAttempts}"
                    };

                    _csvLogger.Append(throttleRow);
                    _logger.LogWarning(
                        "Throttled ({StatusCode}) for {QueryName} ({Method} {UrlPath}) on attempt {Attempt}/{MaxAttempts}. Retry-After={RetryAfterSec}s.",
                        throttleRow.StatusCode,
                        query.Name,
                        query.Method,
                        query.UrlPath,
                        attempt,
                        _options.MaxAttempts,
                        retryAfter?.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture) ?? "-");

                    await DrainAsync(response, cancellationToken);
                    if (attempt < _options.MaxAttempts)
                    {
                        await DelayWithRetryAsync(attempt, retryAfter, cancellationToken);
                        continue;
                    }
                }
                else
                {
                    await DrainAsync(response, cancellationToken);
                }

                stopwatch.Stop();
                var row = new CsvRow
                {
                    TimestampUtc = DateTimeOffset.UtcNow,
                    QueryName = query.Name,
                    EventType = "result",
                    Method = query.Method,
                    Url = query.RequestUri.ToString(),
                    StatusCode = (int)response.StatusCode,
                    TtfbMs = Math.Round(ttfbWatch.Elapsed.TotalMilliseconds, 3),
                    TotalMs = Math.Round(stopwatch.Elapsed.TotalMilliseconds, 3),
                    HttpVersion = response.Version.ToString(),
                    Server = response.Headers.TryGetValues("Server", out var serverValues) ? string.Join(" ", serverValues) : string.Empty,
                    RetryAfterSec = retryAfter?.TotalSeconds
                };

                _csvLogger.Append(row);
                _logger.LogInformation(
                    "{Timestamp} | {QueryName} | {Method} {UrlPath} | {StatusCode} | TTFB={TtfbMs} ms | Total={TotalMs} ms | v{HttpVersion}",
                    row.TimestampUtc.ToString("u"),
                    query.Name,
                    row.Method,
                    query.UrlPath,
                    row.StatusCode,
                    row.TtfbMs,
                    row.TotalMs,
                    row.HttpVersion);
                return;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (attempt < _options.MaxAttempts)
            {
                _logger.LogWarning(
                    ex,
                    "Attempt {Attempt}/{MaxAttempts} failed for {QueryName} ({Method} {UrlPath}). Retrying.",
                    attempt,
                    _options.MaxAttempts,
                    query.Name,
                    query.Method,
                    query.UrlPath);
                await DelayWithRetryAsync(attempt, null, cancellationToken);
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                _csvLogger.Append(new CsvRow
                {
                    TimestampUtc = DateTimeOffset.UtcNow,
                    QueryName = query.Name,
                    EventType = "result",
                    Method = query.Method,
                    Url = query.RequestUri.ToString(),
                    StatusCode = 0,
                    TtfbMs = double.NaN,
                    TotalMs = Math.Round(stopwatch.Elapsed.TotalMilliseconds, 3),
                    Error = $"{ex.GetType().Name}: {ex.Message}"
                });

                _logger.LogError(ex, "Dataverse monitor request failed for {QueryName} ({Method} {UrlPath}).", query.Name, query.Method, query.UrlPath);
                return;
            }
            finally
            {
                response?.Dispose();
            }
        }
    }

    private static IReadOnlyList<ConfiguredQuery> BuildQueries(MonitorOptions options)
    {
        var configured = new List<MonitorQueryOptions>();
        if (options.Queries is { Count: > 0 })
        {
            configured.AddRange(options.Queries);
        }
        else
        {
            configured.Add(new MonitorQueryOptions
            {
                UrlPath = options.UrlPath,
                Method = options.Method,
                ContentType = options.ContentType,
                RequestBody = options.RequestBody,
                BatchBoundary = options.BatchBoundary
            });
        }

        var queries = new List<ConfiguredQuery>(configured.Count);
        foreach (var query in configured)
        {
            var method = string.IsNullOrWhiteSpace(query.Method) ? "GET" : query.Method.Trim().ToUpperInvariant();
            var urlPath = query.UrlPath.Trim();
            var name = ResolveQueryName(query.Name, urlPath);
            queries.Add(new ConfiguredQuery(
                name,
                method,
                urlPath,
                BuildRequestUri(options, urlPath),
                query.ContentType,
                query.RequestBody,
                query.BatchBoundary));
        }

        return queries;
    }

    private static string ResolveQueryName(string? configuredName, string urlPath)
    {
        if (!string.IsNullOrWhiteSpace(configuredName))
        {
            return configuredName.Trim();
        }

        var pathWithoutQuery = urlPath.Split('?', 2)[0].Trim('/');
        if (!string.IsNullOrWhiteSpace(pathWithoutQuery))
        {
            return pathWithoutQuery;
        }

        return "query";
    }

    private HttpRequestMessage BuildRequest(ConfiguredQuery query, string accessToken)
    {
        var request = new HttpRequestMessage(new HttpMethod(query.Method), query.RequestUri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Version = HttpVersion.Version20;
        request.VersionPolicy = HttpVersionPolicy.RequestVersionOrHigher;

        if (query.Method.Equals("POST", StringComparison.OrdinalIgnoreCase))
        {
            var boundary = string.IsNullOrWhiteSpace(query.BatchBoundary)
                ? "batch_123"
                : query.BatchBoundary.Trim();
            var body = query.RequestBody ?? $"--{boundary}--\r\n";

            var content = new ByteArrayContent(Encoding.UTF8.GetBytes(body));
            content.Headers.ContentType = new MediaTypeHeaderValue(query.ContentType ?? "multipart/mixed");
            if (!content.Headers.ContentType.Parameters.Any(p => p.Name.Equals("boundary", StringComparison.OrdinalIgnoreCase)))
            {
                content.Headers.ContentType.Parameters.Add(new NameValueHeaderValue("boundary", boundary));
            }

            request.Content = content;
        }

        return request;
    }

    private static async Task DrainAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.Content is null)
        {
            return;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var buffer = new byte[8192];
        while (await stream.ReadAsync(buffer, cancellationToken) > 0)
        {
        }
    }

    private static TimeSpan? ParseRetryAfter(HttpResponseMessage response)
    {
        if (response.Headers.RetryAfter?.Delta is TimeSpan delta)
        {
            return delta;
        }

        if (response.Headers.RetryAfter?.Date is DateTimeOffset date)
        {
            var until = date - DateTimeOffset.UtcNow;
            return until < TimeSpan.Zero ? TimeSpan.Zero : until;
        }

        return null;
    }

    private static Task DelayWithRetryAsync(int attempt, TimeSpan? retryAfter, CancellationToken cancellationToken)
    {
        if (retryAfter.HasValue && retryAfter.Value > TimeSpan.Zero)
        {
            return Task.Delay(retryAfter.Value, cancellationToken);
        }

        const int baseDelayMs = 300;
        var exponent = Math.Min(8, attempt);
        var jitterMs = Random.Shared.Next(50, 250);
        var totalDelayMs = baseDelayMs * Math.Pow(2, exponent) + jitterMs;
        return Task.Delay(TimeSpan.FromMilliseconds(totalDelayMs), cancellationToken);
    }

    private static Uri BuildRequestUri(MonitorOptions options, string urlPath)
    {
        var baseUri = new Uri($"{options.ResourceBaseUrl.TrimEnd('/')}/");
        return new Uri(baseUri, $"api/data/v9.0/{urlPath.TrimStart('/')}");
    }

    private static DateTimeOffset? ResolveStopAtUtc(int stopAfterMinutes)
    {
        if (stopAfterMinutes <= 0)
        {
            return null;
        }

        return DateTimeOffset.UtcNow.AddMinutes(stopAfterMinutes);
    }
}
