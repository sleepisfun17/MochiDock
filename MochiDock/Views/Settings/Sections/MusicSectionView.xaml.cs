using System.Windows;
using MochiDock.Core;
using MochiDock.ViewModels;
using UserControl = System.Windows.Controls.UserControl;

namespace MochiDock.Views.Settings.Sections;

/// <summary>Lyrics options. Switches bind to the view model; timing uses its nudge methods.</summary>
public partial class MusicSectionView : UserControl
{
    public MusicSectionView() => InitializeComponent();

    private MainViewModel? ViewModel => DataContext as MainViewModel;

    private void Earlier_Click(object sender, RoutedEventArgs e) => ViewModel?.NudgeLyricsOffset(LyricOffset.StepMs);
    private void Later_Click(object sender, RoutedEventArgs e) => ViewModel?.NudgeLyricsOffset(-LyricOffset.StepMs);
    private void Reset_Click(object sender, RoutedEventArgs e) => ViewModel?.ResetLyricsOffset();
}
