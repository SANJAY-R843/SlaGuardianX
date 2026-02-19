using CommunityToolkit.Mvvm.ComponentModel;

namespace SlaGuardianX.ViewModels.Navigation;

/// <summary>
/// Represents a navigation menu item in the sidebar
/// </summary>
public partial class NavigationItem : ObservableObject
{
    public string Id { get; set; }
    public string Title { get; set; }
    public string Icon { get; set; }
    public Type ViewType { get; set; }
    public Type? ViewModelType { get; set; }
    public int Order { get; set; }
    public string Category { get; set; }

    [ObservableProperty]
    private bool isSelected;

    public NavigationItem(string id, string title, string icon, Type viewType, int order, string category = "Core", Type? viewModelType = null)
    {
        Id = id;
        Title = title;
        Icon = icon;
        ViewType = viewType;
        ViewModelType = viewModelType;
        Order = order;
        Category = category;
    }
}
