using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using System.Net.NetworkInformation;
using System.Threading.Tasks;

namespace SlaGuardianX.Services;

/// <summary>
/// Collects real hardware info via WMI: CPU, GPU, RAM, Disk type, etc.
/// </summary>
public class HardwareInfoService
{
    public Task<HardwareReport> GetHardwareInfoAsync()
    {
        return Task.Run(() =>
        {
            var info = new HardwareReport();

            // CPU
            try
            {
                using var s = new ManagementObjectSearcher("SELECT Name, NumberOfCores, NumberOfLogicalProcessors, MaxClockSpeed FROM Win32_Processor");
                foreach (var obj in s.Get())
                {
                    info.CpuName = obj["Name"]?.ToString()?.Trim() ?? "Unknown";
                    info.CpuCores = Convert.ToInt32(obj["NumberOfCores"]);
                    info.CpuLogicalProcessors = Convert.ToInt32(obj["NumberOfLogicalProcessors"]);
                    info.CpuMaxClockMHz = Convert.ToInt32(obj["MaxClockSpeed"]);
                    break;
                }
            }
            catch { info.CpuName = "Unavailable"; }

            // GPU
            try
            {
                using var s = new ManagementObjectSearcher("SELECT Name, AdapterRAM FROM Win32_VideoController");
                foreach (var obj in s.Get())
                {
                    info.GpuName = obj["Name"]?.ToString()?.Trim() ?? "Unknown";
                    var ram = obj["AdapterRAM"];
                    if (ram != null) info.GpuMemoryMB = Convert.ToInt64(ram) / 1048576;
                    break;
                }
            }
            catch { info.GpuName = "Unavailable"; }

            // RAM
            try
            {
                using var s = new ManagementObjectSearcher("SELECT Capacity, Speed, Manufacturer FROM Win32_PhysicalMemory");
                long totalCap = 0;
                foreach (var obj in s.Get())
                {
                    totalCap += Convert.ToInt64(obj["Capacity"]);
                    if (string.IsNullOrEmpty(info.RamType))
                    {
                        info.RamSpeed = Convert.ToInt32(obj["Speed"]);
                        info.RamManufacturer = obj["Manufacturer"]?.ToString()?.Trim() ?? "";
                    }
                }
                info.RamTotalMB = totalCap / 1048576;
            }
            catch { }

            // Disk
            try
            {
                using var s = new ManagementObjectSearcher("SELECT Model, Size, MediaType FROM Win32_DiskDrive");
                foreach (var obj in s.Get())
                {
                    var disk = new DiskHwInfo
                    {
                        Model = obj["Model"]?.ToString()?.Trim() ?? "",
                        SizeGB = Math.Round(Convert.ToDouble(obj["Size"]) / 1_073_741_824.0, 1),
                    };
                    var mediaType = obj["MediaType"]?.ToString() ?? "";
                    disk.Type = mediaType.Contains("SSD", StringComparison.OrdinalIgnoreCase) ? "SSD"
                              : mediaType.Contains("Fixed", StringComparison.OrdinalIgnoreCase) ? "HDD"
                              : disk.Model.Contains("SSD", StringComparison.OrdinalIgnoreCase) || disk.Model.Contains("NVMe", StringComparison.OrdinalIgnoreCase) ? "SSD"
                              : "HDD";
                    info.Disks.Add(disk);
                }
            }
            catch { }

            // OS
            try
            {
                using var s = new ManagementObjectSearcher("SELECT Caption, Version, BuildNumber FROM Win32_OperatingSystem");
                foreach (var obj in s.Get())
                {
                    info.OsName = obj["Caption"]?.ToString()?.Trim() ?? "";
                    info.OsVersion = obj["Version"]?.ToString() ?? "";
                    info.OsBuild = obj["BuildNumber"]?.ToString() ?? "";
                    break;
                }
            }
            catch { }

            // Motherboard
            try
            {
                using var s = new ManagementObjectSearcher("SELECT Manufacturer, Product FROM Win32_BaseBoard");
                foreach (var obj in s.Get())
                {
                    info.MotherboardManufacturer = obj["Manufacturer"]?.ToString()?.Trim() ?? "";
                    info.MotherboardModel = obj["Product"]?.ToString()?.Trim() ?? "";
                    break;
                }
            }
            catch { }

            return info;
        });
    }
}

public class HardwareReport
{
    public string CpuName { get; set; } = "";
    public int CpuCores { get; set; }
    public int CpuLogicalProcessors { get; set; }
    public int CpuMaxClockMHz { get; set; }
    public string GpuName { get; set; } = "";
    public long GpuMemoryMB { get; set; }
    public long RamTotalMB { get; set; }
    public int RamSpeed { get; set; }
    public string RamManufacturer { get; set; } = "";
    public string RamType { get; set; } = "";
    public List<DiskHwInfo> Disks { get; set; } = new();
    public string OsName { get; set; } = "";
    public string OsVersion { get; set; } = "";
    public string OsBuild { get; set; } = "";
    public string MotherboardManufacturer { get; set; } = "";
    public string MotherboardModel { get; set; } = "";
}

public class DiskHwInfo
{
    public string Model { get; set; } = "";
    public double SizeGB { get; set; }
    public string Type { get; set; } = "";
}
