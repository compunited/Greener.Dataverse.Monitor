namespace Greener.Dataverse.Monitor.Configuration;

public sealed class AuthOptions
{
    public string Mode { get; set; } = "Interactive";
    public string ClientId { get; set; } = string.Empty;
    public string TenantId { get; set; } = string.Empty;
    public string CacheName { get; set; } = "Dataverse.Monitor";
    public string CacheDir { get; set; } = "msal_cache";
    public string CacheFileName { get; set; } = "msal_cache.bin";
}
