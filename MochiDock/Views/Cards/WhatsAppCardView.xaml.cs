using System.Windows;
using MochiDock.ViewModels;
using Button = System.Windows.Controls.Button;
using Key = System.Windows.Input.Key;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using UserControl = System.Windows.Controls.UserControl;

namespace MochiDock.Views.Cards;

public partial class WhatsAppCardView : UserControl
{
    public WhatsAppCardView()
    {
        InitializeComponent();
    }

    private MainViewModel? ViewModel => DataContext as MainViewModel;

    private async void Reaction_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && ViewModel != null)
        {
            string reaction = btn.Tag as string ?? btn.Content as string ?? "";
            if (!string.IsNullOrEmpty(reaction))
            {
                await ViewModel.SendQuickReactionAsync(reaction);
            }
        }
    }

    private async void SendWhatsApp_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel != null) await ViewModel.SendWhatsAppReplyAsync();
    }

    private void DismissWhatsApp_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel != null)
        {
            ViewModel.DismissWhatsAppMessage(ViewModel.CurrentWhatsApp);
        }
    }

    private void MuteChat_Click(object sender, RoutedEventArgs e)
    {
        ViewModel?.MuteCurrentWhatsAppChat();
    }

    private void AllowGroup_Click(object sender, RoutedEventArgs e) => ViewModel?.AllowCurrentWhatsAppGroup();

    private void ClearAllWhatsApp_Click(object sender, RoutedEventArgs e)
    {
        ViewModel?.ClearAllWhatsAppMessages();
    }

    private void HistoryItem_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement elem && elem.DataContext is MochiDock.Models.WhatsAppNotification notif)
        {
            ViewModel?.SelectWhatsAppMessage(notif);
        }
    }

    private void OpenWhatsAppApp_Click(object sender, RoutedEventArgs e)
    {
        ViewModel?.LaunchWhatsAppApp();
    }

    private void OpenWhatsAppQR_Click(object sender, RoutedEventArgs e)
    {
        ViewModel?.OpenWhatsAppDashboard();
    }

    private async void ReplyTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && ViewModel != null)
        {
            await ViewModel.SendWhatsAppReplyAsync();
        }
    }

    private void MessageScrollViewer_PreviewMouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
    {
        if (sender is System.Windows.Controls.ScrollViewer scroller)
        {
            scroller.ScrollToVerticalOffset(scroller.VerticalOffset - (e.Delta / 2.0));
            e.Handled = true;
        }
    }
}
