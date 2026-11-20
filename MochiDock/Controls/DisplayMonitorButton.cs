using System.Windows;
using Button = System.Windows.Controls.Button;

namespace MochiDock.Controls;

public static class DisplayMonitorButton
{
    // Shared by the monitor picker and isolated fixtures; resource references survive theme changes.
    public static Button Create(string label, bool selected)
    {
        var button = new Button { Content = label, Tag = selected ? "Selected" : "" };
        button.SetResourceReference(FrameworkElement.StyleProperty, "MonitorSelectionButton");
        return button;
    }
}
