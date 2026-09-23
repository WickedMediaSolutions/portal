using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace Portal.UI;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel = new();

    public MainWindow()
    {
        DataContext = _viewModel;
        InitializeComponent();

        // Size the initial window to the usable desktop work area (which
        // excludes the taskbar) so the title bar and window controls are never
        // positioned off-screen on smaller displays. The window uses native
        // chrome, so Minimize / Maximize-Restore / Close and resizing are
        // provided by Windows and already respect the work area when maximized.
        var workArea = SystemParameters.WorkArea;

        // Never allow the minimum dimensions to exceed the work area, which
        // would otherwise force the window (and its title bar) off-screen.
        MinWidth = Math.Min(MinWidth, workArea.Width);
        MinHeight = Math.Min(MinHeight, workArea.Height);

        // Clamp the initial size to fit within the work area.
        Width = Math.Min(Width, workArea.Width);
        Height = Math.Min(Height, workArea.Height);
    }
}