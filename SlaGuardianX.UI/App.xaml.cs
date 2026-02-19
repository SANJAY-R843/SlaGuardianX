using System.Configuration;
using System.Data;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using SlaGuardianX.Data;
using SlaGuardianX.Models;
using SlaGuardianX.Services;
using SlaGuardianX.ViewModels;
using SlaGuardianX.ViewModels.Navigation;
using SlaGuardianX.ViewModels.Modules;
using SlaGuardianX.UI.Views;
using SlaGuardianX.UI.Views.Modules;

namespace SlaGuardianX.UI;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    private IServiceProvider _serviceProvider;

    /// <summary>
    /// Global access to the DI service provider
    /// </summary>
    public static IServiceProvider Services { get; private set; } = null!;

    public App()
    {
        var services = new ServiceCollection();
        ConfigureServices(services);
        _serviceProvider = services.BuildServiceProvider();
        Services = _serviceProvider;
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Initialize database
        try
        {
            var context = _serviceProvider.GetRequiredService<AppDbContext>();
            context.Database.EnsureCreated();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Database init error: {ex.Message}");
        }

        // Create and show main window
        var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
        mainWindow.Show();
    }

    private void ConfigureServices(IServiceCollection services)
    {
        // Register DbContext
        services.AddDbContext<AppDbContext>(options =>
            options.UseSqlite("Data Source=sla_guardian.db"), ServiceLifetime.Singleton);

        // Register repositories as singletons (since DbContext is singleton)
        services.AddSingleton(typeof(IRepository<>), typeof(Repository<>));

        // Register core services as singletons (they hold state like timers, enabled flags)
        services.AddSingleton<TrafficSimulatorService>();
        services.AddSingleton<SlaService>();
        services.AddSingleton<OptimizationService>();
        services.AddSingleton<PredictionService>();

        // Register Navigation Service
        services.AddSingleton<INavigationService, NavigationService>();

        // Register CORE MODULE ViewModels (transient so each navigation creates fresh instance)
        services.AddTransient<OverviewViewModel>();
        services.AddTransient<RealTimeMonitoringViewModel>();
        services.AddTransient<SlaManagerViewModel>();
        services.AddTransient<AiPredictionViewModel>();
        services.AddTransient<OptimizationControlViewModel>();
        services.AddTransient<AlertsIncidentViewModel>();
        services.AddTransient<AnalyticsReportsViewModel>();
        services.AddTransient<MultiSiteViewModel>();

        // Register ADVANCED MODULE ViewModels
        services.AddTransient<TopologyViewModel>();
        services.AddTransient<UserRoleViewModel>();
        services.AddTransient<LogsAuditViewModel>();
        services.AddTransient<SettingsViewModel>();

        // Register WOW MODULE ViewModels
        services.AddTransient<RootCauseAnalyzerViewModel>();
        services.AddTransient<TrafficAnalyzerViewModel>();
        services.AddTransient<CapacityPlanningViewModel>();
        services.AddTransient<SmartNotificationsViewModel>();

        // Register Shell ViewModel
        services.AddSingleton<ShellViewModel>();

        // Register DashboardViewModel (legacy)
        services.AddTransient<DashboardViewModel>();

        // Register Main Window
        services.AddSingleton<MainWindow>(provider =>
        {
            var window = new MainWindow();
            var shellViewModel = provider.GetRequiredService<ShellViewModel>();

            // Initialize navigation modules with ViewModelType mappings
            var modules = new List<NavigationItem>
            {
                // CORE MODULES
                new("overview", "Overview", "📊", typeof(OverviewView), 1, "CORE", typeof(OverviewViewModel)),
                new("monitoring", "Real-Time Monitoring", "📡", typeof(RealTimeMonitoringView), 2, "CORE", typeof(RealTimeMonitoringViewModel)),
                new("sla_manager", "SLA Manager", "📜", typeof(SlaManagerView), 3, "CORE", typeof(SlaManagerViewModel)),
                new("ai_prediction", "AI Prediction", "🧠", typeof(AiPredictionView), 4, "CORE", typeof(AiPredictionViewModel)),
                new("optimization", "Optimization Control", "⚡", typeof(OptimizationControlView), 5, "CORE", typeof(OptimizationControlViewModel)),
                new("alerts", "Alerts & Incidents", "🚨", typeof(AlertsIncidentView), 6, "CORE", typeof(AlertsIncidentViewModel)),
                new("analytics", "Analytics & Reports", "📊", typeof(AnalyticsReportsView), 7, "CORE", typeof(AnalyticsReportsViewModel)),
                new("multisite", "Multi-Site View", "🌐", typeof(MultiSiteView), 8, "CORE", typeof(MultiSiteViewModel)),

                // ADVANCED MODULES
                new("topology", "Device & Topology", "🖥", typeof(TopologyView), 9, "ADVANCED", typeof(TopologyViewModel)),
                new("users", "User & Roles", "👥", typeof(UserRoleView), 10, "ADVANCED", typeof(UserRoleViewModel)),
                new("logs", "Logs & Audit", "🧾", typeof(LogsAuditView), 11, "ADVANCED", typeof(LogsAuditViewModel)),
                new("settings", "Settings", "⚙️", typeof(SettingsView), 12, "ADVANCED", typeof(SettingsViewModel)),

                // WOW MODULES
                new("root_cause", "Root Cause Analyzer", "🔍", typeof(RootCauseAnalyzerView), 13, "INNOVATION", typeof(RootCauseAnalyzerViewModel)),
                new("traffic", "Traffic Analyzer", "📦", typeof(TrafficAnalyzerView), 14, "INNOVATION", typeof(TrafficAnalyzerViewModel)),
                new("capacity", "Capacity Planning", "⏱", typeof(CapacityPlanningView), 15, "INNOVATION", typeof(CapacityPlanningViewModel)),
                new("notifications", "Smart Notifications", "🔔", typeof(SmartNotificationsView), 16, "INNOVATION", typeof(SmartNotificationsViewModel)),
            };

            shellViewModel.Initialize(modules);
            window.DataContext = shellViewModel;
            return window;
        });
    }
}

