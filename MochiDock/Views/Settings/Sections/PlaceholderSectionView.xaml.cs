using UserControl = System.Windows.Controls.UserControl;
using System.Windows.Controls;

namespace MochiDock.Views.Settings.Sections;

/// <summary>Stand-in for a section whose controls still live in the dock Settings tab.</summary>
public partial class PlaceholderSectionView : UserControl
{
    public PlaceholderSectionView() => InitializeComponent();
}
