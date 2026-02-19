using System.Windows;
using System.Windows.Input;
using SlaGuardianX.ViewModels;

namespace SlaGuardianX.UI;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    private ShellViewModel? _shellViewModel;

    public MainWindow()
    {
        InitializeComponent();
    }

    private void NavigationButton_Click(object sender, RoutedEventArgs e)
    {
        // Navigation is handled by RelayCommand in ViewModel
    }

    private void ToggleSidebar_Click(object sender, RoutedEventArgs e)
    {
        if (_shellViewModel?.ToggleSidebarCommand.CanExecute(null) == true)
        {
            _shellViewModel.ToggleSidebarCommand.Execute(null);
        }
    }

    // Custom window chrome handlers
    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            ToggleMaximize();
        }
        else
        {
            DragMove();
        }
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void Maximize_Click(object sender, RoutedEventArgs e) => ToggleMaximize();
    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void ToggleMaximize()
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    protected override void OnInitialized(EventArgs e)
    {
        base.OnInitialized(e);

        // Get ViewModel from DataContext
        if (DataContext is ShellViewModel shellViewModel)
        {
            _shellViewModel = shellViewModel;
        }
    }
}
