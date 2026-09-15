using Avalonia.Controls;
using Avalonia.Interactivity;
using G915Fix.Desktop.ViewModels;

namespace G915Fix.MacOS;

public partial class PermissionsWindow : Window
{
    internal bool AllowClose { get; set; }

    public PermissionsWindow()
    {
        InitializeComponent();
        Closing += OnClosing;
        Activated += OnActivated;
    }

    public PermissionsWindow(DesktopPermissionsViewModel viewModel)
        : this()
    {
        DataContext = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
    }

    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        // Keep the instance available from the menu-bar item after its close button
        // is used, just like the main settings window.
        if (!AllowClose)
        {
            e.Cancel = true;
            Hide();
        }
    }

    private async void OnActivated(object? sender, EventArgs e)
    {
        if (DataContext is DesktopPermissionsViewModel viewModel)
        {
            await viewModel.RefreshAsync();
        }
    }
}
