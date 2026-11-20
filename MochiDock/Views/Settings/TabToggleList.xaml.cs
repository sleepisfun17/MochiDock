using UserControl = System.Windows.Controls.UserControl;

namespace MochiDock.Views.Settings;

/// <summary>
/// The on/off switches for the header tabs, shared by the Tabs section of the Advanced Settings window and the dock's Settings tab
/// so the two cannot drift apart. Every switch binds two-way to a view-model property that saves and updates the dock.
/// </summary>
public partial class TabToggleList : UserControl
{
    public TabToggleList() => InitializeComponent();
}
