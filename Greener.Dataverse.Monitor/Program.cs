using System.Net;
using Greener.Dataverse.Monitor.Configuration;
using Greener.Dataverse.Monitor.Logging;
using Greener.Dataverse.Monitor.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

var builder = Host.CreateApplicationBuilder(args);
builder.Configuration.AddJsonFile("appsettings.json", optional: false, reloadOnChange: false);

var runTimestamp = DateTimeOffset.Now.ToString("yyyyMMdd_HHmmss");
var runLogDirectory = Path.Combine(Directory.GetCurrentDirectory(), "log");
Directory.CreateDirectory(runLogDirectory);

var configuredCsvPath = builder.Configuration["CsvPath"];
var csvFileName = BuildTimestampedFileName(configuredCsvPath, "dataverse_monitor", ".csv", runTimestamp);
var csvPath = Path.Combine(runLogDirectory, csvFileName);
var appLogPath = Path.Combine(runLogDirectory, $"app_{runTimestamp}.log");

builder.Services
    .AddOptions<MonitorOptions>()
    .Bind(builder.Configuration)
    .PostConfigure(options =>
    {
        options.CsvPath = csvPath;
    })
    .ValidateOnStart();
builder.Services.AddSingleton<IValidateOptions<MonitorOptions>, MonitorOptionsValidator>();

builder.Services.AddSingleton<ICsvLogger, CsvLogger>();
builder.Services.AddSingleton<ITokenProvider, MsalTokenProvider>();
builder.Services.AddHostedService<DataverseMonitorWorker>();

builder.Services
    .AddHttpClient("DataverseMonitor")
    .ConfigureHttpClient((sp, client) =>
    {
        var options = sp.GetRequiredService<IOptions<MonitorOptions>>().Value;
        client.Timeout = TimeSpan.FromSeconds(options.HttpTimeoutSeconds);
    })
    .ConfigurePrimaryHttpMessageHandler(sp =>
    {
        var options = sp.GetRequiredService<IOptions<MonitorOptions>>().Value;
        return new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli,
            AllowAutoRedirect = false,
            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
            PooledConnectionLifetime = TimeSpan.FromMinutes(15),
            MaxConnectionsPerServer = options.HttpMaxConnectionsPerServer,
            EnableMultipleHttp2Connections = true
        };
    });

builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(c =>
{
    c.TimestampFormat = "yyyy-MM-dd HH:mm:ss.fff ";
    c.SingleLine = true;
});
builder.Logging.AddProvider(new SimpleFileLoggerProvider(appLogPath));

var host = builder.Build();
var startupLogger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Startup");
startupLogger.LogInformation("Application log file: {AppLogPath}", appLogPath);
startupLogger.LogInformation("Dataverse CSV file: {CsvPath}", host.Services.GetRequiredService<IOptions<MonitorOptions>>().Value.CsvPath);

await host.RunAsync();

static string BuildTimestampedFileName(string? configuredPath, string defaultBaseName, string defaultExtension, string timestamp)
{
    var configuredFileName = string.IsNullOrWhiteSpace(configuredPath) ? null : Path.GetFileName(configuredPath);
    var baseName = string.IsNullOrWhiteSpace(configuredFileName)
        ? defaultBaseName
        : Path.GetFileNameWithoutExtension(configuredFileName);
    var extension = string.IsNullOrWhiteSpace(configuredFileName)
        ? defaultExtension
        : Path.GetExtension(configuredFileName);

    if (string.IsNullOrWhiteSpace(extension))
    {
        extension = defaultExtension;
    }

    return $"{baseName}_{timestamp}{extension}";
}
