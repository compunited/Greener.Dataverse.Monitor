namespace Greener.Dataverse.Monitor.Configuration;

public sealed class MonitorOptions
{
    public string ResourceBaseUrl { get; set; } = string.Empty;
    public List<MonitorQueryOptions> Queries { get; set; } = [];

    // Legacy single-query settings for backward compatibility.
    public string UrlPath { get; set; } = "$batch";
    public string Method { get; set; } = "POST";
    public string? ContentType { get; set; } = "multipart/mixed";
    public string? RequestBody { get; set; } = "--batch_123--";
    public string? BatchBoundary { get; set; } = "batch_123";
    public int IntervalMinutes { get; set; } = 5;
    public int StopAfterMinutes { get; set; } = 0;
    public int HttpTimeoutSeconds { get; set; } = 30;
    public int HttpMaxConnectionsPerServer { get; set; } = 8;
    public int MaxAttempts { get; set; } = 4;
    public string CsvPath { get; set; } = "dataverse_monitor.csv";
    public AuthOptions Auth { get; set; } = new();
}
