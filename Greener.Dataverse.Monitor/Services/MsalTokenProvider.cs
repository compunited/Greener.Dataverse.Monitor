using Greener.Dataverse.Monitor.Configuration;
using Microsoft.Identity.Client;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Greener.Dataverse.Monitor.Services;

public sealed class MsalTokenProvider : ITokenProvider
{
    private readonly MonitorOptions _options;
    private readonly ILogger<MsalTokenProvider> _logger;
    private readonly IPublicClientApplication _publicClient;
    private readonly string[] _scopes;
    private int _identityLogged;

    public MsalTokenProvider(IOptions<MonitorOptions> options, ILogger<MsalTokenProvider> logger)
    {
        _options = options.Value;
        _logger = logger;
        _scopes = [$"{_options.ResourceBaseUrl.TrimEnd('/')}/user_impersonation"];
        _publicClient = PublicClientApplicationBuilder
            .Create(_options.Auth.ClientId)
            .WithTenantId(_options.Auth.TenantId)
            .WithDefaultRedirectUri()
            .Build();
    }

    public async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        try
        {
            var account = (await _publicClient.GetAccountsAsync().ConfigureAwait(false)).FirstOrDefault();
            var result = await _publicClient
                .AcquireTokenSilent(_scopes, account)
                .ExecuteAsync(cancellationToken)
                .ConfigureAwait(false);

            LogAuthenticationOnce();
            return result.AccessToken;
        }
        catch (MsalUiRequiredException)
        {
            var result = await AcquireWithUiFallbackAsync(cancellationToken).ConfigureAwait(false);
            LogAuthenticationOnce();
            return result.AccessToken;
        }
    }

    private void LogAuthenticationOnce()
    {
        if (Interlocked.Exchange(ref _identityLogged, 1) == 1)
        {
            return;
        }

        _logger.LogInformation("Authentication succeeded.");
    }

    private async Task<AuthenticationResult> AcquireWithUiFallbackAsync(CancellationToken cancellationToken)
    {
        if (_options.Auth.Mode.Equals("DeviceCode", StringComparison.OrdinalIgnoreCase))
        {
            return await _publicClient
                .AcquireTokenWithDeviceCode(
                    _scopes,
                    code =>
                    {
                        // The short-lived sign-in code must not be written to the persistent app log.
                        Console.WriteLine(code.Message);
                        return Task.CompletedTask;
                    })
                .ExecuteAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        return await _publicClient
            .AcquireTokenInteractive(_scopes)
            .WithPrompt(Prompt.SelectAccount)
            .ExecuteAsync(cancellationToken)
            .ConfigureAwait(false);
    }

}
