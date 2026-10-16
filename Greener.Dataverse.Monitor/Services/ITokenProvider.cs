namespace Greener.Dataverse.Monitor.Services;

public interface ITokenProvider
{
    Task<string> GetAccessTokenAsync(CancellationToken cancellationToken);
}
