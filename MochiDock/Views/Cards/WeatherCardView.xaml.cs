using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MochiDock.ViewModels;
using UserControl = System.Windows.Controls.UserControl;

namespace MochiDock.Views.Cards;

public partial class WeatherCardView : UserControl
{
    private MainViewModel? ViewModel => DataContext as MainViewModel;

    public WeatherCardView()
    {
        InitializeComponent();
    }

    private void HourlyScroll_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is ScrollViewer scroller)
        {
            if (e.Delta < 0)
                scroller.LineRight();
            else
                scroller.LineLeft();
            e.Handled = true;
        }
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is { } vm && sender is System.Windows.Controls.Button button)
        {
            await MochiDock.Controls.RefreshFeedback.RunAsync(button, vm.RefreshWeatherAsync);
        }
    }

    private void ChangeCity_Click(object sender, RoutedEventArgs e) =>
        App.OpenSettings(MochiDock.Core.SettingsSections.Location);
}
