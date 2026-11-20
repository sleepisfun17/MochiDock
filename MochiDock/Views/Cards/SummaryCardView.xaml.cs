using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using MochiDock.ViewModels;

namespace MochiDock.Views.Cards;

public partial class SummaryCardView : System.Windows.Controls.UserControl
{
    public SummaryCardView()
    {
        InitializeComponent();
    }

    private MainViewModel? ViewModel => DataContext as MainViewModel;

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        ViewModel?.DismissSummary();
    }

    private void Dismiss_Click(object sender, RoutedEventArgs e)
    {
        ViewModel?.DismissSummary();
    }

    private void OpenWhatsApp_Click(object sender, RoutedEventArgs e)
    {
        ViewModel?.LaunchWhatsAppApp();
        ViewModel?.DismissSummary();
    }

    private void OpenMail_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo("mailto:") { UseShellExecute = true });
        }
        catch { }
        ViewModel?.DismissSummary();
    }
}