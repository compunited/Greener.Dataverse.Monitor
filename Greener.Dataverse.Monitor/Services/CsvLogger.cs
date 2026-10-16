using System.Globalization;
using System.Text;
using Greener.Dataverse.Monitor.Configuration;
using Greener.Dataverse.Monitor.Models;
using Microsoft.Extensions.Options;

namespace Greener.Dataverse.Monitor.Services;

public sealed class CsvLogger : ICsvLogger
{
    private const string HeaderV1 = "timestamp_utc,method,url,status,ttfb_ms,total_ms,http_version,server,retry_after_s,error";
    private const string HeaderV2 = "timestamp_utc,query_name,method,url,status,ttfb_ms,total_ms,http_version,server,retry_after_s,error";
    private const string HeaderV3 = "timestamp_utc,query_name,event_type,method,url,status,ttfb_ms,total_ms,http_version,server,retry_after_s,error";
    private readonly object _gate = new();
    private readonly string _path;
    private bool _useQueryNameColumn = true;
    private bool _useEventTypeColumn = true;

    public CsvLogger(IOptions<MonitorOptions> options)
    {
        _path = options.Value.CsvPath;
    }

    public void EnsureHeader()
    {
        lock (_gate)
        {
            if (File.Exists(_path))
            {
                var fileInfo = new FileInfo(_path);
                if (fileInfo.Length == 0)
                {
                    File.WriteAllText(_path, HeaderV3 + Environment.NewLine, Encoding.UTF8);
                    _useQueryNameColumn = true;
                    _useEventTypeColumn = true;
                    return;
                }

                var firstLine = ReadFirstLine(_path);
                _useQueryNameColumn = HasQueryNameColumn(firstLine);
                _useEventTypeColumn = HasEventTypeColumn(firstLine);
                return;
            }

            var fullPath = Path.GetFullPath(_path);
            var directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(_path, HeaderV3 + Environment.NewLine, Encoding.UTF8);
            _useQueryNameColumn = true;
            _useEventTypeColumn = true;
        }
    }

    public void Append(CsvRow row)
    {
        var columns = new List<string>
        {
            row.TimestampUtc.ToString("o")
        };
        if (_useQueryNameColumn)
        {
            columns.Add(Csv(row.QueryName ?? string.Empty));
        }
        if (_useEventTypeColumn)
        {
            columns.Add(Csv(row.EventType));
        }

        columns.AddRange(
        [
            Csv(row.Method),
            Csv(row.Url),
            row.StatusCode.ToString(CultureInfo.InvariantCulture),
            Numeric(row.TtfbMs),
            Numeric(row.TotalMs),
            Csv(row.HttpVersion ?? string.Empty),
            Csv(row.Server ?? string.Empty),
            row.RetryAfterSec?.ToString("0.###", CultureInfo.InvariantCulture) ?? string.Empty,
            Csv(row.Error ?? string.Empty)
        ]);

        var line = string.Join(",", columns);

        lock (_gate)
        {
            File.AppendAllText(_path, line + Environment.NewLine, Encoding.UTF8);
        }
    }

    private static string? ReadFirstLine(string path)
    {
        using var reader = File.OpenText(path);
        return reader.ReadLine();
    }

    private static bool HasQueryNameColumn(string? headerLine)
    {
        if (string.IsNullOrWhiteSpace(headerLine))
        {
            return false;
        }

        if (string.Equals(headerLine, HeaderV2, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (string.Equals(headerLine, HeaderV1, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return headerLine.Contains("query_name", StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasEventTypeColumn(string? headerLine)
    {
        if (string.IsNullOrWhiteSpace(headerLine))
        {
            return false;
        }

        if (string.Equals(headerLine, HeaderV3, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return headerLine.Contains("event_type", StringComparison.OrdinalIgnoreCase);
    }

    private static string Numeric(double value)
        => double.IsNaN(value) ? string.Empty : value.ToString("0.###", CultureInfo.InvariantCulture);

    private static string Csv(string value)
        => value.Contains(',') || value.Contains('"') || value.Contains('\n')
            ? $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\""
            : value;
}
