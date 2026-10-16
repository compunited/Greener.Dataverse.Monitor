namespace Greener.Dataverse.Monitor.Models;

public sealed class CsvRow
{
    public DateTimeOffset TimestampUtc { get; set; }
    public string? QueryName { get; set; }
    public string EventType { get; set; } = "result";
    public string Method { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public int StatusCode { get; set; }
    public double TtfbMs { get; set; }
    public double TotalMs { get; set; }
    public string? HttpVersion { get; set; }
    public string? Server { get; set; }
    public double? RetryAfterSec { get; set; }
    public string? Error { get; set; }
}
