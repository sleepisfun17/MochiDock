using System.Windows;
using MochiDock.ViewModels;
using UserControl = System.Windows.Controls.UserControl;

namespace MochiDock.Views.Cards;

public partial class PomodoroCardView : UserControl
{
    public PomodoroCardView()
    {
        InitializeComponent();
    }

    private MainViewModel? ViewModel => DataContext as MainViewModel;

    private void TogglePlay_Click(object sender, RoutedEventArgs e)
    {
        ViewModel?.TogglePomodoro();
    }

    private void Reset_Click(object sender, RoutedEventArgs e)
    {
        ViewModel?.ResetPomodoro();
    }

    private void Preset25_Click(object sender, RoutedEventArgs e)
    {
        ViewModel?.StartPomodoro(25);
    }

    private void Preset50_Click(object sender, RoutedEventArgs e)
    {
        ViewModel?.StartPomodoro(50);
    }

    private void Preset5_Click(object sender, RoutedEventArgs e)
    {
        ViewModel?.StartPomodoroBreak(5);
    }
}

