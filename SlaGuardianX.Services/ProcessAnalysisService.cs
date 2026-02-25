using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace SlaGuardianX.Services;

/// <summary>
/// Analyzes running processes for CPU, RAM, and IO usage.
/// Identifies top resource consumers with real-time CPU % calculation.
/// Supports End Task functionality.
/// </summary>
public class ProcessAnalysisService
{
    // CPU tracking dictionary for real-time % calculation
    private readonly ConcurrentDictionary<int, CpuTracker> _cpuTrackers = new();
    private static readonly int ProcessorCount = Environment.ProcessorCount;

    /// <summary>Get all processes with real-time CPU % calculation.</summary>
    public Task<List<ProcessInfo>> GetAllProcessesAsync(string? filter = null)
    {
        return Task.Run(() =>
        {
            var result = new List<ProcessInfo>();
            var processes = Process.GetProcesses();

            foreach (var process in processes)
            {
                try
                {
                    if (process.ProcessName == "Idle") continue;

                    var info = new ProcessInfo
                    {
                        Pid = process.Id,
                        Name = process.ProcessName,
                        MemoryMB = Math.Round(process.WorkingSet64 / 1048576.0, 1),
                        ThreadCount = process.Threads.Count,
                        CpuPercent = CalculateCpuPercent(process),
                        CpuSeconds = GetCpuSeconds(process),
                    };

                    result.Add(info);
                }
                catch
                {
                    // Skip inaccessible processes
                }
            }

            // Apply filter if provided
            if (!string.IsNullOrWhiteSpace(filter))
            {
                result = result.Where(p => p.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList();
            }

            return result.OrderByDescending(p => p.CpuPercent).ToList();
        });
    }

    /// <summary>Get top N processes by working set (RAM).</summary>
    public Task<List<ProcessInfo>> GetTopMemoryProcessesAsync(int count = 10)
    {
        return Task.Run(async () =>
        {
            var all = await GetAllProcessesAsync();
            return all.OrderByDescending(p => p.MemoryMB).Take(count).ToList();
        });
    }

    /// <summary>Get top N processes by CPU %.</summary>
    public Task<List<ProcessInfo>> GetTopCpuProcessesAsync(int count = 10)
    {
        return Task.Run(async () =>
        {
            var all = await GetAllProcessesAsync();
            return all.OrderByDescending(p => p.CpuPercent).Take(count).ToList();
        });
    }

    /// <summary>Get all processes grouped by category.</summary>
    public Task<ProcessSummary> GetProcessSummaryAsync()
    {
        return Task.Run(() =>
        {
            var procs = Process.GetProcesses();
            var summary = new ProcessSummary
            {
                TotalProcesses = procs.Length,
                TotalMemoryMB = procs.Sum(p => { try { return p.WorkingSet64 / 1048576.0; } catch { return 0; } }),
                TotalThreads = procs.Sum(p => { try { return p.Threads.Count; } catch { return 0; } }),
            };
            return summary;
        });
    }

    /// <summary>End/kill a process by PID.</summary>
    public Task<ProcessOperationResult> EndTaskAsync(int pid)
    {
        return Task.Run(() =>
        {
            try
            {
                var process = Process.GetProcessById(pid);
                var name = process.ProcessName;
                process.Kill();
                process.WaitForExit(5000);

                // Remove from CPU tracker
                _cpuTrackers.TryRemove(pid, out _);

                return new ProcessOperationResult
                {
                    Success = true,
                    Message = $"Process '{name}' (PID: {pid}) terminated successfully."
                };
            }
            catch (ArgumentException)
            {
                return new ProcessOperationResult
                {
                    Success = false,
                    Message = $"Process with PID {pid} not found."
                };
            }
            catch (Exception ex)
            {
                return new ProcessOperationResult
                {
                    Success = false,
                    Message = $"Failed to terminate process: {ex.Message}"
                };
            }
        });
    }

    /// <summary>Calculate real-time CPU % for a process using time delta.</summary>
    private double CalculateCpuPercent(Process process)
    {
        try
        {
            var now = DateTime.UtcNow;
            var totalTime = process.TotalProcessorTime;

            if (!_cpuTrackers.TryGetValue(process.Id, out var tracker))
            {
                // First sample - store and return 0
                _cpuTrackers[process.Id] = new CpuTracker
                {
                    LastCheckTime = now,
                    LastTotalProcessorTime = totalTime
                };
                return 0;
            }

            var cpuUsedMs = (totalTime - tracker.LastTotalProcessorTime).TotalMilliseconds;
            var elapsedMs = (now - tracker.LastCheckTime).TotalMilliseconds;

            // Update tracker
            tracker.LastCheckTime = now;
            tracker.LastTotalProcessorTime = totalTime;

            if (elapsedMs <= 0) return 0;

            var cpuPercent = (cpuUsedMs / (ProcessorCount * elapsedMs)) * 100;
            return Math.Min(100, Math.Round(cpuPercent, 1));
        }
        catch
        {
            return 0;
        }
    }

    private static double GetCpuSeconds(Process process)
    {
        try { return Math.Round(process.TotalProcessorTime.TotalSeconds, 1); }
        catch { return 0; }
    }

    /// <summary>Clean up old trackers for processes that no longer exist.</summary>
    public void CleanupTrackers()
    {
        var runningPids = new HashSet<int>(Process.GetProcesses().Select(p => p.Id));
        foreach (var pid in _cpuTrackers.Keys)
        {
            if (!runningPids.Contains(pid))
                _cpuTrackers.TryRemove(pid, out _);
        }
    }
}

// ---------- CPU Tracker ----------
internal class CpuTracker
{
    public DateTime LastCheckTime { get; set; }
    public TimeSpan LastTotalProcessorTime { get; set; }
}

// ---------- Models ----------
public class ProcessInfo
{
    public int Pid { get; set; }
    public string Name { get; set; } = "";
    public double MemoryMB { get; set; }
    public double CpuPercent { get; set; }
    public double CpuSeconds { get; set; }
    public int ThreadCount { get; set; }
}

public class ProcessSummary
{
    public int TotalProcesses { get; set; }
    public double TotalMemoryMB { get; set; }
    public int TotalThreads { get; set; }
}

public class ProcessOperationResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = "";
}
