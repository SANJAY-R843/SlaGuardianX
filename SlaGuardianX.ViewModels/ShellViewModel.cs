using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using SlaGuardianX.ViewModels.Navigation;

namespace SlaGuardianX.ViewModels;

/// <summary>
/// Shell ViewModel - manages the entire application framework (navigation, sidebar, header, etc)
/// </summary>
public partial class ShellViewModel : ObservableObject
{
    private readonly INavigationService _navigationService;
    private readonly IServiceProvider _serviceProvider;

    [ObservableProperty]
    private object? currentView;

    [ObservableProperty]
    private NavigationItem? currentModule;

    [ObservableProperty]
    private string breadcrumb = "Overview";

    [ObservableProperty]
    private ObservableCollection<NavigationItem> modules = new();

    [ObservableProperty]
    private bool isSidebarExpanded = true;

    [ObservableProperty]
    private string appVersion = "1.0.0";

    public ShellViewModel(INavigationService navigationService, IServiceProvider serviceProvider)
    {
        _navigationService = navigationService;
        _serviceProvider = serviceProvider;
        
        _navigationService.NavigationRequested += OnNavigationRequested;
    }

    [RelayCommand]
    public void NavigateTo(object parameter)
    {
        if (parameter is NavigationItem item)
        {
            _navigationService.NavigateTo(item.Id);
        }
    }

    [RelayCommand]
    public void ToggleSidebar()
    {
        IsSidebarExpanded = !IsSidebarExpanded;
    }

    public void Initialize(IEnumerable<NavigationItem> modules)
    {
        _navigationService.RegisterModules(modules);
        
        foreach (var module in _navigationService.GetAllModules())
        {
            Modules.Add(module);
        }

        // Navigate to first module
        var firstModule = _navigationService.GetAllModules().FirstOrDefault();
        if (firstModule != null)
        {
            _navigationService.NavigateTo(firstModule.Id);
        }
    }

    private void OnNavigationRequested(object? sender, NavigationEventArgs e)
    {
        // Deselect all modules, then select current
        foreach (var m in Modules)
            m.IsSelected = false;
        e.Module.IsSelected = true;

        CurrentModule = e.Module;
        Breadcrumb = e.Module.Title;
        
        // Create view and resolve ViewModel via DI
        try
        {
            var view = Activator.CreateInstance(e.ViewType);
            
            // If the NavigationItem has a ViewModelType, resolve it from DI and set as DataContext
            if (e.Module.ViewModelType != null && view is System.Windows.FrameworkElement fe)
            {
                var viewModel = _serviceProvider.GetService(e.Module.ViewModelType);
                if (viewModel != null)
                {
                    fe.DataContext = viewModel;
                }
            }
            
            CurrentView = view;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Navigation error: {ex.Message}");
        }
    }
}
