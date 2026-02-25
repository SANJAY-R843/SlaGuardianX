using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace SlaGuardianX.Services;

/// <summary>
/// Logging service: writes to local file + in-memory ring buffer.
/// </summary>
public class LoggingService
{
    private readonly List<LogEntry> _entries = new();
    private readonly object _lock = new();
    private readonly string _logFilePath;
    private const int MaxEntries = 500;

    public LoggingService()
    {
        var appData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "SlaGuardianX");
        Directory.CreateDirectory(appData);
        _logFilePath = Path.Combine(appData, $"sla_guardian_{DateTime.Now:yyyyMMdd}.log");
    }

    public void Log(LogLevel level, string source, string message)
    {
        var entry = new LogEntry
        {
            Timestamp = DateTime.Now,
            Level = level,
            Source = source,
            Message = message
        };

        lock (_lock)
        {
            _entries.Insert(0, entry);
            if (_entries.Count > MaxEntries) _entries.RemoveAt(_entries.Count - 1);
        }

        // Append to file (fire-and-forget, non-blocking)
        try
        {
            File.AppendAllText(_logFilePath, $"[{entry.Timestamp:yyyy-MM-dd HH:mm:ss}] [{entry.Level}] [{entry.Source}] {entry.Message}{Environment.NewLine}");
        }
        catch { /* non-critical */ }
    }

    public List<LogEntry> GetRecentLogs(int count = 50)
    {
        lock (_lock) { return _entries.Take(Math.Min(count, _entries.Count)).ToList(); }
    }

    public int TotalCount { get { lock (_lock) { return _entries.Count; } } }

    public string LogFilePath => _logFilePath;

    public Task<string> ExportLogsJsonAsync()
    {
        return Task.Run(() =>
        {
            lock (_lock)
            {
                return JsonSerializer.Serialize(_entries, new JsonSerializerOptions { WriteIndented = true });
            }
        });
    }
}

public enum LogLevel { Info, Warning, Error, Action }

public class LogEntry
{
    public DateTime Timestamp { get; set; }
    public LogLevel Level { get; set; }
    public string Source { get; set; } = "";
    public string Message { get; set; } = "";
}
