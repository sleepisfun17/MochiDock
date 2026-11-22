using System.Windows;
using MochiDock.Models;
using MochiDock.ViewModels;
using UserControl = System.Windows.Controls.UserControl;

namespace MochiDock.Views.Cards;

public partial class ClipboardCardView : UserControl
{
    public ClipboardCardView()
    {
        InitializeComponent();
    }

    private MainViewModel? ViewModel => DataContext as MainViewModel;

    private async void CopyItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is ClipboardHistoryItem item)
        {
            ViewModel?.RecallClipboard(item);
            if (sender is System.Windows.Controls.Button copyButton)
            {
                copyButton.Content = "Copied";
                await System.Threading.Tasks.Task.Delay(1200);
                if (IsLoaded && Equals(copyButton.Content, "Copied"))
                    copyButton.Content = "Copy";
            }
        }
    }

    private void PinItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is ClipboardHistoryItem item)
        {
            ViewModel?.TogglePinClipboard(item);
        }
    }

    private void ClearUnpinned_Click(object sender, RoutedEventArgs e)
    {
        ViewModel?.ClearUnpinnedClipboard();
    }
}

