using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace SlaGuardianX.Services;

/// <summary>
/// Real network diagnostics: connectivity, ping, DNS, public IP, adapters.
/// </summary>
public class NetworkDiagnosticsService
{
    /// <summary>Full network diagnostic sweep.</summary>
    public async Task<NetworkDiagReport> RunDiagnosticsAsync()
    {
        var report = new NetworkDiagReport { Timestamp = DateTime.Now };

        // Internet connectivity
        report.IsInternetAvailable = NetworkInterface.GetIsNetworkAvailable();

        // Gateway ping
        try
        {
            var gateway = GetDefaultGateway();
            if (gateway != null)
            {
                report.GatewayIp = gateway.ToString();
                using var ping = new Ping();
                var reply = await ping.SendPingAsync(gateway, 2000);
                report.GatewayPingMs = reply.Status == IPStatus.Success ? reply.RoundtripTime : -1;
                report.GatewayReachable = reply.Status == IPStatus.Success;
            }
        }
        catch { report.GatewayReachable = false; }

        // DNS resolution time
        try
        {
            var sw = Stopwatch.StartNew();
            await Dns.GetHostAddressesAsync("google.com");
            sw.Stop();
            report.DnsResolutionMs = sw.ElapsedMilliseconds;
            report.DnsWorking = true;
        }
        catch { report.DnsWorking = false; report.DnsResolutionMs = -1; }

        // External ping (8.8.8.8)
        try
        {
            using var ping = new Ping();
            var reply = await ping.SendPingAsync("8.8.8.8", 3000);
            report.ExternalPingMs = reply.Status == IPStatus.Success ? reply.RoundtripTime : -1;
            report.ExternalReachable = reply.Status == IPStatus.Success;
        }
        catch { report.ExternalReachable = false; }

        // Public IP (lightweight)
        try
        {
            using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(3) };
            report.PublicIp = (await http.GetStringAsync("https://api.ipify.org")).Trim();
        }
        catch { report.PublicIp = "N/A"; }

        // Adapter list
        try
        {
            report.Adapters = NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                .Select(n => new AdapterInfo
                {
                    Name = n.Name,
                    Description = n.Description,
                    Status = n.OperationalStatus.ToString(),
                    SpeedMbps = n.Speed / 1_000_000,
                    MacAddress = n.GetPhysicalAddress().ToString(),
                    IpAddress = n.GetIPProperties().UnicastAddresses
                        .FirstOrDefault(a => a.Address.AddressFamily == AddressFamily.InterNetwork)?.Address.ToString() ?? "-"
                }).ToList();
        }
        catch { }

        return report;
    }

    private static IPAddress? GetDefaultGateway()
    {
        try
        {
            return NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up)
                .SelectMany(n => n.GetIPProperties().GatewayAddresses)
                .Select(g => g.Address)
                .FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork);
        }
        catch { return null; }
    }
}

public class NetworkDiagReport
{
    public DateTime Timestamp { get; set; }
    public bool IsInternetAvailable { get; set; }
    public string GatewayIp { get; set; } = "-";
    public long GatewayPingMs { get; set; }
    public bool GatewayReachable { get; set; }
    public long DnsResolutionMs { get; set; }
    public bool DnsWorking { get; set; }
    public long ExternalPingMs { get; set; }
    public bool ExternalReachable { get; set; }
    public string PublicIp { get; set; } = "-";
    public List<AdapterInfo> Adapters { get; set; } = new();
}

public class AdapterInfo
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Status { get; set; } = "";
    public long SpeedMbps { get; set; }
    public string MacAddress { get; set; } = "";
    public string IpAddress { get; set; } = "-";
}
