using System.Windows;
using System.Windows.Input;
using MochiDock.Models;
using MochiDock.ViewModels;
using UserControl = System.Windows.Controls.UserControl;

namespace MochiDock.Views.Cards;

public partial class AppsCardView : UserControl
{
    public AppsCardView()
    {
        InitializeComponent();
    }

    private MainViewModel? ViewModel => DataContext as MainViewModel;

    private void MessageScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is System.Windows.Controls.ScrollViewer scv)
        {
            scv.ScrollToVerticalOffset(scv.VerticalOffset - (e.Delta / 3.0));
            e.Handled = true;
        }
    }

    private void HistoryItem_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement elem && elem.DataContext is AppNotification notif) ViewModel?.SelectAppMessage(notif);
    }

    private void DismissItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement elem && elem.DataContext is AppNotification notif)
        {
            ViewModel?.DismissAppMessage(notif);
            e.Handled = true;
        }
    }

    private void OpenApp_Click(object sender, RoutedEventArgs e) => ViewModel?.OpenCurrentApp();

    private void Dismiss_Click(object sender, RoutedEventArgs e) => ViewModel?.DismissApps();
}
