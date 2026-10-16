using System.Globalization;
using System.Text.Json;
using CsvHelper;
using CsvHelper.Configuration;
using CsvHelper.Configuration.Attributes;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using ScottPlot;

QuestPDF.Settings.License = LicenseType.Community;

try
{
    var parsedArgs = ParseArgs(args);
    var inputCsvPath = ResolveInputCsvPath(parsedArgs);
    var outputPdfPath = ResolveOutputPdfPath(parsedArgs, inputCsvPath);
    var appSettingsAppendix = LoadAppSettingsAppendix(parsedArgs);

    Console.WriteLine($"Input CSV: {inputCsvPath}");
    Console.WriteLine($"Output PDF: {outputPdfPath}");
    Console.WriteLine($"App settings: {appSettingsAppendix.SourcePath ?? "not found"}");

    var entries = LoadEntries(inputCsvPath);
    if (entries.Count == 0)
    {
        Console.Error.WriteLine("No records found in CSV.");
        return 1;
    }

    var reports = BuildReports(entries);
    if (reports.Count == 0)
    {
        Console.Error.WriteLine("No analyzable query data found.");
        return 1;
    }

    var reportTimestamp = DateTimeOffset.Now.ToString("yyyyMMdd_HHmmss");
    var chartsDirectory = Path.Combine(Path.GetDirectoryName(outputPdfPath)!, $"charts_{reportTimestamp}");
    Directory.CreateDirectory(chartsDirectory);
    GenerateCharts(reports, chartsDirectory);

    GeneratePdfReport(inputCsvPath, outputPdfPath, reports, entries, appSettingsAppendix);

    Console.WriteLine($"Report created: {outputPdfPath}");
    Console.WriteLine($"Charts saved to: {chartsDirectory}");
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine(ex.Message);
    return 2;
}

static Dictionary<string, string> ParseArgs(string[] args)
{
    var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    for (var i = 0; i < args.Length; i++)
    {
        var key = args[i];
        if (!key.StartsWith("-", StringComparison.Ordinal))
        {
            continue;
        }

        var normalizedKey = key.TrimStart('-');
        if (i + 1 >= args.Length)
        {
            throw new ArgumentException($"Missing value for argument '{key}'.");
        }

        result[normalizedKey] = args[++i];
    }

    return result;
}

static string ResolveInputCsvPath(IReadOnlyDictionary<string, string> args)
{
    if (TryGetArg(args, out var input, "input", "i"))
    {
        if (File.Exists(input))
        {
            return Path.GetFullPath(input);
        }

        if (Directory.Exists(input))
        {
            var latestInDirectory = FindLatestCsvInDirectory(input);
            if (latestInDirectory is not null)
            {
                return latestInDirectory;
            }
        }

        throw new FileNotFoundException($"Input not found: {input}");
    }

    var candidateDirectories = new[]
    {
        Path.Combine(Environment.CurrentDirectory, "log"),
        Path.Combine(Environment.CurrentDirectory, "Greener.Dataverse.Monitor", "log"),
        Path.Combine(AppContext.BaseDirectory, "log")
    };

    foreach (var dir in candidateDirectories)
    {
        var latest = FindLatestCsvInDirectory(dir);
        if (latest is not null)
        {
            return latest;
        }
    }

    var recursiveLatest = Directory
        .EnumerateFiles(Environment.CurrentDirectory, "*.csv", SearchOption.AllDirectories)
        .Where(path => path.Contains($"{Path.DirectorySeparatorChar}log{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
        .OrderByDescending(File.GetLastWriteTimeUtc)
        .FirstOrDefault();

    if (recursiveLatest is not null)
    {
        return Path.GetFullPath(recursiveLatest);
    }

    throw new FileNotFoundException(
        "No CSV found. Use --input <csv-or-folder>. Expected e.g. './Greener.Dataverse.Monitor/log/*.csv'.");
}

static string ResolveOutputPdfPath(IReadOnlyDictionary<string, string> args, string inputCsvPath)
{
    if (TryGetArg(args, out var output, "output", "o"))
    {
        var outputPath = output;
        if (Directory.Exists(outputPath))
        {
            outputPath = Path.Combine(outputPath, $"dataverse_report_{DateTimeOffset.Now:yyyyMMdd_HHmmss}.pdf");
        }

        if (!Path.HasExtension(outputPath))
        {
            outputPath += ".pdf";
        }

        var fullPath = Path.GetFullPath(outputPath);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        return fullPath;
    }

    var inputDirectory = Path.GetDirectoryName(inputCsvPath) ?? Environment.CurrentDirectory;
    return Path.Combine(inputDirectory, $"dataverse_report_{DateTimeOffset.Now:yyyyMMdd_HHmmss}.pdf");
}

static AppSettingsAppendix LoadAppSettingsAppendix(IReadOnlyDictionary<string, string> args)
{
    var sourcePath = ResolveAppSettingsPath(args);
    if (string.IsNullOrWhiteSpace(sourcePath))
    {
        return new AppSettingsAppendix
        {
            SourcePath = null,
            LoadError = "No appsettings.json found."
        };
    }

    try
    {
        var json = File.ReadAllText(sourcePath);
        var serializerOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };
        var parsed = JsonSerializer.Deserialize<MonitorAppSettings>(json, serializerOptions) ?? new MonitorAppSettings();

        var queries = new List<AppSettingsQueryInfo>();
        if (parsed.Queries is { Count: > 0 })
        {
            foreach (var configuredQuery in parsed.Queries)
            {
                if (configuredQuery is null || string.IsNullOrWhiteSpace(configuredQuery.UrlPath))
                {
                    continue;
                }

                var urlPath = configuredQuery.UrlPath.Trim();
                var queryName = ResolveConfiguredQueryName(configuredQuery.Name, urlPath);
                var method = NormalizeMethod(configuredQuery.Method);
                queries.Add(new AppSettingsQueryInfo(queryName, method, urlPath));
            }
        }

        if (queries.Count == 0 && !string.IsNullOrWhiteSpace(parsed.QueryUrl))
        {
            var legacyUrl = parsed.QueryUrl.Trim();
            queries.Add(new AppSettingsQueryInfo(
                ResolveConfiguredQueryName(parsed.QueryName, legacyUrl),
                NormalizeMethod(parsed.QueryMethod),
                legacyUrl));
        }

        return new AppSettingsAppendix
        {
            SourcePath = sourcePath,
            ResourceBaseUrl = string.IsNullOrWhiteSpace(parsed.ResourceBaseUrl)
                ? null
                : parsed.ResourceBaseUrl.Trim(),
            Queries = queries
        };
    }
    catch (Exception ex)
    {
        return new AppSettingsAppendix
        {
            SourcePath = sourcePath,
            LoadError = $"Failed to parse appsettings.json: {ex.Message}"
        };
    }
}

static string? ResolveAppSettingsPath(IReadOnlyDictionary<string, string> args)
{
    if (TryGetArg(args, out var configuredPath, "appsettings", "settings", "config"))
    {
        if (File.Exists(configuredPath))
        {
            return Path.GetFullPath(configuredPath);
        }

        if (Directory.Exists(configuredPath))
        {
            var candidateInDirectory = Path.Combine(configuredPath, "appsettings.json");
            if (File.Exists(candidateInDirectory))
            {
                return Path.GetFullPath(candidateInDirectory);
            }
        }

        throw new FileNotFoundException($"App settings not found: {configuredPath}");
    }

    var candidates = new[]
    {
        Path.Combine(Environment.CurrentDirectory, "appsettings.json"),
        Path.Combine(Environment.CurrentDirectory, "Greener.Dataverse.Monitor", "appsettings.json"),
        Path.Combine(AppContext.BaseDirectory, "appsettings.json"),
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Greener.Dataverse.Monitor", "appsettings.json"))
    };

    foreach (var candidate in candidates)
    {
        if (File.Exists(candidate))
        {
            return Path.GetFullPath(candidate);
        }
    }

    return Directory
        .EnumerateFiles(Environment.CurrentDirectory, "appsettings.json", SearchOption.AllDirectories)
        .Where(path => path.EndsWith(
            $"{Path.DirectorySeparatorChar}Greener.Dataverse.Monitor{Path.DirectorySeparatorChar}appsettings.json",
            StringComparison.OrdinalIgnoreCase))
        .OrderByDescending(File.GetLastWriteTimeUtc)
        .Select(Path.GetFullPath)
        .FirstOrDefault();
}

static string ResolveConfiguredQueryName(string? configuredName, string urlPath)
{
    if (!string.IsNullOrWhiteSpace(configuredName))
    {
        return configuredName.Trim();
    }

    if (string.IsNullOrWhiteSpace(urlPath))
    {
        return "query";
    }

    var pathWithoutQuery = urlPath.Split('?', 2)[0].Trim('/');
    if (!string.IsNullOrWhiteSpace(pathWithoutQuery))
    {
        return pathWithoutQuery;
    }

    return "query";
}

static string NormalizeMethod(string? method)
    => string.IsNullOrWhiteSpace(method)
        ? "GET"
        : method.Trim().ToUpperInvariant();

static bool TryGetArg(
    IReadOnlyDictionary<string, string> args,
    out string value,
    params string[] keys)
{
    foreach (var key in keys)
    {
        if (args.TryGetValue(key, out value!))
        {
            return true;
        }
    }

    value = string.Empty;
    return false;
}

static string? FindLatestCsvInDirectory(string directoryPath)
{
    if (!Directory.Exists(directoryPath))
    {
        return null;
    }

    return Directory
        .EnumerateFiles(directoryPath, "*.csv", SearchOption.TopDirectoryOnly)
        .OrderByDescending(File.GetLastWriteTimeUtc)
        .Select(Path.GetFullPath)
        .FirstOrDefault();
}

static List<LogEntry> LoadEntries(string csvPath)
{
    using var reader = new StreamReader(csvPath);
    var config = new CsvConfiguration(CultureInfo.InvariantCulture)
    {
        PrepareHeaderForMatch = args => args.Header.Trim().ToLowerInvariant(),
        MissingFieldFound = null,
        HeaderValidated = null,
        BadDataFound = null,
        TrimOptions = TrimOptions.Trim
    };

    using var csv = new CsvReader(reader, config);
    var rawRows = csv.GetRecords<RawLogRow>().ToList();

    var entries = new List<LogEntry>(rawRows.Count);
    foreach (var raw in rawRows)
    {
        if (!DateTimeOffset.TryParse(raw.TimestampUtc, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var timestamp))
        {
            continue;
        }

        var totalMs = ParseNullableDouble(raw.TotalMs);
        var ttfbMs = ParseNullableDouble(raw.TtfbMs);
        var status = ParseInt(raw.StatusCode);
        var eventType = string.IsNullOrWhiteSpace(raw.EventType) ? "result" : raw.EventType.Trim().ToLowerInvariant();
        var isThrottleEvent =
            string.Equals(eventType, "throttle", StringComparison.OrdinalIgnoreCase) ||
            (
                (status == 429 || status == 503) &&
                !totalMs.HasValue &&
                !string.IsNullOrWhiteSpace(raw.Error) &&
                raw.Error.Contains("Throttled on attempt", StringComparison.OrdinalIgnoreCase)
            );
        var hasThrottleSignal = isThrottleEvent || status == 429;
        var method = string.IsNullOrWhiteSpace(raw.Method) ? "GET" : raw.Method.Trim().ToUpperInvariant();
        var url = raw.Url?.Trim() ?? string.Empty;
        var queryName = ResolveQueryName(raw.QueryName, url);
        var hasError = !string.IsNullOrWhiteSpace(raw.Error) || status == 0 || status >= 400;

        entries.Add(new LogEntry(
            timestamp.ToUniversalTime(),
            queryName,
            eventType,
            isThrottleEvent,
            hasThrottleSignal,
            method,
            url,
            status,
            ttfbMs,
            totalMs,
            raw.Error ?? string.Empty,
            !hasError));
    }

    return entries;
}

static double? ParseNullableDouble(string? value)
{
    if (string.IsNullOrWhiteSpace(value))
    {
        return null;
    }

    return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
        ? parsed
        : null;
}

static int ParseInt(string? value)
    => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0;

static string ResolveQueryName(string? configuredName, string url)
{
    if (!string.IsNullOrWhiteSpace(configuredName))
    {
        return configuredName.Trim();
    }

    if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
    {
        var marker = "/api/data/v9.0/";
        var absolutePath = uri.AbsolutePath;
        var index = absolutePath.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (index >= 0)
        {
            var relative = absolutePath[(index + marker.Length)..].Trim('/');
            if (!string.IsNullOrWhiteSpace(relative))
            {
                return relative;
            }
        }
    }

    return "query";
}

static List<QueryReport> BuildReports(IReadOnlyCollection<LogEntry> entries)
{
    const int minSamplesForInference = 120;
    const int permutationIterations = 400;
    const int bootstrapIterations = 1000;
    const int quantileBootstrapIterations = 1000;

    var reports = entries
        .GroupBy(x => x.QueryName, StringComparer.OrdinalIgnoreCase)
        .Select(group =>
        {
            var groupedRows = group.OrderBy(x => x.TimestampUtc).ToList();
            var throttleEventsUtc = groupedRows
                .Where(x => x.HasThrottleSignal)
                .Select(x => x.TimestampUtc)
                .ToList();
            var rows = groupedRows
                .Where(x => !x.IsThrottleEvent)
                .ToList();

            if (rows.Count == 0)
            {
                rows = groupedRows;
            }

            var durations = rows
                .Where(x => x.TotalMs.HasValue && !double.IsNaN(x.TotalMs.Value))
                .Select(x => x.TotalMs!.Value)
                .OrderBy(x => x)
                .ToArray();

            var fastest = rows
                .Where(x => x.TotalMs.HasValue && !double.IsNaN(x.TotalMs.Value))
                .OrderBy(x => x.TotalMs)
                .FirstOrDefault();
            var slowest = rows
                .Where(x => x.TotalMs.HasValue && !double.IsNaN(x.TotalMs.Value))
                .OrderByDescending(x => x.TotalMs)
                .FirstOrDefault();

            var method = groupedRows
                .GroupBy(x => x.Method)
                .OrderByDescending(x => x.Count())
                .Select(x => x.Key)
                .FirstOrDefault() ?? "GET";

            var p50 = Percentile(durations, 0.50);
            var p90 = Percentile(durations, 0.90);
            var p95 = Percentile(durations, 0.95);
            var p99 = Percentile(durations, 0.99);
            var p95Interval = EstimateQuantileConfidenceInterval(durations, 0.95, quantileBootstrapIterations, 2026);
            var p99Interval = EstimateQuantileConfidenceInterval(durations, 0.99, quantileBootstrapIterations, 2027);
            var minuteBuckets = BuildMinuteBuckets(rows);
            var minuteEffectEtaSquared = CalculateMinuteOfHourEtaSquared(rows);
            var minuteEffectPValue = rows.Count >= minSamplesForInference
                ? CalculateMinuteOfHourPermutationPValue(rows, permutationIterations)
                : null;
            var minuteCoverage = minuteBuckets.Count(x => x.Count > 0);
            var peakMinuteSummary = BuildTopMinuteSummary(minuteBuckets);
            var medianMs = p50;
            var halfHourInference = AnalyzeHalfHourDifference(rows, medianMs, bootstrapIterations);
            var changePointInference = DetectChangePointPettitt(rows);

            return new QueryReport
            {
                QueryName = group.Key,
                Method = method,
                Rows = rows,
                Samples = rows.Count,
                SuccessCount = rows.Count(x => x.IsSuccess),
                ErrorCount = rows.Count(x => !x.IsSuccess),
                SuccessRatePct = rows.Count == 0 ? null : (double)rows.Count(x => x.IsSuccess) / rows.Count * 100d,
                AverageMs = durations.Length > 0 ? durations.Average() : null,
                MedianMs = medianMs,
                P50Ms = p50,
                P90Ms = p90,
                P95Ms = p95,
                P95CiLowMs = p95Interval.LowMs,
                P95CiHighMs = p95Interval.HighMs,
                P99Ms = p99,
                P99CiLowMs = p99Interval.LowMs,
                P99CiHighMs = p99Interval.HighMs,
                StdDevMs = StandardDeviation(durations),
                MinMs = durations.Length > 0 ? durations[0] : null,
                MaxMs = durations.Length > 0 ? durations[^1] : null,
                FirstSeenAtUtc = groupedRows.FirstOrDefault()?.TimestampUtc,
                LastSeenAtUtc = groupedRows.LastOrDefault()?.TimestampUtc,
                FastestAtUtc = fastest?.TimestampUtc,
                FastestMs = fastest?.TotalMs,
                SlowestAtUtc = slowest?.TimestampUtc,
                SlowestMs = slowest?.TotalMs,
                ThrottleEventsUtc = throttleEventsUtc,
                ThrottleEventCount = throttleEventsUtc.Count,
                MinuteBuckets = minuteBuckets,
                MinuteCoverage = minuteCoverage,
                MinuteOfHourEtaSquared = minuteEffectEtaSquared,
                PeakMinuteSummary = peakMinuteSummary,
                MinuteEffectPValue = minuteEffectPValue,
                HalfHourMedianDiffMs = halfHourInference.MedianDiffMs,
                HalfHourMedianDiffCiLowMs = halfHourInference.CiLowMs,
                HalfHourMedianDiffCiHighMs = halfHourInference.CiHighMs,
                HalfHourBiasPct = halfHourInference.BiasPct,
                HalfHourBiasLabel = halfHourInference.DirectionLabel,
                FirstHalfSamples = halfHourInference.FirstHalfCount,
                SecondHalfSamples = halfHourInference.SecondHalfCount,
                HalfHourInferenceReliable = halfHourInference.IsStatisticallyReliable,
                ChangePointAtUtc = changePointInference.ChangeAtUtc,
                ChangePointPValue = changePointInference.PValue,
                ChangePointStatisticK = changePointInference.StatisticK,
                ChangePointMedianShiftMs = changePointInference.MedianShiftMs,
                ChangePointFirstMedianMs = changePointInference.FirstSegmentMedianMs,
                ChangePointSecondMedianMs = changePointInference.SecondSegmentMedianMs,
                ChangePointFirstSegmentSamples = changePointInference.FirstSegmentSamples,
                ChangePointSecondSegmentSamples = changePointInference.SecondSegmentSamples,
                ChangePointReliable = changePointInference.IsStatisticallyReliable
            };
        })
        .OrderByDescending(x => x.Samples)
        .ThenBy(x => x.QueryName)
        .ToList();

    ApplyBenjaminiHochbergAdjustment(
        reports,
        report => report.MinuteEffectPValue,
        (report, adjusted) => report.MinuteEffectQValue = adjusted);

    ApplyBenjaminiHochbergAdjustment(
        reports,
        report => report.ChangePointPValue,
        (report, adjusted) => report.ChangePointQValue = adjusted);

    foreach (var report in reports)
    {
        var changePointScore = report.ChangePointQValue ?? report.ChangePointPValue;
        report.ChangePointReliable = changePointScore.HasValue &&
                                     changePointScore.Value < 0.05 &&
                                     report.ChangePointFirstSegmentSamples >= 8 &&
                                     report.ChangePointSecondSegmentSamples >= 8;

        report.ReaderSummary = BuildReaderSummary(report);
    }

    return reports;
}

static double? Percentile(double[] sortedAscendingValues, double percentile)
{
    if (sortedAscendingValues.Length == 0)
    {
        return null;
    }

    if (sortedAscendingValues.Length == 1)
    {
        return sortedAscendingValues[0];
    }

    var index = percentile * (sortedAscendingValues.Length - 1);
    var lowerIndex = (int)Math.Floor(index);
    var upperIndex = (int)Math.Ceiling(index);

    if (lowerIndex == upperIndex)
    {
        return sortedAscendingValues[lowerIndex];
    }

    var weight = index - lowerIndex;
    return sortedAscendingValues[lowerIndex] + (sortedAscendingValues[upperIndex] - sortedAscendingValues[lowerIndex]) * weight;
}

static double? StandardDeviation(double[] values)
{
    if (values.Length < 2)
    {
        return null;
    }

    var mean = values.Average();
    var variance = values
        .Select(x => Math.Pow(x - mean, 2))
        .Sum() / (values.Length - 1);
    return Math.Sqrt(variance);
}

static QuantileInterval EstimateQuantileConfidenceInterval(
    double[] sortedValues,
    double quantile,
    int iterations,
    int seed)
{
    if (iterations <= 0 || sortedValues.Length < 8)
    {
        return new QuantileInterval(null, null);
    }

    var random = new Random(seed);
    var estimates = new double[iterations];

    for (var iteration = 0; iteration < iterations; iteration++)
    {
        var sample = BootstrapSample(sortedValues, random);
        Array.Sort(sample);
        var estimate = Percentile(sample, quantile);
        estimates[iteration] = estimate ?? double.NaN;
    }

    var valid = estimates
        .Where(x => !double.IsNaN(x))
        .OrderBy(x => x)
        .ToArray();

    if (valid.Length < 2)
    {
        return new QuantileInterval(null, null);
    }

    return new QuantileInterval(
        Percentile(valid, 0.025),
        Percentile(valid, 0.975));
}

static ChangePointInference DetectChangePointPettitt(IReadOnlyList<LogEntry> rows)
{
    var validRows = rows
        .Where(x => x.TotalMs.HasValue && !double.IsNaN(x.TotalMs.Value))
        .OrderBy(x => x.TimestampUtc)
        .ToList();

    if (validRows.Count < 20)
    {
        return new ChangePointInference(
            null,
            null,
            null,
            null,
            null,
            null,
            0,
            0,
            false);
    }

    var n = validRows.Count;
    var values = validRows.Select(x => x.TotalMs!.Value).ToArray();
    var ranks = BuildAverageRanks(values);

    var cumulativeRankSum = 0d;
    var maxAbsStatistic = double.MinValue;
    var maxIndex = -1;

    for (var index = 0; index < n - 1; index++)
    {
        cumulativeRankSum += ranks[index];
        var t = index + 1d;
        var u = (2d * cumulativeRankSum) - (t * (n + 1d));
        var absU = Math.Abs(u);
        if (absU > maxAbsStatistic)
        {
            maxAbsStatistic = absU;
            maxIndex = index;
        }
    }

    if (maxIndex < 0)
    {
        return new ChangePointInference(
            null,
            null,
            null,
            null,
            null,
            null,
            0,
            0,
            false);
    }

    var firstCount = maxIndex + 1;
    var secondCount = n - firstCount;

    var firstSegment = values.Take(firstCount).OrderBy(x => x).ToArray();
    var secondSegment = values.Skip(firstCount).OrderBy(x => x).ToArray();
    var firstMedian = Percentile(firstSegment, 0.50);
    var secondMedian = Percentile(secondSegment, 0.50);
    double? medianShift = firstMedian.HasValue && secondMedian.HasValue
        ? secondMedian.Value - firstMedian.Value
        : null;

    var denominator = (n * n * n) + (n * n);
    var pValue = denominator <= 0
        ? (double?)null
        : Math.Min(1d, 2d * Math.Exp((-6d * maxAbsStatistic * maxAbsStatistic) / denominator));

    var changeAtIndex = Math.Min(maxIndex + 1, n - 1);
    var changeAtUtc = validRows[changeAtIndex].TimestampUtc;

    var isReliable = pValue.HasValue &&
                     pValue.Value < 0.05 &&
                     firstCount >= 8 &&
                     secondCount >= 8;

    return new ChangePointInference(
        changeAtUtc,
        pValue,
        maxAbsStatistic,
        firstMedian,
        secondMedian,
        medianShift,
        firstCount,
        secondCount,
        isReliable);
}

static double[] BuildAverageRanks(double[] values)
{
    var indexed = values
        .Select((value, index) => new RankedValue(index, value))
        .OrderBy(x => x.Value)
        .ToArray();

    var ranks = new double[values.Length];
    var position = 0;
    while (position < indexed.Length)
    {
        var start = position;
        var current = indexed[position].Value;
        while (position + 1 < indexed.Length && indexed[position + 1].Value.Equals(current))
        {
            position++;
        }

        var end = position;
        var averageRank = ((start + 1d) + (end + 1d)) / 2d;
        for (var idx = start; idx <= end; idx++)
        {
            ranks[indexed[idx].OriginalIndex] = averageRank;
        }

        position++;
    }

    return ranks;
}

static void ApplyBenjaminiHochbergAdjustment(
    IReadOnlyList<QueryReport> reports,
    Func<QueryReport, double?> pValueSelector,
    Action<QueryReport, double?> qValueSetter)
{
    var indexed = reports
        .Select((report, index) => new IndexedPValue(index, pValueSelector(report)))
        .Where(x => x.PValue.HasValue)
        .Select(x => new IndexedPValue(x.Index, Math.Clamp(x.PValue!.Value, 0d, 1d)))
        .OrderBy(x => x.PValue!.Value)
        .ToList();

    if (indexed.Count == 0)
    {
        return;
    }

    var m = indexed.Count;
    var adjusted = new double[m];
    for (var rank = 0; rank < m; rank++)
    {
        var p = indexed[rank].PValue!.Value;
        adjusted[rank] = Math.Min(1d, p * m / (rank + 1d));
    }

    for (var idx = m - 2; idx >= 0; idx--)
    {
        adjusted[idx] = Math.Min(adjusted[idx], adjusted[idx + 1]);
    }

    for (var idx = 0; idx < m; idx++)
    {
        qValueSetter(reports[indexed[idx].Index], adjusted[idx]);
    }
}

static List<MinuteBucket> BuildMinuteBuckets(IReadOnlyCollection<LogEntry> rows)
{
    var byMinute = rows
        .Where(x => x.TotalMs.HasValue && !double.IsNaN(x.TotalMs.Value))
        .GroupBy(x => x.TimestampUtc.Minute)
        .ToDictionary(
            g => g.Key,
            g => g.Select(x => x.TotalMs!.Value).OrderBy(v => v).ToArray());

    var result = new List<MinuteBucket>(60);
    for (var minute = 0; minute < 60; minute++)
    {
        if (!byMinute.TryGetValue(minute, out var values) || values.Length == 0)
        {
            result.Add(new MinuteBucket(minute, 0, null, null));
            continue;
        }

        result.Add(new MinuteBucket(
            minute,
            values.Length,
            values.Average(),
            Percentile(values, 0.50)));
    }

    return result;
}

static double? CalculateMinuteOfHourEtaSquared(IReadOnlyCollection<LogEntry> rows)
{
    var valid = rows
        .Where(x => x.TotalMs.HasValue && !double.IsNaN(x.TotalMs.Value))
        .Select(x => (Minute: x.TimestampUtc.Minute, Value: x.TotalMs!.Value))
        .ToArray();

    if (valid.Length < 2)
    {
        return null;
    }

    var minutes = valid.Select(x => x.Minute).ToArray();
    var values = valid.Select(x => x.Value).ToArray();
    return CalculateEtaSquared(minutes, values);
}

static double? CalculateMinuteOfHourPermutationPValue(IReadOnlyCollection<LogEntry> rows, int iterations)
{
    if (iterations <= 0)
    {
        return null;
    }

    var valid = rows
        .Where(x => x.TotalMs.HasValue && !double.IsNaN(x.TotalMs.Value))
        .Select(x => (Minute: x.TimestampUtc.Minute, Value: x.TotalMs!.Value))
        .ToArray();

    if (valid.Length < 2)
    {
        return null;
    }

    var minutes = valid.Select(x => x.Minute).ToArray();
    var values = valid.Select(x => x.Value).ToArray();
    var observedEtaSquared = CalculateEtaSquared(minutes, values);
    if (!observedEtaSquared.HasValue)
    {
        return null;
    }

    var shuffledMinutes = new int[minutes.Length];
    var random = new Random(42);
    var exceedCount = 0;

    for (var iteration = 0; iteration < iterations; iteration++)
    {
        Array.Copy(minutes, shuffledMinutes, minutes.Length);
        ShuffleInPlace(shuffledMinutes, random);
        var permutedEtaSquared = CalculateEtaSquared(shuffledMinutes, values);
        if (permutedEtaSquared.HasValue && permutedEtaSquared.Value >= observedEtaSquared.Value)
        {
            exceedCount++;
        }
    }

    return (exceedCount + 1d) / (iterations + 1d);
}

static double? CalculateEtaSquared(int[] groups, double[] values)
{
    if (groups.Length != values.Length || values.Length < 2)
    {
        return null;
    }

    var overallMean = values.Average();
    var sst = values.Sum(x => Math.Pow(x - overallMean, 2));
    if (sst <= double.Epsilon)
    {
        return null;
    }

    var bucketCount = new int[60];
    var bucketSum = new double[60];

    for (var index = 0; index < values.Length; index++)
    {
        var minute = Math.Clamp(groups[index], 0, 59);
        bucketCount[minute]++;
        bucketSum[minute] += values[index];
    }

    var ssb = 0d;
    for (var minute = 0; minute < 60; minute++)
    {
        if (bucketCount[minute] == 0)
        {
            continue;
        }

        var mean = bucketSum[minute] / bucketCount[minute];
        ssb += bucketCount[minute] * Math.Pow(mean - overallMean, 2);
    }

    return ssb / sst;
}

static string BuildTopMinuteSummary(IEnumerable<MinuteBucket> buckets)
{
    var top = buckets
        .Where(x => x.Count > 0 && x.MedianMs.HasValue)
        .OrderByDescending(x => x.MedianMs)
        .ThenByDescending(x => x.Count)
        .Take(3)
        .ToList();

    if (top.Count == 0)
    {
        return "-";
    }

    return string.Join(", ", top.Select(x => $"m{x.Minute:00} ({x.MedianMs:0.###} ms, n={x.Count})"));
}

static HalfHourInference AnalyzeHalfHourDifference(
    IReadOnlyCollection<LogEntry> rows,
    double? overallMedianMs,
    int bootstrapIterations)
{
    var firstHalf = rows
        .Where(x => x.TotalMs.HasValue && !double.IsNaN(x.TotalMs.Value) && x.TimestampUtc.Minute < 30)
        .Select(x => x.TotalMs!.Value)
        .ToArray();
    var secondHalf = rows
        .Where(x => x.TotalMs.HasValue && !double.IsNaN(x.TotalMs.Value) && x.TimestampUtc.Minute >= 30)
        .Select(x => x.TotalMs!.Value)
        .ToArray();

    var firstHalfCount = firstHalf.Length;
    var secondHalfCount = secondHalf.Length;

    if (firstHalfCount == 0 || secondHalfCount == 0)
    {
        return new HalfHourInference(null, null, null, null, "-", firstHalfCount, secondHalfCount, false);
    }

    var firstHalfSorted = firstHalf.OrderBy(x => x).ToArray();
    var secondHalfSorted = secondHalf.OrderBy(x => x).ToArray();
    var firstMedian = Percentile(firstHalfSorted, 0.50);
    var secondMedian = Percentile(secondHalfSorted, 0.50);
    if (!firstMedian.HasValue || !secondMedian.HasValue)
    {
        return new HalfHourInference(null, null, null, null, "-", firstHalfCount, secondHalfCount, false);
    }

    var medianDiffMs = secondMedian.Value - firstMedian.Value;
    double? biasPct = overallMedianMs.HasValue && Math.Abs(overallMedianMs.Value) > double.Epsilon
        ? (medianDiffMs / overallMedianMs.Value) * 100d
        : null;

    var directionLabel = Math.Abs(medianDiffMs) < 0.1
        ? "balanced"
        : medianDiffMs > 0
            ? "second half slower"
            : "first half slower";

    double? ciLowMs = null;
    double? ciHighMs = null;
    if (bootstrapIterations > 0 && firstHalfCount >= 8 && secondHalfCount >= 8)
    {
        var random = new Random(1337);
        var bootstrapDiffs = new double[bootstrapIterations];
        for (var iteration = 0; iteration < bootstrapIterations; iteration++)
        {
            var firstSample = BootstrapSample(firstHalf, random);
            var secondSample = BootstrapSample(secondHalf, random);
            Array.Sort(firstSample);
            Array.Sort(secondSample);
            var firstSampleMedian = Percentile(firstSample, 0.50) ?? 0d;
            var secondSampleMedian = Percentile(secondSample, 0.50) ?? 0d;
            bootstrapDiffs[iteration] = secondSampleMedian - firstSampleMedian;
        }

        Array.Sort(bootstrapDiffs);
        ciLowMs = Percentile(bootstrapDiffs, 0.025);
        ciHighMs = Percentile(bootstrapDiffs, 0.975);
    }

    var ciExcludesZero = ciLowMs.HasValue && ciHighMs.HasValue &&
                         (ciLowMs.Value > 0 || ciHighMs.Value < 0);
    var hasAdequateSample = firstHalfCount >= 30 && secondHalfCount >= 30;
    var isStatisticallyReliable = hasAdequateSample && ciExcludesZero;

    return new HalfHourInference(
        medianDiffMs,
        ciLowMs,
        ciHighMs,
        biasPct,
        directionLabel,
        firstHalfCount,
        secondHalfCount,
        isStatisticallyReliable);
}

static List<string> BuildReaderSummary(QueryReport report)
{
    var summary = new List<string>();

    if (report.Samples > 0)
    {
        var errorRate = (double)report.ErrorCount / report.Samples * 100d;
        if (report.ErrorCount == 0)
        {
            summary.Add($"All {report.Samples} requests succeeded in the observed window.");
        }
        else
        {
            summary.Add($"{errorRate:0.##}% of requests failed ({report.ErrorCount}/{report.Samples}).");
        }
    }

    if (report.ThrottleEventCount > 0)
    {
        summary.Add($"{report.ThrottleEventCount} throttling event(s) were observed and marked in red in the timeline.");
    }
    else
    {
        summary.Add("No throttling events were observed.");
    }

    if (report.P95Ms.HasValue)
    {
        var p95Text = report.P95CiLowMs.HasValue && report.P95CiHighMs.HasValue
            ? $"{FormatMs(report.P95Ms)} ms (95% CI {FormatMsRange(report.P95CiLowMs, report.P95CiHighMs)})"
            : $"{FormatMs(report.P95Ms)} ms";
        summary.Add($"Estimated P95 latency is {p95Text}.");
    }

    if (report.P99Ms.HasValue)
    {
        var p99Text = report.P99CiLowMs.HasValue && report.P99CiHighMs.HasValue
            ? $"{FormatMs(report.P99Ms)} ms (95% CI {FormatMsRange(report.P99CiLowMs, report.P99CiHighMs)})"
            : $"{FormatMs(report.P99Ms)} ms";
        summary.Add($"Estimated P99 latency is {p99Text}.");
    }

    if (report.MinuteOfHourEtaSquared.HasValue && report.MinuteEffectPValue.HasValue)
    {
        var adjustedScore = report.MinuteEffectQValue ?? report.MinuteEffectPValue;
        var scoreLabel = report.MinuteEffectQValue.HasValue ? "q" : "p";
        if (adjustedScore.HasValue && adjustedScore.Value < 0.05)
        {
            summary.Add(
                $"Minute-of-hour impact is statistically significant after multiplicity control ({scoreLabel}={FormatPValue(adjustedScore)}; eta-squared {FormatPct(report.MinuteOfHourEtaSquared * 100d)}).");
        }
        else
        {
            summary.Add(
                $"No statistically significant minute-of-hour impact was detected after multiplicity control ({scoreLabel}={FormatPValue(adjustedScore)}).");
        }
    }
    else
    {
        summary.Add("Not enough data to make a statistically reliable minute-of-hour statement.");
    }

    if (report.HalfHourMedianDiffMs.HasValue)
    {
        if (report.HalfHourInferenceReliable &&
            report.HalfHourMedianDiffCiLowMs.HasValue &&
            report.HalfHourMedianDiffCiHighMs.HasValue)
        {
            var direction = report.HalfHourMedianDiffMs.Value >= 0
                ? "second half of the hour tends to be slower"
                : "first half of the hour tends to be slower";
            summary.Add(
                $"{direction}: median difference {FormatSignedMs(report.HalfHourMedianDiffMs)} (95% CI {FormatMsInterval(report.HalfHourMedianDiffCiLowMs, report.HalfHourMedianDiffCiHighMs)}).");
        }
        else if (report.HalfHourMedianDiffCiLowMs.HasValue && report.HalfHourMedianDiffCiHighMs.HasValue)
        {
            summary.Add(
                $"Half-hour difference is not statistically reliable yet (95% CI {FormatMsInterval(report.HalfHourMedianDiffCiLowMs, report.HalfHourMedianDiffCiHighMs)}).");
        }
        else
        {
            summary.Add("Half-hour difference exists in the sample, but confidence interval could not be estimated reliably.");
        }
    }

    if (report.ChangePointAtUtc.HasValue && report.ChangePointPValue.HasValue)
    {
        var adjustedScore = report.ChangePointQValue ?? report.ChangePointPValue;
        var scoreLabel = report.ChangePointQValue.HasValue ? "q" : "p";

        if (adjustedScore.HasValue && adjustedScore.Value < 0.05 && report.ChangePointMedianShiftMs.HasValue)
        {
            summary.Add(
                $"A likely structural break was detected around {FormatUtcTimestamp(report.ChangePointAtUtc)} (median shift {FormatSignedMs(report.ChangePointMedianShiftMs)}, {scoreLabel}={FormatPValue(adjustedScore)}).");
        }
        else
        {
            summary.Add(
                $"No statistically robust structural break was detected in the timeline ({scoreLabel}={FormatPValue(adjustedScore)}).");
        }
    }
    else
    {
        summary.Add("Not enough data to run change-point detection reliably.");
    }

    return summary;
}

static double[] BootstrapSample(IReadOnlyList<double> source, Random random)
{
    var result = new double[source.Count];
    for (var index = 0; index < result.Length; index++)
    {
        result[index] = source[random.Next(source.Count)];
    }

    return result;
}

static void ShuffleInPlace(int[] values, Random random)
{
    for (var index = values.Length - 1; index > 0; index--)
    {
        var swapIndex = random.Next(index + 1);
        (values[index], values[swapIndex]) = (values[swapIndex], values[index]);
    }
}

static void GenerateCharts(IEnumerable<QueryReport> reports, string chartsDirectory)
{
    foreach (var report in reports)
    {
        var safeName = MakeSafeFileName(report.QueryName);
        var timeChartPath = Path.Combine(chartsDirectory, $"{safeName}_timeline.png");
        var histogramPath = Path.Combine(chartsDirectory, $"{safeName}_histogram.png");
        var minuteProfilePath = Path.Combine(chartsDirectory, $"{safeName}_minute_profile.png");

        GenerateTimeChart(report, timeChartPath);
        GenerateHistogram(report, histogramPath);
        GenerateMinuteProfileChart(report, minuteProfilePath);

        report.TimelineChartPath = timeChartPath;
        report.HistogramChartPath = histogramPath;
        report.MinuteProfileChartPath = minuteProfilePath;
    }
}

static void GenerateTimeChart(QueryReport report, string outputPath)
{
    const int maxTimeAxisTicks = 50;

    var rows = report.Rows
        .Where(x => x.TotalMs.HasValue && !double.IsNaN(x.TotalMs.Value))
        .OrderBy(x => x.TimestampUtc)
        .ToList();
    var throttledXs = report.ThrottleEventsUtc
        .OrderBy(x => x)
        .Select(x => x.UtcDateTime.ToOADate())
        .ToArray();

    var plot = new Plot();
    if (rows.Count > 0)
    {
        var xs = rows.Select(x => x.TimestampUtc.UtcDateTime.ToOADate()).ToArray();
        var ys = rows.Select(x => x.TotalMs!.Value).ToArray();

        var scatter = plot.Add.Scatter(xs, ys);
        scatter.LineWidth = 1;
        scatter.MarkerSize = 3;
    }

    if (throttledXs.Length > 0)
    {
        var markerY = rows.Count > 0
            ? rows.Max(x => x.TotalMs!.Value) * 1.03
            : 1d;
        if (markerY <= 0)
        {
            markerY = 1d;
        }

        var throttledYs = Enumerable.Repeat(markerY, throttledXs.Length).ToArray();
        var throttledScatter = plot.Add.Scatter(throttledXs, throttledYs);
        throttledScatter.LineWidth = 0;
        throttledScatter.MarkerSize = 8;
        throttledScatter.Color = ScottPlot.Colors.Red;
    }

    var timeAxis = plot.Axes.DateTimeTicksBottom();
    timeAxis.TickGenerator.MaxTickCount = maxTimeAxisTicks;
    if (rows.Count > 0 || throttledXs.Length > 0)
    {
        plot.Axes.AutoScale();
    }

    plot.Title($"{report.QueryName} - Timeline");
    plot.XLabel("Time (UTC)");
    plot.YLabel("Total (ms)");
    plot.SavePng(outputPath, 1280, 720);
}

static void GenerateHistogram(QueryReport report, string outputPath)
{
    var values = report.Rows
        .Where(x => x.TotalMs.HasValue && !double.IsNaN(x.TotalMs.Value))
        .Select(x => x.TotalMs!.Value)
        .ToArray();

    var plot = new Plot();
    if (values.Length > 0)
    {
        var (centers, counts) = BuildHistogram(values);
        plot.Add.Bars(centers, counts.Select(x => (double)x).ToArray());
        plot.Axes.AutoScale();
    }

    plot.Title($"{report.QueryName} - Histogram");
    plot.XLabel("Total (ms)");
    plot.YLabel("Count");
    plot.SavePng(outputPath, 1280, 720);
}

static void GenerateMinuteProfileChart(QueryReport report, string outputPath)
{
    var plot = new Plot();

    var validBuckets = report.MinuteBuckets
        .Where(x => x.MedianMs.HasValue)
        .OrderBy(x => x.Minute)
        .ToList();

    if (validBuckets.Count > 0)
    {
        var xs = validBuckets.Select(x => (double)x.Minute).ToArray();
        var ys = validBuckets.Select(x => x.MedianMs!.Value).ToArray();
        var bars = plot.Add.Bars(xs, ys);
        bars.ValueLabelStyle.IsVisible = false;
    }

    plot.Axes.SetLimitsX(-0.5, 59.5);
    plot.Axes.AutoScaleY();
    plot.Title($"{report.QueryName} - Minute-of-hour profile");
    plot.XLabel("Minute of hour (0-59)");
    plot.YLabel("Median total latency (ms)");
    plot.SavePng(outputPath, 1280, 720);
}

static (double[] Centers, int[] Counts) BuildHistogram(double[] values)
{
    if (values.Length == 1)
    {
        return (new[] { values[0] }, new[] { 1 });
    }

    var min = values.Min();
    var max = values.Max();
    if (Math.Abs(max - min) < double.Epsilon)
    {
        max = min + 1;
    }

    var bins = Math.Clamp((int)Math.Sqrt(values.Length), 8, 24);
    var binWidth = (max - min) / bins;

    var counts = new int[bins];
    foreach (var value in values)
    {
        var idx = (int)((value - min) / binWidth);
        if (idx >= bins)
        {
            idx = bins - 1;
        }

        if (idx < 0)
        {
            idx = 0;
        }

        counts[idx]++;
    }

    var centers = Enumerable.Range(0, bins)
        .Select(i => min + (i + 0.5) * binWidth)
        .ToArray();

    return (centers, counts);
}

static void GeneratePdfReport(
    string inputCsvPath,
    string outputPdfPath,
    IReadOnlyList<QueryReport> reports,
    IReadOnlyCollection<LogEntry> entries,
    AppSettingsAppendix appSettingsAppendix)
{
    var orderedEntries = entries.OrderBy(x => x.TimestampUtc).ToList();
    var resultEntries = orderedEntries.Where(x => !x.IsThrottleEvent).ToList();
    var throttleEventsCount = orderedEntries.Count(x => x.HasThrottleSignal);
    var periodStartUtc = orderedEntries.FirstOrDefault()?.TimestampUtc;
    var periodEndUtc = orderedEntries.LastOrDefault()?.TimestampUtc;
    var testDuration = periodStartUtc.HasValue && periodEndUtc.HasValue
        ? periodEndUtc.Value - periodStartUtc.Value
        : (TimeSpan?)null;

    var totalRows = orderedEntries.Count;
    var resultRows = resultEntries.Count;
    var successCount = resultEntries.Count(x => x.IsSuccess);
    var errorCount = resultEntries.Count(x => !x.IsSuccess);
    var successRate = resultRows == 0 ? (double?)null : (double)successCount / resultRows * 100d;

    var globalDurations = resultEntries
        .Where(x => x.TotalMs.HasValue && !double.IsNaN(x.TotalMs.Value))
        .Select(x => x.TotalMs!.Value)
        .OrderBy(x => x)
        .ToArray();
    var globalMinMs = globalDurations.Length == 0 ? (double?)null : globalDurations[0];
    var globalMaxMs = globalDurations.Length == 0 ? (double?)null : globalDurations[^1];
    var globalAverageMs = globalDurations.Length == 0 ? (double?)null : globalDurations.Average();
    var globalMedianMs = Percentile(globalDurations, 0.50);
    var globalP95Ms = Percentile(globalDurations, 0.95);
    var globalP99Ms = Percentile(globalDurations, 0.99);
    var globalStdDevMs = StandardDeviation(globalDurations);

    var globalFastest = resultEntries
        .Where(x => x.TotalMs.HasValue && !double.IsNaN(x.TotalMs.Value))
        .OrderBy(x => x.TotalMs)
        .FirstOrDefault();
    var globalSlowest = resultEntries
        .Where(x => x.TotalMs.HasValue && !double.IsNaN(x.TotalMs.Value))
        .OrderByDescending(x => x.TotalMs)
        .FirstOrDefault();

    Document.Create(document =>
    {
        document.Page(page =>
        {
            page.Margin(25);
            page.Header()
                .Text("Dataverse Monitoring Report")
                .FontSize(24)
                .Bold();

            page.Content().Column(col =>
            {
                col.Spacing(8);
                col.Item().Text($"Created: {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss} UTC");
                col.Item().Text($"Input: {inputCsvPath}");
                col.Item().Text($"Test period (UTC): {FormatPeriod(periodStartUtc, periodEndUtc)}");
                col.Item().Text($"Test duration: {FormatDuration(testDuration)}");
                col.Item().Text($"Records: {totalRows} (result rows: {resultRows})");
                col.Item().Text($"Throttle events: {throttleEventsCount}");
                col.Item().Text($"Queries: {reports.Count}");
                col.Item().Text($"Success rate: {FormatPct(successRate)} ({successCount}/{resultRows})");
                col.Item().Text("Multiplicity control: Benjamini-Hochberg FDR adjustment is applied across query-level p-values.");
                col.Item().Text($"Fastest data point: {FormatExtreme(globalFastest)}");
                col.Item().Text($"Slowest data point: {FormatExtreme(globalSlowest)}");

                col.Item().PaddingTop(10).Text("Global latency statistics (total ms)").Bold();
                col.Item().Table(table =>
                {
                    table.ColumnsDefinition(columns =>
                    {
                        columns.RelativeColumn(1.4f);
                        columns.RelativeColumn(1.4f);
                        columns.RelativeColumn(1.4f);
                        columns.RelativeColumn(1.4f);
                        columns.RelativeColumn(1.4f);
                        columns.RelativeColumn(1.4f);
                        columns.RelativeColumn(1.4f);
                    });

                    AddHeaderCell(table, "Min ms");
                    AddHeaderCell(table, "Median ms");
                    AddHeaderCell(table, "Avg ms");
                    AddHeaderCell(table, "StdDev ms");
                    AddHeaderCell(table, "P95 ms");
                    AddHeaderCell(table, "P99 ms");
                    AddHeaderCell(table, "Max ms");

                    AddBodyCell(table, FormatMs(globalMinMs));
                    AddBodyCell(table, FormatMs(globalMedianMs));
                    AddBodyCell(table, FormatMs(globalAverageMs));
                    AddBodyCell(table, FormatMs(globalStdDevMs));
                    AddBodyCell(table, FormatMs(globalP95Ms));
                    AddBodyCell(table, FormatMs(globalP99Ms));
                    AddBodyCell(table, FormatMs(globalMaxMs));
                });

                col.Item().PaddingTop(10).Text("Queries with strongest within-hour effect").Bold();
                col.Item().Table(table =>
                {
                    table.ColumnsDefinition(columns =>
                    {
                        columns.RelativeColumn(2.2f);
                        columns.RelativeColumn(0.9f);
                        columns.RelativeColumn(0.9f);
                        columns.RelativeColumn(0.9f);
                        columns.RelativeColumn(0.9f);
                        columns.RelativeColumn(2.2f);
                    });

                    AddHeaderCell(table, "Query");
                    AddHeaderCell(table, "Eta²");
                    AddHeaderCell(table, "p-value");
                    AddHeaderCell(table, "q-value");
                    AddHeaderCell(table, "Coverage");
                    AddHeaderCell(table, "Top minute medians");

                    foreach (var item in reports
                                 .Where(x => x.MinuteOfHourEtaSquared.HasValue)
                                 .OrderByDescending(x => x.MinuteOfHourEtaSquared)
                                 .Take(8))
                    {
                        AddBodyCell(table, item.QueryName);
                        AddBodyCell(table, FormatPct(item.MinuteOfHourEtaSquared * 100d));
                        AddBodyCell(table, FormatPValue(item.MinuteEffectPValue));
                        AddBodyCell(table, FormatPValue(item.MinuteEffectQValue));
                        AddBodyCell(table, $"{item.MinuteCoverage}/60");
                        AddBodyCell(table, item.PeakMinuteSummary);
                    }
                });

                col.Item().PaddingTop(8).Text("Queries with strongest structural break signal (Pettitt)").Bold();
                col.Item().Table(table =>
                {
                    table.ColumnsDefinition(columns =>
                    {
                        columns.RelativeColumn(2.2f);
                        columns.RelativeColumn(0.9f);
                        columns.RelativeColumn(0.9f);
                        columns.RelativeColumn(1.6f);
                        columns.RelativeColumn(1.2f);
                    });

                    AddHeaderCell(table, "Query");
                    AddHeaderCell(table, "p-value");
                    AddHeaderCell(table, "q-value");
                    AddHeaderCell(table, "Change at (UTC)");
                    AddHeaderCell(table, "Median shift");

                    foreach (var item in reports
                                 .Where(x => x.ChangePointPValue.HasValue)
                                 .OrderBy(x => x.ChangePointPValue)
                                 .Take(8))
                    {
                        AddBodyCell(table, item.QueryName);
                        AddBodyCell(table, FormatPValue(item.ChangePointPValue));
                        AddBodyCell(table, FormatPValue(item.ChangePointQValue));
                        AddBodyCell(table, FormatUtcTimestamp(item.ChangePointAtUtc));
                        AddBodyCell(table, FormatSignedMs(item.ChangePointMedianShiftMs));
                    }
                });
            });
        });

        for (var reportIndex = 0; reportIndex < reports.Count; reportIndex++)
        {
            var report = reports[reportIndex];

            document.Page(page =>
            {
                page.Margin(20);
                page.Header().Column(header =>
                {
                    header.Item().Text($"{report.QueryName}").FontSize(18).Bold();
                    header.Item().Text($"{report.Method} | Samples: {report.Samples} | Errors: {report.ErrorCount} | Success: {FormatPct(report.SuccessRatePct)} | Overview");
                });

                page.Content().Column(col =>
                {
                    col.Spacing(8);
                    col.Item().Text($"Observation window (UTC): {FormatPeriod(report.FirstSeenAtUtc, report.LastSeenAtUtc)}");
                    col.Item().PaddingTop(4).Text("Latency metrics").Bold();
                    col.Item().Row(row =>
                    {
                        row.Spacing(8);
                        row.RelativeItem().Element(container => AddStatCard(container, "Min ms", FormatMs(report.MinMs)));
                        row.RelativeItem().Element(container => AddStatCard(container, "Median ms", FormatMs(report.MedianMs)));
                        row.RelativeItem().Element(container => AddStatCard(container, "Avg ms", FormatMs(report.AverageMs)));
                        row.RelativeItem().Element(container => AddStatCard(container, "StdDev ms", FormatMs(report.StdDevMs)));
                    });
                    col.Item().Row(row =>
                    {
                        row.Spacing(8);
                        row.RelativeItem().Element(container => AddStatCard(container, "P90 ms", FormatMs(report.P90Ms)));
                        row.RelativeItem().Element(container => AddStatCard(container, "P95 ms", FormatMs(report.P95Ms)));
                        row.RelativeItem().Element(container => AddStatCard(container, "P99 ms", FormatMs(report.P99Ms)));
                        row.RelativeItem().Element(container => AddStatCard(container, "Max ms", FormatMs(report.MaxMs)));
                    });
                    col.Item().Text($"P95 95% CI: {FormatMsRange(report.P95CiLowMs, report.P95CiHighMs)}");
                    col.Item().Text($"P99 95% CI: {FormatMsRange(report.P99CiLowMs, report.P99CiHighMs)}");

                    col.Item().PaddingTop(2).Text("Volume / quality").Bold();
                    col.Item().Row(row =>
                    {
                        row.Spacing(8);
                        row.RelativeItem().Element(container => AddStatCard(container, "Samples", report.Samples.ToString(CultureInfo.InvariantCulture)));
                        row.RelativeItem().Element(container => AddStatCard(container, "Success", report.SuccessCount.ToString(CultureInfo.InvariantCulture)));
                        row.RelativeItem().Element(container => AddStatCard(container, "Errors", report.ErrorCount.ToString(CultureInfo.InvariantCulture)));
                        row.RelativeItem().Element(container => AddStatCard(container, "Success %", FormatPct(report.SuccessRatePct)));
                    });
                    col.Item().Text($"Throttle events observed: {report.ThrottleEventCount}");

                    col.Item().Text($"Fastest at: {FormatMoment(report.FastestAtUtc, report.FastestMs)}");
                    col.Item().Text($"Slowest at: {FormatMoment(report.SlowestAtUtc, report.SlowestMs)}");
                });
            });

            document.Page(page =>
            {
                page.Margin(20);
                page.Header().Column(header =>
                {
                    header.Item().Text($"{report.QueryName}").FontSize(18).Bold();
                    header.Item().Text($"{report.Method} | Statistical diagnostics");
                });

                page.Content().Column(col =>
                {
                    col.Spacing(8);
                    col.Item().Text($"Observation window (UTC): {FormatPeriod(report.FirstSeenAtUtc, report.LastSeenAtUtc)}");

                    col.Item().PaddingTop(2).Text("Within-hour pattern analysis").Bold();
                    col.Item().Text($"Minute-of-hour effect (ANOVA eta²): {FormatPct(report.MinuteOfHourEtaSquared * 100d)}");
                    col.Item().Text($"Minute-of-hour permutation p-value: {FormatPValue(report.MinuteEffectPValue)}");
                    col.Item().Text($"Minute-of-hour FDR q-value: {FormatPValue(report.MinuteEffectQValue)}");
                    col.Item().Text($"Half-hour median delta (H2 - H1): {FormatSignedMs(report.HalfHourMedianDiffMs)}");
                    col.Item().Text($"Half-hour median delta 95% CI: {FormatMsInterval(report.HalfHourMedianDiffCiLowMs, report.HalfHourMedianDiffCiHighMs)}");
                    col.Item().Text($"Half-hour bias (relative to overall median): {FormatSignedPct(report.HalfHourBiasPct)} ({report.HalfHourBiasLabel})");
                    col.Item().Text($"Half-hour sample split: H1 n={report.FirstHalfSamples}, H2 n={report.SecondHalfSamples}");
                    col.Item().Text($"Half-hour inference quality: {(report.HalfHourInferenceReliable ? "statistically reliable" : "not statistically reliable")}");
                    col.Item().Text($"Minute coverage: {report.MinuteCoverage}/60");
                    col.Item().Text($"Top minute medians: {report.PeakMinuteSummary}");

                    col.Item().PaddingTop(2).Text("Change-point analysis (Pettitt)").Bold();
                    col.Item().Text($"Change point at: {FormatUtcTimestamp(report.ChangePointAtUtc)}");
                    col.Item().Text($"Change-point p-value: {FormatPValue(report.ChangePointPValue)}");
                    col.Item().Text($"Change-point FDR q-value: {FormatPValue(report.ChangePointQValue)}");
                    col.Item().Text($"Median shift (after - before): {FormatSignedMs(report.ChangePointMedianShiftMs)}");
                    col.Item().Text($"Segment medians: before {FormatMs(report.ChangePointFirstMedianMs)} ms, after {FormatMs(report.ChangePointSecondMedianMs)} ms");
                    col.Item().Text($"Segment split: before n={report.ChangePointFirstSegmentSamples}, after n={report.ChangePointSecondSegmentSamples}");
                    col.Item().Text($"Change-point inference quality: {(report.ChangePointReliable ? "statistically reliable" : "not statistically reliable")}");

                    col.Item().PaddingTop(2).Text("Interpretation (plain language)").Bold();
                    foreach (var statement in report.ReaderSummary)
                    {
                        col.Item().Text($"- {statement}");
                    }
                });
            });

            if (File.Exists(report.TimelineChartPath))
            {
                document.Page(page =>
                {
                    page.Margin(20);
                    page.Header().Column(header =>
                    {
                        header.Item().Text($"{report.QueryName}").FontSize(18).Bold();
                        header.Item().Text("Timeline chart");
                    });
                    page.Content().Column(col =>
                    {
                        col.Spacing(6);
                        col.Item().Text($"Observation window (UTC): {FormatPeriod(report.FirstSeenAtUtc, report.LastSeenAtUtc)}");
                        col.Item().PaddingTop(4).Image(report.TimelineChartPath).FitWidth();
                    });
                });
            }

            if (File.Exists(report.HistogramChartPath))
            {
                document.Page(page =>
                {
                    page.Margin(20);
                    page.Header().Column(header =>
                    {
                        header.Item().Text($"{report.QueryName}").FontSize(18).Bold();
                        header.Item().Text("Histogram");
                    });
                    page.Content().Column(col =>
                    {
                        col.Spacing(6);
                        col.Item().Text($"Observation window (UTC): {FormatPeriod(report.FirstSeenAtUtc, report.LastSeenAtUtc)}");
                        col.Item().PaddingTop(4).Image(report.HistogramChartPath).FitWidth();
                    });
                });
            }

            if (File.Exists(report.MinuteProfileChartPath))
            {
                document.Page(page =>
                {
                    page.Margin(20);
                    page.Header().Column(header =>
                    {
                        header.Item().Text($"{report.QueryName}").FontSize(18).Bold();
                        header.Item().Text("Minute-of-hour profile (median by minute)");
                    });
                    page.Content().Column(col =>
                    {
                        col.Spacing(6);
                        col.Item().Text($"Observation window (UTC): {FormatPeriod(report.FirstSeenAtUtc, report.LastSeenAtUtc)}");
                        col.Item().PaddingTop(4).Image(report.MinuteProfileChartPath).FitWidth();
                    });
                });
            }
        }

        document.Page(page =>
        {
            page.Margin(20);
            page.Header().Column(header =>
            {
                header.Item().Text("Appendix").FontSize(18).Bold();
                header.Item().Text("Configured queries from appsettings.json");
            });

            page.Footer()
                .AlignCenter()
                .Text("DataVerse diagnostic tool by greener software GmbH")
                .FontSize(10)
                .FontColor(QuestPDF.Helpers.Colors.Grey.Darken1);

            page.Content().Column(col =>
            {
                col.Spacing(8);
                col.Item().Text($"Source file: {appSettingsAppendix.SourcePath ?? "-"}");
                col.Item().Text($"ResourceBaseUrl: {appSettingsAppendix.ResourceBaseUrl ?? "-"}");
                col.Item().Text($"Configured query count: {appSettingsAppendix.Queries.Count}");

                if (!string.IsNullOrWhiteSpace(appSettingsAppendix.LoadError))
                {
                    col.Item().PaddingTop(4).Text($"Note: {appSettingsAppendix.LoadError}")
                        .FontColor(QuestPDF.Helpers.Colors.Red.Darken2);
                }
                else if (appSettingsAppendix.Queries.Count == 0)
                {
                    col.Item().PaddingTop(4).Text("No query definitions found in the selected appsettings.json.");
                }
                else
                {
                    col.Item().PaddingTop(6).Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.RelativeColumn(0.45f);
                            columns.RelativeColumn(1.4f);
                            columns.RelativeColumn(0.7f);
                            columns.RelativeColumn(4.2f);
                        });

                        AddHeaderCell(table, "#");
                        AddHeaderCell(table, "Name");
                        AddHeaderCell(table, "Method");
                        AddHeaderCell(table, "UrlPath");

                        for (var index = 0; index < appSettingsAppendix.Queries.Count; index++)
                        {
                            var query = appSettingsAppendix.Queries[index];
                            AddBodyCell(table, (index + 1).ToString(CultureInfo.InvariantCulture));
                            AddBodyCell(table, query.Name);
                            AddBodyCell(table, query.Method);
                            AddBodyCell(table, WrapLongTextForPdf(query.UrlPath, 120));
                        }
                    });
                }
            });
        });
    }).GeneratePdf(outputPdfPath);
}

static string MakeSafeFileName(string value)
{
    var invalidChars = Path.GetInvalidFileNameChars();
    var chars = value.Select(c => invalidChars.Contains(c) ? '_' : c).ToArray();
    return new string(chars);
}

static string WrapLongTextForPdf(string value, int chunkLength)
{
    if (string.IsNullOrWhiteSpace(value))
    {
        return "-";
    }

    if (chunkLength < 20 || value.Length <= chunkLength)
    {
        return value;
    }

    var chunks = new List<string>();
    for (var index = 0; index < value.Length; index += chunkLength)
    {
        var size = Math.Min(chunkLength, value.Length - index);
        chunks.Add(value.Substring(index, size));
    }

    return string.Join("\n", chunks);
}

static string FormatMs(double? value)
    => value.HasValue ? value.Value.ToString("0.###", CultureInfo.InvariantCulture) : "-";

static string FormatSignedMs(double? value)
{
    if (!value.HasValue)
    {
        return "-";
    }

    return $"{value.Value.ToString("+0.###;-0.###;0", CultureInfo.InvariantCulture)} ms";
}

static string FormatMsInterval(double? low, double? high)
{
    if (!low.HasValue || !high.HasValue)
    {
        return "-";
    }

    var lowText = low.Value.ToString("+0.###;-0.###;0", CultureInfo.InvariantCulture);
    var highText = high.Value.ToString("+0.###;-0.###;0", CultureInfo.InvariantCulture);
    return $"[{lowText}, {highText}] ms";
}

static string FormatMsRange(double? low, double? high)
{
    if (!low.HasValue || !high.HasValue)
    {
        return "-";
    }

    return $"[{low.Value.ToString("0.###", CultureInfo.InvariantCulture)}, {high.Value.ToString("0.###", CultureInfo.InvariantCulture)}] ms";
}

static string FormatPct(double? value)
    => value.HasValue ? $"{value.Value:0.##}%" : "-";

static string FormatPValue(double? pValue)
{
    if (!pValue.HasValue)
    {
        return "-";
    }

    if (pValue.Value < 0.001)
    {
        return "< 0.001";
    }

    return pValue.Value.ToString("0.###", CultureInfo.InvariantCulture);
}

static string FormatSignedPct(double? value)
{
    if (!value.HasValue)
    {
        return "-";
    }

    return $"{value.Value:+0.##;-0.##;0}%";
}

static string FormatPeriod(DateTimeOffset? startUtc, DateTimeOffset? endUtc)
{
    if (!startUtc.HasValue || !endUtc.HasValue)
    {
        return "-";
    }

    return $"{startUtc.Value:yyyy-MM-dd HH:mm:ss} to {endUtc.Value:yyyy-MM-dd HH:mm:ss}";
}

static string FormatUtcTimestamp(DateTimeOffset? timestampUtc)
    => timestampUtc.HasValue
        ? $"{timestampUtc.Value:yyyy-MM-dd HH:mm:ss}"
        : "-";

static string FormatDuration(TimeSpan? duration)
{
    if (!duration.HasValue)
    {
        return "-";
    }

    var value = duration.Value;
    if (value.TotalHours >= 1)
    {
        return $"{(int)value.TotalHours:00}:{value.Minutes:00}:{value.Seconds:00}";
    }

    return $"{value.Minutes:00}:{value.Seconds:00}";
}

static string FormatMoment(DateTimeOffset? timestampUtc, double? durationMs)
{
    if (!timestampUtc.HasValue || !durationMs.HasValue)
    {
        return "-";
    }

    return $"{timestampUtc.Value:yyyy-MM-dd HH:mm:ss} UTC ({durationMs.Value:0.###} ms)";
}

static string FormatExtreme(LogEntry? entry)
{
    if (entry is null || !entry.TotalMs.HasValue)
    {
        return "-";
    }

    return $"{entry.QueryName} | {entry.TimestampUtc:yyyy-MM-dd HH:mm:ss} UTC | {entry.TotalMs.Value:0.###} ms";
}

static void AddHeaderCell(QuestPDF.Fluent.TableDescriptor table, string text)
{
    table.Cell()
        .BorderBottom(1)
        .BorderColor(QuestPDF.Helpers.Colors.Grey.Darken1)
        .Padding(3)
        .Text(text)
        .Bold();
}

static void AddBodyCell(QuestPDF.Fluent.TableDescriptor table, string text)
{
    table.Cell()
        .BorderBottom(1)
        .BorderColor(QuestPDF.Helpers.Colors.Grey.Lighten2)
        .Padding(3)
        .Text(text);
}

static void AddStatCard(QuestPDF.Infrastructure.IContainer container, string label, string value)
{
    container
        .Border(1)
        .BorderColor(QuestPDF.Helpers.Colors.Grey.Lighten1)
        .Background(QuestPDF.Helpers.Colors.Grey.Lighten4)
        .PaddingHorizontal(8)
        .PaddingVertical(6)
        .Column(col =>
        {
            col.Spacing(2);
            col.Item().Text(label).SemiBold().FontSize(10).FontColor(QuestPDF.Helpers.Colors.Grey.Darken2);
            col.Item().Text(value).Bold().FontSize(15);
        });
}

internal sealed class RawLogRow
{
    [Name("timestamp_utc")]
    public string TimestampUtc { get; set; } = string.Empty;

    [Name("query_name")]
    public string? QueryName { get; set; }

    [Name("event_type")]
    public string? EventType { get; set; }

    [Name("method")]
    public string? Method { get; set; }

    [Name("url")]
    public string? Url { get; set; }

    [Name("status")]
    public string? StatusCode { get; set; }

    [Name("ttfb_ms")]
    public string? TtfbMs { get; set; }

    [Name("total_ms")]
    public string? TotalMs { get; set; }

    [Name("error")]
    public string? Error { get; set; }
}

internal sealed record LogEntry(
    DateTimeOffset TimestampUtc,
    string QueryName,
    string EventType,
    bool IsThrottleEvent,
    bool HasThrottleSignal,
    string Method,
    string Url,
    int StatusCode,
    double? TtfbMs,
    double? TotalMs,
    string Error,
    bool IsSuccess);

internal sealed record MinuteBucket(
    int Minute,
    int Count,
    double? MeanMs,
    double? MedianMs);

internal sealed record QuantileInterval(
    double? LowMs,
    double? HighMs);

internal sealed record HalfHourInference(
    double? MedianDiffMs,
    double? CiLowMs,
    double? CiHighMs,
    double? BiasPct,
    string DirectionLabel,
    int FirstHalfCount,
    int SecondHalfCount,
    bool IsStatisticallyReliable);

internal sealed record ChangePointInference(
    DateTimeOffset? ChangeAtUtc,
    double? PValue,
    double? StatisticK,
    double? FirstSegmentMedianMs,
    double? SecondSegmentMedianMs,
    double? MedianShiftMs,
    int FirstSegmentSamples,
    int SecondSegmentSamples,
    bool IsStatisticallyReliable);

internal sealed record RankedValue(
    int OriginalIndex,
    double Value);

internal sealed record IndexedPValue(
    int Index,
    double? PValue);

internal sealed class AppSettingsAppendix
{
    public string? SourcePath { get; set; }
    public string? ResourceBaseUrl { get; set; }
    public List<AppSettingsQueryInfo> Queries { get; set; } = [];
    public string? LoadError { get; set; }
}

internal sealed class MonitorAppSettings
{
    public string? ResourceBaseUrl { get; set; }
    public List<MonitorConfiguredQuery>? Queries { get; set; }
    public string? QueryName { get; set; }
    public string? QueryMethod { get; set; }
    public string? QueryUrl { get; set; }
}

internal sealed class MonitorConfiguredQuery
{
    public string? Name { get; set; }
    public string? Method { get; set; }
    public string? UrlPath { get; set; }
}

internal sealed record AppSettingsQueryInfo(
    string Name,
    string Method,
    string UrlPath);

internal sealed class QueryReport
{
    public string QueryName { get; set; } = string.Empty;
    public string Method { get; set; } = string.Empty;
    public List<LogEntry> Rows { get; set; } = [];
    public int Samples { get; set; }
    public int SuccessCount { get; set; }
    public int ErrorCount { get; set; }
    public double? SuccessRatePct { get; set; }
    public double? AverageMs { get; set; }
    public double? MedianMs { get; set; }
    public double? P50Ms { get; set; }
    public double? P90Ms { get; set; }
    public double? P95Ms { get; set; }
    public double? P95CiLowMs { get; set; }
    public double? P95CiHighMs { get; set; }
    public double? P99Ms { get; set; }
    public double? P99CiLowMs { get; set; }
    public double? P99CiHighMs { get; set; }
    public double? StdDevMs { get; set; }
    public double? MinMs { get; set; }
    public double? MaxMs { get; set; }
    public DateTimeOffset? FirstSeenAtUtc { get; set; }
    public DateTimeOffset? LastSeenAtUtc { get; set; }
    public DateTimeOffset? FastestAtUtc { get; set; }
    public double? FastestMs { get; set; }
    public DateTimeOffset? SlowestAtUtc { get; set; }
    public double? SlowestMs { get; set; }
    public List<DateTimeOffset> ThrottleEventsUtc { get; set; } = [];
    public int ThrottleEventCount { get; set; }
    public List<MinuteBucket> MinuteBuckets { get; set; } = [];
    public int MinuteCoverage { get; set; }
    public double? MinuteOfHourEtaSquared { get; set; }
    public double? MinuteEffectPValue { get; set; }
    public double? MinuteEffectQValue { get; set; }
    public string PeakMinuteSummary { get; set; } = "-";
    public double? HalfHourMedianDiffMs { get; set; }
    public double? HalfHourMedianDiffCiLowMs { get; set; }
    public double? HalfHourMedianDiffCiHighMs { get; set; }
    public double? HalfHourBiasPct { get; set; }
    public string HalfHourBiasLabel { get; set; } = "-";
    public int FirstHalfSamples { get; set; }
    public int SecondHalfSamples { get; set; }
    public bool HalfHourInferenceReliable { get; set; }
    public DateTimeOffset? ChangePointAtUtc { get; set; }
    public double? ChangePointPValue { get; set; }
    public double? ChangePointQValue { get; set; }
    public double? ChangePointStatisticK { get; set; }
    public double? ChangePointMedianShiftMs { get; set; }
    public double? ChangePointFirstMedianMs { get; set; }
    public double? ChangePointSecondMedianMs { get; set; }
    public int ChangePointFirstSegmentSamples { get; set; }
    public int ChangePointSecondSegmentSamples { get; set; }
    public bool ChangePointReliable { get; set; }
    public List<string> ReaderSummary { get; set; } = [];
    public string TimelineChartPath { get; set; } = string.Empty;
    public string HistogramChartPath { get; set; } = string.Empty;
    public string MinuteProfileChartPath { get; set; } = string.Empty;
}
