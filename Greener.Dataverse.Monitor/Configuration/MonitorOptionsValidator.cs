using Microsoft.Extensions.Options;

namespace Greener.Dataverse.Monitor.Configuration;

public sealed class MonitorOptionsValidator : IValidateOptions<MonitorOptions>
{
    public ValidateOptionsResult Validate(string? name, MonitorOptions options)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(options.ResourceBaseUrl))
        {
            errors.Add("ResourceBaseUrl must be set.");
        }
        else if (!Uri.TryCreate(options.ResourceBaseUrl, UriKind.Absolute, out _))
        {
            errors.Add("ResourceBaseUrl must be a valid absolute URL.");
        }

        ValidateQueryConfiguration(options, errors);

        if (options.IntervalMinutes <= 0)
        {
            errors.Add("IntervalMinutes must be greater than 0.");
        }

        if (options.StopAfterMinutes < 0)
        {
            errors.Add("StopAfterMinutes must be greater than or equal to 0.");
        }

        if (options.HttpTimeoutSeconds <= 0)
        {
            errors.Add("HttpTimeoutSeconds must be greater than 0.");
        }

        if (options.HttpMaxConnectionsPerServer <= 0)
        {
            errors.Add("HttpMaxConnectionsPerServer must be greater than 0.");
        }

        if (options.MaxAttempts <= 0)
        {
            errors.Add("MaxAttempts must be greater than 0.");
        }

        if (string.IsNullOrWhiteSpace(options.CsvPath))
        {
            errors.Add("CsvPath must be set.");
        }

        if (options.Auth is null)
        {
            errors.Add("Auth section must be set.");
            return ValidateOptionsResult.Fail(errors);
        }

        if (string.IsNullOrWhiteSpace(options.Auth.ClientId))
        {
            errors.Add("Auth.ClientId must be set.");
        }

        if (string.IsNullOrWhiteSpace(options.Auth.TenantId))
        {
            errors.Add("Auth.TenantId must be set.");
        }

        if (!options.Auth.Mode.Equals("Interactive", StringComparison.OrdinalIgnoreCase) &&
            !options.Auth.Mode.Equals("DeviceCode", StringComparison.OrdinalIgnoreCase))
        {
            errors.Add("Auth.Mode must be either 'Interactive' or 'DeviceCode'.");
        }

        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }

    private static void ValidateQueryConfiguration(MonitorOptions options, List<string> errors)
    {
        var queries = options.Queries;
        if (queries is { Count: > 0 })
        {
            for (var i = 0; i < queries.Count; i++)
            {
                var query = queries[i];
                if (query is null)
                {
                    errors.Add($"Queries[{i}] must be set.");
                    continue;
                }

                ValidateQuery(query, $"Queries[{i}]", errors);
            }

            return;
        }

        var legacyQuery = new MonitorQueryOptions
        {
            UrlPath = options.UrlPath,
            Method = options.Method,
            ContentType = options.ContentType,
            RequestBody = options.RequestBody,
            BatchBoundary = options.BatchBoundary
        };

        ValidateQuery(legacyQuery, "Query", errors);
    }

    private static void ValidateQuery(MonitorQueryOptions query, string prefix, List<string> errors)
    {
        if (query.Name is not null && string.IsNullOrWhiteSpace(query.Name))
        {
            errors.Add($"{prefix}: Name must not be empty when provided.");
        }

        if (string.IsNullOrWhiteSpace(query.UrlPath))
        {
            errors.Add($"{prefix}: UrlPath must be set.");
        }

        if (string.IsNullOrWhiteSpace(query.Method))
        {
            errors.Add($"{prefix}: Method must be set.");
        }
    }
}
