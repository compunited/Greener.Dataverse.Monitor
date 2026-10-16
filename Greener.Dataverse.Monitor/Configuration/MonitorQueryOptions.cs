namespace Greener.Dataverse.Monitor.Configuration;

public sealed class MonitorQueryOptions
{
    public string? Name { get; set; }
    public string UrlPath { get; set; } = string.Empty;
    public string Method { get; set; } = "GET";
    public string? ContentType { get; set; }
    public string? RequestBody { get; set; }
    public string? BatchBoundary { get; set; }
}
