using Avalonia.Controls;
using G915Fix.Desktop.ViewModels;

namespace G915Fix.Desktop.Views;

/// <summary>Reusable settings surface for platform-specific Avalonia windows.</summary>
public partial class DesktopMainView : UserControl
{
    public DesktopMainView()
    {
        InitializeComponent();
    }

    public DesktopMainView(DesktopMainViewModel viewModel)
        : this()
    {
        DataContext = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
    }
}
