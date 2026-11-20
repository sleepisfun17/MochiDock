using MochiDock.Core;

namespace MochiDock.ViewModels;

// Interface size (zoom): the user's choice is a setting; the dock window computes the size that really fits this display and publishes it back.
public partial class MainViewModel
{
    private InterfaceScaleResult _effectiveScale = new(InterfaceScale.Default, 1.0, false);

    /// <summary>The chosen interface size in percent (80 to 200).</summary>
    public int InterfaceScalePercent
    {
        get => InterfaceScale.Clamp(_settings.InterfaceScalePercent);
        set
        {
            int percent = InterfaceScale.Clamp(value);
            if (percent == InterfaceScale.Clamp(_settings.InterfaceScalePercent)) return;
            UpdateSettings(x => x.InterfaceScalePercent = percent, SettingsApply.Reposition | SettingsApply.Scale);
            OnPropertyChanged();
        }
    }

    /// <summary>The size actually in use: the chosen one, or the largest that fits this display.</summary>
    public int EffectiveInterfaceScalePercent => _effectiveScale.Percent;

    /// <summary>True when the chosen size does not fit the display, so a smaller one is in use.</summary>
    public bool IsInterfaceScaleLimited => _effectiveScale.Limited;

    /// <summary>One preset up (+1) or down (-1) from the current choice.</summary>
    public void StepInterfaceScale(int direction) =>
        InterfaceScalePercent = direction >= 0 ? InterfaceScale.Next(InterfaceScalePercent) : InterfaceScale.Previous(InterfaceScalePercent);

    /// <summary>Called by the dock window after it fitted the size to the display.</summary>
    public void PublishEffectiveInterfaceScale(InterfaceScaleResult result)
    {
        if (result == _effectiveScale) return;
        _effectiveScale = result;
        OnPropertyChanged(nameof(EffectiveInterfaceScalePercent));
        OnPropertyChanged(nameof(IsInterfaceScaleLimited));
    }
}
