using System.Windows;
using System.Windows.Input;
using MochiDock.Models;
using MochiDock.ViewModels;
using UserControl = System.Windows.Controls.UserControl;

namespace MochiDock.Views.Cards;

public partial class EmailCardView : UserControl
{
    private readonly System.Windows.Threading.DispatcherTimer _copyFeedbackTimer = new() { Interval = TimeSpan.FromSeconds(2) };

    public EmailCardView()
    {
        InitializeComponent();
        _copyFeedbackTimer.Tick += (_, _) => { _copyFeedbackTimer.Stop(); SetCopyFeedback(false); };
        Unloaded += (_, _) => { _copyFeedbackTimer.Stop(); SetCopyFeedback(false); };
    }

    private MainViewModel? ViewModel => DataContext as MainViewModel;

    private void HistoryItem_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement elem && elem.DataContext is EmailNotification notif)
        {
            ViewModel?.SelectEmailMessage(notif);
        }
    }

    private void DismissItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement elem && elem.DataContext is EmailNotification notif)
        {
            ViewModel?.DismissEmailMessage(notif);
            e.Handled = true;
        }
    }

    private void OpenEmail_Click(object sender, RoutedEventArgs e)
    {
        ViewModel?.OpenEmail();
    }

    private void MarkRead_Click(object sender, RoutedEventArgs e)
    {
        ViewModel?.MarkEmailAsRead();
    }

    private void CopyCode_Click(object sender, MouseButtonEventArgs e)
    {
        if (ViewModel != null)
        {
            ViewModel.CopyEmailVerificationCode();
            SetCopyFeedback(true);
            _copyFeedbackTimer.Stop();
            _copyFeedbackTimer.Start();
        }
    }

    private void SetCopyFeedback(bool copied)
    {
        CopyStatusText.Text = copied ? "✓ Copied!" : "Copy Code";
        CopyStatusText.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty,
            copied ? "IslandPositiveBrush" : "IslandAccentBrush");
    }

    private void DismissEmail_Click(object sender, RoutedEventArgs e)
    {
        ViewModel?.DismissEmail();
    }

    private void EmailScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is System.Windows.Controls.ScrollViewer scroller)
        {
            scroller.ScrollToVerticalOffset(scroller.VerticalOffset - (e.Delta / 3.0));
            e.Handled = true;
        }
    }
}
