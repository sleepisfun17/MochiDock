using UserControl = System.Windows.Controls.UserControl;

namespace MochiDock.Views.Settings.Sections;

/// <summary>Which tabs the dock shows. Every switch binds two-way to the view model, which saves and updates the dock.</summary>
public partial class TabsSectionView : UserControl
{
    public TabsSectionView() => InitializeComponent();
}
