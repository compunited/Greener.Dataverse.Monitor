# Security

Do not post sensitive data, customer URLs, tenant IDs, account identifiers, access tokens, logs, or production traces in GitHub issues.

For suspected security vulnerabilities, use the repository's **Security > Advisories > Report a vulnerability** action when available. If private reporting is unavailable, open an issue asking for a private contact channel without including exploit details, logs, environment names or credentials.

## Token handling

Authentication uses MSAL delegated public-client flows. Persistent disk token caching has been removed; MSAL's process-local cache is used for the duration of a run. Do not add unprotected persistent token caches.

## Privacy

Use only against Dataverse environments for which you have explicit authorization. Review exported telemetry before sharing it.

The tool records full request URLs in CSV files, including query strings. Application logs include query names and paths, HTTP client diagnostics and exception details. URL filters, error messages and local file paths can contain personal or customer information. Treat all output as sensitive until reviewed.

The reporting companion embeds the discovered or explicitly selected configuration in a PDF appendix, including the environment URL, configured queries and source-file path. It also includes measurement source paths. Redact the input/configuration or the finished PDF before sharing it. The tool does not automatically anonymize telemetry.

Use a delegated account with only the access needed for the configured requests. Request methods and batch bodies are user-controlled; review them before use because write-capable requests may change Dataverse data. Use HTTPS environment URLs and protect your local configuration with appropriate filesystem permissions.

Never share a device login code. Anyone with access to your terminal or its recording may see a displayed code during authentication.

## Safe contributions

Use synthetic records and placeholder URLs in examples or bug reports. The repository ignores CSV files, application logs, generated reports, token-cache directories and local configuration overlays. Ignore rules do not remove a file that is already tracked; inspect the staged diff before every contribution.

This repository has a new history containing the reviewed source snapshot. Do not import branches, tags, PR references or Git objects from a repository that contained sensitive data. If credentials were ever disclosed, revoke or rotate them; deleting files or rewriting Git history is insufficient.

## Supported releases

Security fixes are made on the current default branch. Use the latest published release and check dependency advisories when deploying in your environment.
