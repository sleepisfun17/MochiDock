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

    private void CopyItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is ClipboardHistoryItem item)
        {
            ViewModel?.RecallClipboard(item);
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

