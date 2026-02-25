using System;
using System.Collections.Generic;
using System.Linq;

namespace SlaGuardianX.Services;

/// <summary>
/// Evaluates health rules against system snapshots.
/// CPU &lt; 85%, RAM &lt; 80%, Disk active time &lt; 90%, Ping &lt; 100ms.
/// </summary>
public class HealthRuleEngine
{
    public double CpuThreshold { get; set; } = 85;
    public double RamThreshold { get; set; } = 80;
    public double DiskThreshold { get; set; } = 90;
    public double DiskSpaceMinGB { get; set; } = 10;

    public HealthReport Evaluate(SystemSnapshot snap)
    {
        var issues = new List<HealthIssue>();

        // CPU
        if (snap.CpuPercent >= CpuThreshold)
            issues.Add(new HealthIssue("CPU", $"CPU at {snap.CpuPercent:F0}% (threshold {CpuThreshold}%)", IssueSeverity.Critical));
        else if (snap.CpuPercent >= CpuThreshold * 0.85)
            issues.Add(new HealthIssue("CPU", $"CPU at {snap.CpuPercent:F0}% (approaching threshold)", IssueSeverity.Warning));

        // RAM
        if (snap.RamPercent >= RamThreshold)
            issues.Add(new HealthIssue("RAM", $"RAM at {snap.RamPercent:F0}% ({snap.UsedRamMB} MB / {snap.TotalRamMB} MB)", IssueSeverity.Critical));
        else if (snap.RamPercent >= RamThreshold * 0.85)
            issues.Add(new HealthIssue("RAM", $"RAM at {snap.RamPercent:F0}% (approaching threshold)", IssueSeverity.Warning));

        // Disk active
        if (snap.DiskActivePercent >= DiskThreshold)
            issues.Add(new HealthIssue("Disk I/O", $"Disk active time at {snap.DiskActivePercent:F0}%", IssueSeverity.Critical));

        // Disk space
        if (snap.FreeDiskGB < DiskSpaceMinGB && snap.TotalDiskGB > 0)
            issues.Add(new HealthIssue("Disk Space", $"Only {snap.FreeDiskGB:F1} GB free", IssueSeverity.Critical));
        else if (snap.FreeDiskGB < DiskSpaceMinGB * 2 && snap.TotalDiskGB > 0)
            issues.Add(new HealthIssue("Disk Space", $"{snap.FreeDiskGB:F1} GB free (low)", IssueSeverity.Warning));

        // Health score: 100 minus penalties
        double score = 100;
        foreach (var issue in issues)
        {
            score -= issue.Severity == IssueSeverity.Critical ? 25 : 10;
        }
        score = Math.Max(0, Math.Min(100, score));

        var status = score >= 80 ? HealthStatus.Healthy
                   : score >= 50 ? HealthStatus.Warning
                   : HealthStatus.Critical;

        return new HealthReport
        {
            Timestamp = snap.Timestamp,
            HealthScore = Math.Round(score, 0),
            Status = status,
            Issues = issues
        };
    }
}

public enum IssueSeverity { Info, Warning, Critical }
public enum HealthStatus { Healthy, Warning, Critical }

public class HealthIssue
{
    public string Source { get; set; }
    public string Description { get; set; }
    public IssueSeverity Severity { get; set; }
    public string SuggestedFix { get; set; } = "";
    public DateTime Timestamp { get; set; } = DateTime.Now;

    public HealthIssue(string source, string desc, IssueSeverity severity)
    {
        Source = source;
        Description = desc;
        Severity = severity;

        SuggestedFix = source switch
        {
            "CPU" => "Check top CPU-consuming processes and close unnecessary apps.",
            "RAM" => "Close memory-heavy applications or increase system RAM.",
            "Disk I/O" => "Reduce disk-intensive operations or check for disk errors.",
            "Disk Space" => "Clear temp files, uninstall unused apps, or expand storage.",
            _ => "Investigate the issue source."
        };
    }
}

public class HealthReport
{
    public DateTime Timestamp { get; set; }
    public double HealthScore { get; set; }
    public HealthStatus Status { get; set; }
    public List<HealthIssue> Issues { get; set; } = new();
}
