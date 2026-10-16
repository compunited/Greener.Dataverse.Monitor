using System.Text;
using Microsoft.Extensions.Logging;

namespace Greener.Dataverse.Monitor.Logging;

public sealed class SimpleFileLoggerProvider : ILoggerProvider
{
    private readonly object _gate = new();
    private readonly StreamWriter _writer;

    public SimpleFileLoggerProvider(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var stream = new FileStream(fullPath, FileMode.Append, FileAccess.Write, FileShare.Read);
        _writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
        {
            AutoFlush = true
        };
    }

    public ILogger CreateLogger(string categoryName) => new SimpleFileLogger(categoryName, WriteLine);

    public void Dispose() => _writer.Dispose();

    private void WriteLine(string line)
    {
        lock (_gate)
        {
            _writer.WriteLine(line);
        }
    }

    private sealed class SimpleFileLogger(string categoryName, Action<string> writeLine) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            var timestamp = DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
            var message = formatter(state, exception);
            if (exception is not null)
            {
                message = $"{message}{Environment.NewLine}{exception}";
            }

            writeLine($"{timestamp} [{logLevel}] {categoryName}: {message}");
        }
    }
}
