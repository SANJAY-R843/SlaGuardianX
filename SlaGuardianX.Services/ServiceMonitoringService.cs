using System;
using System.Collections.Generic;
using System.Linq;
using System.ServiceProcess;
using System.Threading.Tasks;

namespace SlaGuardianX.Services;

/// <summary>
/// Monitors and controls Windows Services.
/// Start, Stop, Restart services with status tracking.
/// </summary>
public class ServiceMonitoringService
{
    private readonly HashSet<string> _criticalServices = new(StringComparer.OrdinalIgnoreCase)
    {
        "wuauserv",      // Windows Update
        "BITS",          // Background Intelligent Transfer
        "Dnscache",      // DNS Client
        "Dhcp",          // DHCP Client
        "EventLog",      // Windows Event Log
        "Schedule",      // Task Scheduler
        "W32Time",       // Windows Time
        "Winmgmt",       // WMI
        "SecurityHealthService", // Windows Security
    };

    public event EventHandler<ServiceStatusChangedArgs>? ServiceStatusChanged;

    /// <summary>Get all Windows services with their current status.</summary>
    public Task<List<ServiceInfoModel>> GetServicesAsync(string? filter = null)
    {
        return Task.Run(() =>
        {
            try
            {
                var services = ServiceController.GetServices()
                    .Select(s => MapService(s))
                    .Where(s => s != null)
                    .ToList()!;

                if (!string.IsNullOrWhiteSpace(filter))
                {
                    services = services
                        .Where(s => s.DisplayName.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                                    s.ServiceName.Contains(filter, StringComparison.OrdinalIgnoreCase))
                        .ToList();
                }

                return services.OrderBy(s => s.DisplayName).ToList();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ServiceMonitoring] Error: {ex.Message}");
                return new List<ServiceInfoModel>();
            }
        });
    }

    /// <summary>Get only critical/important services.</summary>
    public Task<List<ServiceInfoModel>> GetCriticalServicesAsync()
    {
        return Task.Run(() =>
        {
            try
            {
                return ServiceController.GetServices()
                    .Where(s => _criticalServices.Contains(s.ServiceName))
                    .Select(s => MapService(s))
                    .Where(s => s != null)
                    .OrderBy(s => s!.DisplayName)
                    .ToList()!;
            }
            catch { return new List<ServiceInfoModel>(); }
        });
    }

    /// <summary>Start a Windows service.</summary>
    public async Task<ServiceOperationResult> StartServiceAsync(string serviceName)
    {
        return await Task.Run(() =>
        {
            try
            {
                using var sc = new ServiceController(serviceName);
                if (sc.Status == ServiceControllerStatus.Running)
                    return new ServiceOperationResult { Success = true, Message = "Service is already running." };

                sc.Start();
                sc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(30));

                ServiceStatusChanged?.Invoke(this, new ServiceStatusChangedArgs
                {
                    ServiceName = serviceName,
                    OldStatus = "Stopped",
                    NewStatus = "Running"
                });

                return new ServiceOperationResult { Success = true, Message = $"Service '{serviceName}' started successfully." };
            }
            catch (Exception ex)
            {
                return new ServiceOperationResult { Success = false, Message = $"Failed to start: {ex.Message}" };
            }
        });
    }

    /// <summary>Stop a Windows service.</summary>
    public async Task<ServiceOperationResult> StopServiceAsync(string serviceName)
    {
        return await Task.Run(() =>
        {
            try
            {
                using var sc = new ServiceController(serviceName);
                if (sc.Status == ServiceControllerStatus.Stopped)
                    return new ServiceOperationResult { Success = true, Message = "Service is already stopped." };

                if (!sc.CanStop)
                    return new ServiceOperationResult { Success = false, Message = "Service cannot be stopped." };

                sc.Stop();
                sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(30));

                ServiceStatusChanged?.Invoke(this, new ServiceStatusChangedArgs
                {
                    ServiceName = serviceName,
                    OldStatus = "Running",
                    NewStatus = "Stopped"
                });

                return new ServiceOperationResult { Success = true, Message = $"Service '{serviceName}' stopped successfully." };
            }
            catch (Exception ex)
            {
                return new ServiceOperationResult { Success = false, Message = $"Failed to stop: {ex.Message}" };
            }
        });
    }

    /// <summary>Restart a Windows service (stop then start).</summary>
    public async Task<ServiceOperationResult> RestartServiceAsync(string serviceName)
    {
        return await Task.Run(() =>
        {
            try
            {
                using var sc = new ServiceController(serviceName);

                if (sc.Status == ServiceControllerStatus.Running)
                {
                    if (!sc.CanStop)
                        return new ServiceOperationResult { Success = false, Message = "Service cannot be stopped." };

                    sc.Stop();
                    sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(30));
                }

                sc.Start();
                sc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(30));

                ServiceStatusChanged?.Invoke(this, new ServiceStatusChangedArgs
                {
                    ServiceName = serviceName,
                    OldStatus = "Stopped",
                    NewStatus = "Running"
                });

                return new ServiceOperationResult { Success = true, Message = $"Service '{serviceName}' restarted successfully." };
            }
            catch (Exception ex)
            {
                return new ServiceOperationResult { Success = false, Message = $"Failed to restart: {ex.Message}" };
            }
        });
    }

    /// <summary>Check if any critical services are down.</summary>
    public async Task<List<ServiceInfoModel>> GetDownCriticalServicesAsync()
    {
        var criticals = await GetCriticalServicesAsync();
        return criticals.Where(s => s.Status != "Running").ToList();
    }

    private ServiceInfoModel? MapService(ServiceController sc)
    {
        try
        {
            return new ServiceInfoModel
            {
                ServiceName = sc.ServiceName,
                DisplayName = sc.DisplayName,
                Status = sc.Status.ToString(),
                CanStop = sc.CanStop,
                CanPauseAndContinue = sc.CanPauseAndContinue,
                IsCritical = _criticalServices.Contains(sc.ServiceName)
            };
        }
        catch { return null; }
    }
}

// ---------- Models ----------

public class ServiceInfoModel
{
    public string ServiceName { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Status { get; set; } = "Unknown";
    public bool CanStop { get; set; }
    public bool CanPauseAndContinue { get; set; }
    public bool IsCritical { get; set; }

    public bool IsRunning => Status == "Running";
    public bool IsStopped => Status == "Stopped";
}

public class ServiceOperationResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = "";
}

public class ServiceStatusChangedArgs : EventArgs
{
    public string ServiceName { get; set; } = "";
    public string OldStatus { get; set; } = "";
    public string NewStatus { get; set; } = "";
}
