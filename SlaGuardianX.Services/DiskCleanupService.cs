using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace SlaGuardianX.Services;

/// <summary>
/// Safe system cleanup: temp files, DNS flush, largest folder scan.
/// NEVER touches critical processes or system files.
/// </summary>
public class DiskCleanupService
{
    private static readonly string[] SafeTempPaths = new[]
    {
        Path.GetTempPath(),                                    // %TEMP%
        @"C:\Windows\Temp",                                    // System temp
    };

    /// <summary>Calculate total reclaimable temp file size.</summary>
    public Task<CleanupReport> AnalyzeTempFilesAsync()
    {
        return Task.Run(() =>
        {
            var report = new CleanupReport();
            foreach (var dir in SafeTempPaths)
            {
                try
                {
                    if (!Directory.Exists(dir)) continue;
                    var files = Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories);
                    foreach (var f in files)
                    {
                        try
                        {
                            var fi = new FileInfo(f);
                            report.TotalTempFiles++;
                            report.TotalTempSizeBytes += fi.Length;
                        }
                        catch { }
                    }
                }
                catch { }
            }
            report.TotalTempSizeMB = Math.Round(report.TotalTempSizeBytes / 1048576.0, 1);
            return report;
        });
    }

    /// <summary>Delete temp files (safe). Returns bytes freed.</summary>
    public Task<long> CleanTempFilesAsync()
    {
        return Task.Run(() =>
        {
            long freed = 0;
            foreach (var dir in SafeTempPaths)
            {
                try
                {
                    if (!Directory.Exists(dir)) continue;
                    foreach (var f in Directory.EnumerateFiles(dir, "*", SearchOption.TopDirectoryOnly))
                    {
                        try
                        {
                            var fi = new FileInfo(f);
                            long size = fi.Length;
                            fi.Delete();
                            freed += size;
                        }
                        catch { /* locked file, skip */ }
                    }
                    // Try subdirectories
                    foreach (var d in Directory.EnumerateDirectories(dir))
                    {
                        try { Directory.Delete(d, true); } catch { }
                    }
                }
                catch { }
            }
            return freed;
        });
    }

    /// <summary>Flush DNS cache.</summary>
    public async Task<bool> FlushDnsAsync()
    {
        try
        {
            var psi = new ProcessStartInfo("ipconfig", "/flushdns")
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true
            };
            var proc = Process.Start(psi);
            if (proc != null)
            {
                await proc.WaitForExitAsync();
                return proc.ExitCode == 0;
            }
        }
        catch { }
        return false;
    }

    /// <summary>Get largest folders on the system drive.</summary>
    public Task<List<FolderSizeInfo>> GetLargestFoldersAsync(string rootPath = @"C:\", int topN = 10)
    {
        return Task.Run(() =>
        {
            var folders = new List<FolderSizeInfo>();
            try
            {
                foreach (var dir in Directory.EnumerateDirectories(rootPath))
                {
                    try
                    {
                        long size = GetDirectorySize(dir, maxDepth: 1);
                        folders.Add(new FolderSizeInfo
                        {
                            Path = dir,
                            SizeBytes = size,
                            SizeMB = Math.Round(size / 1048576.0, 1),
                            SizeGB = Math.Round(size / 1_073_741_824.0, 2)
                        });
                    }
                    catch { }
                }
            }
            catch { }
            return folders.OrderByDescending(f => f.SizeBytes).Take(topN).ToList();
        });
    }

    private static long GetDirectorySize(string path, int maxDepth)
    {
        long size = 0;
        try
        {
            foreach (var f in Directory.EnumerateFiles(path))
            {
                try { size += new FileInfo(f).Length; } catch { }
            }
            if (maxDepth > 0)
            {
                foreach (var d in Directory.EnumerateDirectories(path))
                {
                    try { size += GetDirectorySize(d, maxDepth - 1); } catch { }
                }
            }
        }
        catch { }
        return size;
    }
}

public class CleanupReport
{
    public int TotalTempFiles { get; set; }
    public long TotalTempSizeBytes { get; set; }
    public double TotalTempSizeMB { get; set; }
}

public class FolderSizeInfo
{
    public string Path { get; set; } = "";
    public long SizeBytes { get; set; }
    public double SizeMB { get; set; }
    public double SizeGB { get; set; }
}
