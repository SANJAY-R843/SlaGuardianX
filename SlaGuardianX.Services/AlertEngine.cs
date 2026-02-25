using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace SlaGuardianX.Services;

/// <summary>
/// Fires real alerts when thresholds are breached for sustained periods.
/// CPU > threshold for 10s, RAM > threshold, disk almost full, no internet.
/// </summary>
public class AlertEngine
{
    private readonly HealthRuleEngine _ruleEngine;
    private readonly List<AlertRecord> _alerts = new();
    private readonly object _lock = new();

    private int _cpuHighCount;
    private int _ramHighCount;
    private const int CpuSustainedThreshold = 5; // 5 snapshots (~10s at 2s interval)

    public event EventHandler<AlertRecord>? AlertFired;
    public IReadOnlyList<AlertRecord> Alerts { get { lock (_lock) { return _alerts.ToList().AsReadOnly(); } } }

    public AlertEngine(HealthRuleEngine ruleEngine)
    {
        _ruleEngine = ruleEngine;
    }

    /// <summary>Process a new snapshot through the alert engine.</summary>
    public void ProcessSnapshot(SystemSnapshot snap)
    {
        // CPU sustained check
        if (snap.CpuPercent >= _ruleEngine.CpuThreshold)
        {
            _cpuHighCount++;
            if (_cpuHighCount >= CpuSustainedThreshold)
            {
                Fire("CPU", $"CPU above {_ruleEngine.CpuThreshold}% for {_cpuHighCount * 2}s ({snap.CpuPercent:F0}%)",
                    AlertSeverity.Critical, "Close CPU-intensive processes.");
                _cpuHighCount = 0; // reset after fire
            }
        }
        else
        {
            _cpuHighCount = Math.Max(0, _cpuHighCount - 1);
        }

        // RAM
        if (snap.RamPercent >= _ruleEngine.RamThreshold)
        {
            _ramHighCount++;
            if (_ramHighCount >= 3)
            {
                Fire("RAM", $"RAM at {snap.RamPercent:F0}% ({snap.UsedRamMB} MB / {snap.TotalRamMB} MB)",
                    AlertSeverity.Critical, "Close memory-heavy apps or increase RAM.");
                _ramHighCount = 0;
            }
        }
        else
        {
            _ramHighCount = Math.Max(0, _ramHighCount - 1);
        }

        // Disk space
        if (snap.FreeDiskGB < _ruleEngine.DiskSpaceMinGB && snap.TotalDiskGB > 0)
        {
            Fire("Disk", $"Disk almost full: {snap.FreeDiskGB:F1} GB free of {snap.TotalDiskGB:F0} GB",
                AlertSeverity.Critical, "Clean temp files or expand storage.");
        }

        // Network down
        if (snap.ActiveAdapters == 0 || (snap.NetworkDownloadBps == 0 && snap.NetworkUploadBps == 0))
        {
            Fire("Network", "No active network traffic detected.",
                AlertSeverity.Warning, "Check network cable or Wi-Fi connection.");
        }
    }

    private void Fire(string source, string message, AlertSeverity severity, string suggestedFix)
    {
        // De-duplicate within 30s
        lock (_lock)
        {
            var cutoff = DateTime.Now.AddSeconds(-30);
            if (_alerts.Any(a => a.Source == source && a.Severity == severity && a.Timestamp > cutoff))
                return;

            var alert = new AlertRecord
            {
                Source = source,
                Message = message,
                Severity = severity,
                SuggestedFix = suggestedFix,
                Timestamp = DateTime.Now
            };
            _alerts.Insert(0, alert);
            if (_alerts.Count > 200) _alerts.RemoveAt(_alerts.Count - 1);
            AlertFired?.Invoke(this, alert);
        }
    }

    public void ClearAlerts()
    {
        lock (_lock) { _alerts.Clear(); }
    }
}

public enum AlertSeverity { Info, Warning, Critical }

public class AlertRecord
{
    public string Source { get; set; } = "";
    public string Message { get; set; } = "";
    public AlertSeverity Severity { get; set; }
    public string SuggestedFix { get; set; } = "";
    public DateTime Timestamp { get; set; }
    public bool IsAcknowledged { get; set; }
}
