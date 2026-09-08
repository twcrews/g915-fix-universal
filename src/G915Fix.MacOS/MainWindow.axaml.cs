using Avalonia.Controls;
using Avalonia.Interactivity;

namespace G915Fix.MacOS;

public partial class MainWindow : Window
{
    internal bool AllowClose { get; set; }

    public MainWindow()
    {
        InitializeComponent();
        Closing += OnClosing;
    }

    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        // This is a resident menu-bar utility. Closing its settings window must not
        // silently stop a running filter; Quit from the menu bar exits the process.
        if (!AllowClose)
        {
            e.Cancel = true;
            Hide();
        }
    }
}
