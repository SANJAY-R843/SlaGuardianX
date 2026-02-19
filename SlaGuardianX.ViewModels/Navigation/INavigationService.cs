namespace SlaGuardianX.ViewModels.Navigation;

/// <summary>
/// Service for managing application-wide navigation between modules
/// </summary>
public interface INavigationService
{
    event EventHandler<NavigationEventArgs>? NavigationRequested;

    void NavigateTo(string moduleId);
    void NavigateTo(Type viewType);
    void RegisterModules(IEnumerable<NavigationItem> modules);
    IEnumerable<NavigationItem> GetAllModules();
    NavigationItem? GetCurrentModule();
}

/// <summary>
/// Event args for navigation requests
/// </summary>
public class NavigationEventArgs : EventArgs
{
    public Type ViewType { get; set; }
    public NavigationItem Module { get; set; }

    public NavigationEventArgs(Type viewType, NavigationItem module)
    {
        ViewType = viewType;
        Module = module;
    }
}

/// <summary>
/// Default implementation of navigation service
/// </summary>
public class NavigationService : INavigationService
{
    private readonly Dictionary<string, NavigationItem> _modules = new();
    private NavigationItem? _currentModule;

    public event EventHandler<NavigationEventArgs>? NavigationRequested;

    public void NavigateTo(string moduleId)
    {
        if (_modules.TryGetValue(moduleId, out var module))
        {
            NavigateTo(module.ViewType);
        }
    }

    public void NavigateTo(Type viewType)
    {
        var module = _modules.Values.FirstOrDefault(m => m.ViewType == viewType);
        if (module != null)
        {
            _currentModule = module;
            NavigationRequested?.Invoke(this, new NavigationEventArgs(viewType, module));
        }
    }

    public void RegisterModules(IEnumerable<NavigationItem> modules)
    {
        _modules.Clear();
        foreach (var module in modules.OrderBy(m => m.Order))
        {
            _modules[module.Id] = module;
        }

        // Set first module as default
        if (_modules.Count > 0 && _currentModule == null)
        {
            _currentModule = _modules.Values.First();
        }
    }

    public IEnumerable<NavigationItem> GetAllModules() => _modules.Values.OrderBy(m => m.Order);

    public NavigationItem? GetCurrentModule() => _currentModule;
}
