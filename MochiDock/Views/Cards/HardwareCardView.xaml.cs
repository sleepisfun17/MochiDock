using System.Windows;
using System.Windows.Input;
using MochiDock.ViewModels;
using UserControl = System.Windows.Controls.UserControl;

namespace MochiDock.Views.Cards;

public partial class HardwareCardView : UserControl
{
    private readonly MochiDock.Core.SelectionGesture _chipGesture = new();
    private FrameworkElement? _pressedChip;

    public HardwareCardView()
    {
        InitializeComponent();
        Unloaded += (_, _) => CancelChipPress();
        IsVisibleChanged += (_, _) => { if (!IsVisible) CancelChipPress(); };
        IsHitTestVisibleChanged += (_, _) => { if (!IsHitTestVisible) CancelChipPress(); };
        LostMouseCapture += (_, _) => CancelChipPress();
    }

    private void CancelChipPress()
    {
        var chip = _pressedChip;
        _pressedChip = null;
        _chipGesture.Cancel();
        if (chip?.IsMouseCaptured == true) chip.ReleaseMouseCapture();
    }

    private void ModuleChip_Press(object sender, MouseButtonEventArgs e)
    {
        CancelChipPress();
        if (e.ChangedButton != MouseButton.Left || sender is not FrameworkElement chip || !chip.IsVisible || !chip.IsHitTestVisible) return;
        if (!chip.CaptureMouse()) return;
        _pressedChip = chip;
        _chipGesture.Press(chip);
        e.Handled = true;
    }

    private bool CompleteChipClick(object sender, MouseButtonEventArgs e)
    {
        bool accepted = sender is FrameworkElement chip &&
            _chipGesture.Release(chip, e.ChangedButton == MouseButton.Left && chip.IsVisible && chip.IsHitTestVisible &&
                new Rect(0, 0, chip.ActualWidth, chip.ActualHeight).Contains(e.GetPosition(chip)));
        CancelChipPress();
        if (accepted) e.Handled = true;
        return accepted;
    }

    private void BatteryChip_Click(object sender, MouseButtonEventArgs e)
    {
        if (!CompleteChipClick(sender, e)) return;
        ViewModel?.SelectSystemModule("Battery");
    }
    private MainViewModel? ViewModel => DataContext as MainViewModel;

    private void CpuChip_Click(object sender, MouseButtonEventArgs e)
    {
        if (!CompleteChipClick(sender, e)) return;
        ViewModel?.SelectSystemModule("CPU");
    }

    private void GpuChip_Click(object sender, MouseButtonEventArgs e)
    {
        if (!CompleteChipClick(sender, e)) return;
        ViewModel?.SelectSystemModule("GPU");
    }

    private void MemoryChip_Click(object sender, MouseButtonEventArgs e)
    {
        if (!CompleteChipClick(sender, e)) return;
        ViewModel?.SelectSystemModule("Memory");
    }

    private void DrivesChip_Click(object sender, MouseButtonEventArgs e)
    {
        if (!CompleteChipClick(sender, e)) return;
        ViewModel?.SelectSystemModule("Drives");
    }

    private void NetworkChip_Click(object sender, MouseButtonEventArgs e)
    {
        if (!CompleteChipClick(sender, e)) return;
        ViewModel?.SelectSystemModule("Network");
    }

    private void GamesChip_Click(object sender, MouseButtonEventArgs e)
    {
        if (!CompleteChipClick(sender, e)) return;
        ViewModel?.SelectSystemModule("Games");
    }
    private void ChartSpan_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        (DataContext as MochiDock.ViewModels.MainViewModel)?.ToggleStatsChartSpan();
        e.Handled = true;
    }
}
