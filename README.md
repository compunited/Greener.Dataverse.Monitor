# Greener Dataverse Monitor

A .NET 10 diagnostic tool for repeated measurements of Microsoft Dataverse Web API response performance, with a PDF reporting companion.

## What it measures

- Configurable GET and batch POST requests at regular intervals
- HTTP status, time to response headers (TTFB), total elapsed request time and HTTP version
- HTTP 429/503 throttling, `Retry-After`, exponential backoff with jitter
- Timestamped CSV measurements and application logs
- Report generation (PDF and charts) with the included `Greener.Dataverse.Report` utility

TTFB measures time until response headers arrive at the **client**. It does **not** measure Dataverse server-side execution time independently. Total elapsed time can include retry delays.

## Quick start

### Prebuilt binaries

Open the repository's [Releases](../../releases) page and download the appropriate ZIP:

| Application | Windows (x64) | Linux (x64) |
| --- | --- | --- |
| Monitor | `Greener.Dataverse.Monitor-vX.Y.Z-win-x64.zip` | `Greener.Dataverse.Monitor-vX.Y.Z-linux-x64.zip` |
| Report | `Greener.Dataverse.Report-vX.Y.Z-win-x64.zip` | `Greener.Dataverse.Report-vX.Y.Z-linux-x64.zip` |

Releases are self-contained and do not require a separately installed .NET runtime. Extract the archive before running. Linux executables may require `chmod +x`. Each release includes SHA-256 checksums.

The release page lists completed builds. A version tag produces downloadable files only after the release workflow succeeds.

### Build from source

Requires the .NET 10 SDK.

```shell
dotnet build Greener.Dataverse.Monitor.slnx --configuration Release
cd Greener.Dataverse.Monitor
dotnet run
```

### Configuration

Edit `Greener.Dataverse.Monitor/appsettings.json` (or the packaged `appsettings.json`) for your own environment. The repository contains only placeholder values:

- `ResourceBaseUrl`: your authorized Dataverse environment URL
- `Auth.TenantId` and `Auth.ClientId`: Microsoft Entra ID tenant and public-client app registration
- `Auth.Mode`: `Interactive` or `DeviceCode`
- `Queries`: request names, HTTP methods and paths relative to `api/data/v9.0/`

Example query: `accounts?$select=accountid&$top=1`. Your app registration and account must be authorized to access the target environment. Output files are created in the `log` folder.

Run the monitor from the directory containing its `appsettings.json`. For a public-client app registration, enable the selected delegated authentication flow and configure the appropriate redirect URI. This tool does not use a client secret.

### Reporting

`Greener.Dataverse.Report` reads monitor CSV output, generates a PDF report and saves charts in a sibling directory. For example:

```shell
dotnet run --project Greener.Dataverse.Report -- --input path/to/dataverse_monitor.csv
```

Run this command from the repository root. Use `--output path/to/report.pdf` to choose the PDF destination and `--appsettings path/to/appsettings.json` to select the configuration used for the report appendix. The report can otherwise discover a nearby configuration file automatically. Generated PDFs include the environment URL, query paths and local source paths; review them before sharing.

## Packaging and releases

The [release workflow](.github/workflows/release.yml) runs on tags matching `v*` and supports manual execution using an existing version tag. It packages both applications as independent Windows x64 and Linux x64 ZIP archives, computes SHA-256 checksums, and attaches all files to the GitHub Release.

To publish a release after review:

```shell
git tag v1.0.0
git push origin v1.0.0
```

Use a fresh unique tag for each release. The build workflow checks normal pushes and pull requests.

## Security and privacy

Use only for environments you are authorized to monitor. Do not commit real telemetry, customer URLs, account or tenant identifiers, tokens or connection details. The MSAL token cache is in-memory only; there is no persistent token cache. CSV output contains full request URLs, and application logs include configured query names and paths. Query filters and error messages can reveal customer data. Review [SECURITY.md](SECURITY.md) before contributing data.

This public repository starts from a clean source snapshot with a new Git history. Configuration examples use placeholders; measurement logs and legacy history are excluded.

## License

Project source code is licensed under MIT — see [LICENSE](LICENSE). Dependencies retain their own licenses; see [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

The PDF companion uses QuestPDF 2024.7.1 with its Community license setting. The [license shipped with that version](https://github.com/QuestPDF/QuestPDF/blob/2024.7.1/LICENSE.md) includes an open-source-project eligibility category. Review the applicable QuestPDF terms before using modified code in a different licensing or commercial context; this project's MIT license does not replace dependency terms.
