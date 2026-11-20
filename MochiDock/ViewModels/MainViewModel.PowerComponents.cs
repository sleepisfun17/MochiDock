using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using MochiDock.Core;
using MochiDock.Services;

namespace MochiDock.ViewModels;

/// <summary>One editable row of the "Power estimate" list in Hardware settings, wrapping the saved <see cref="PowerComponent"/>.</summary>
public sealed class PowerComponentItem : INotifyPropertyChanged
{
    private readonly PowerComponent _model;
    private readonly Action _changed;

    public PowerComponentItem(PowerComponent model, Action changed) { _model = model; _changed = changed; }

    public event PropertyChangedEventHandler? PropertyChanged;
    public PowerComponent Model => _model;
    public string Name => _model.Name;
    public string KindLabel => PowerComponentDefaults.Label(_model.Kind);
    public bool CanRemove => !_model.Detected;

    public bool Enabled
    {
        get => _model.Enabled;
        set { if (_model.Enabled == value) return; _model.Enabled = value; _model.Edited = true; Edited(nameof(Enabled)); }
    }

    public int Count
    {
        get => _model.Count;
        set { var v = Math.Clamp(value, 0, 99); if (_model.Count == v) return; _model.Count = v; _model.Edited = true; Edited(nameof(Count)); }
    }

    public double Watts
    {
        get => _model.Watts;
        set { var v = Math.Round(Math.Clamp(double.IsFinite(value) ? value : 0, 0, PowerAllowance.MaxItemWatts), 1); if (_model.Watts == v) return; _model.Watts = v; _model.Edited = true; Edited(nameof(Watts)); }
    }

    /// <summary>Typical draw of one unit of this part, shown in the tooltip next to the editable number.</summary>
    public double TypicalWatts => PowerComponentDefaults.Watts(_model.Kind, _model.Name.Contains("DDR5", StringComparison.OrdinalIgnoreCase));
    public string WattsTooltip => $"Average draw of one unit in watts. Typical: {TypicalWatts:0.#} W";
    /// <summary>Quantity times watts for this row ("8 W"), or a dash while the part is switched off.</summary>
    public string SubtotalText => _model.Enabled ? $"{PowerAllowance.ItemWatts(_model):0.#} W" : "—";

    // The boxes bind to text so a typo never raises a binding error: text that is not a number puts the previous value back,
    // an out-of-range number is clamped, and either way the box is refreshed to what was actually kept.
    public string CountText
    {
        get => Count.ToString(CultureInfo.CurrentCulture);
        set
        {
            if (int.TryParse(value?.Trim(), NumberStyles.Integer, CultureInfo.CurrentCulture, out var n)) Count = n;
            RefreshLater(nameof(CountText));
        }
    }

    public string WattsText
    {
        get => Watts.ToString("0.#", CultureInfo.CurrentCulture);
        set
        {
            var text = (value ?? "").Trim().TrimEnd('W', 'w').Trim();
            if (double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out var w) || double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out w)) Watts = w;
            RefreshLater(nameof(WattsText));
        }
    }

    public void Refresh() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));

    // A setter that changes what it was given is only picked up by the box when the notification comes after the binding finished updating.
    private void RefreshLater(string property)
    {
        void Raise() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
        if (System.Windows.Application.Current?.Dispatcher is { } dispatcher) dispatcher.BeginInvoke(Raise, System.Windows.Threading.DispatcherPriority.Background);
        else Raise();
    }

    private void Edited(string property)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SubtotalText)));
        _changed();
    }
}

// The PC's other power consumers (board, RAM, drives, fans...) as an editable list; their sum replaces the fixed allowance in the power estimate.
public partial class MainViewModel
{
    private ObservableCollection<PowerComponentItem>? _powerComponentItems;
    // Read by the power-log timer thread: recomputed on the UI thread after every change, never built from the live list there.
    private double _powerAllowance = PowerComponentDefaults.FallbackAllowanceWatts;
    private string? _powerAllowanceNote;
    private bool _detectingComponents;

    public ObservableCollection<PowerComponentItem> PowerComponentItems => _powerComponentItems ??= BuildPowerItems();

    /// <summary>The total of the listed parts ("42 W"), or the fixed fallback while nothing is set up.</summary>
    public string PowerAllowanceTotalText => (_settings.PowerComponents?.Count ?? 0) == 0
        ? $"{PowerComponentDefaults.FallbackAllowanceWatts:0} W (assumed)"
        : $"{PowerAllowance.Total(_settings.PowerComponents):0.#} W";

    /// <summary>What follows the total in the settings panel.</summary>
    public string PowerAllowanceHint => (_settings.PowerComponents?.Count ?? 0) == 0
        ? " · nothing set up yet, press Detect components"
        : " · typical values, not measurements";

    private ObservableCollection<PowerComponentItem> BuildPowerItems()
    {
        _settings.PowerComponents ??= new();
        return new ObservableCollection<PowerComponentItem>(_settings.PowerComponents.Select(c => new PowerComponentItem(c, OnPowerComponentEdited)));
    }

    private void OnPowerComponentEdited()
    {
        SettingsService.Save(_settings);
        RecomputePowerAllowance();
    }

    /// <summary>Refreshes the total the power log and the Battery page use, and the explanation line in settings.</summary>
    public void RecomputePowerAllowance()
    {
        var list = _settings.PowerComponents;
        _powerAllowance = PowerAllowance.Total(list);
        _powerAllowanceNote = list is { Count: > 0 } ? PowerAllowance.Describe(list) : null;
        OnPropertyChanged(nameof(PowerAllowanceTotalText));
        OnPropertyChanged(nameof(PowerAllowanceHint));
    }

    /// <summary>Detects board, memory and drives in the background and merges them into the list, keeping the user's edits.</summary>
    public async Task DetectPowerComponentsAsync()
    {
        if (_detectingComponents) return;
        _detectingComponents = true;
        try
        {
            var found = await Task.Run(PowerComponentScanner.Scan).ConfigureAwait(true);
            if (found.Count == 0) return;
            _settings.PowerComponents = PowerComponentMerge.Merge(_settings.PowerComponents ?? new(), found);
            ReloadPowerItems();
            SettingsService.Save(_settings);
        }
        catch (Exception ex) { AppLog.Warn("PowerComponents", ex); }
        finally { _detectingComponents = false; }
    }

    public void AddPowerComponent(PowerComponentKind kind, string name)
    {
        name = string.IsNullOrWhiteSpace(name) ? PowerComponentDefaults.Label(kind) : name.Trim();
        var model = new PowerComponent { Id = PowerComponentMerge.NewId(name), Name = name, Kind = kind, Watts = PowerComponentDefaults.Watts(kind), Detected = false };
        (_settings.PowerComponents ??= new()).Add(model);
        PowerComponentItems.Add(new PowerComponentItem(model, OnPowerComponentEdited));
        OnPowerComponentEdited();
    }

    public void RemovePowerComponent(PowerComponentItem item)
    {
        if (item.Model.Detected) return;
        _settings.PowerComponents?.Remove(item.Model);
        PowerComponentItems.Remove(item);
        OnPowerComponentEdited();
    }

    /// <summary>Puts every item back to its typical wattage, switched on (counts are kept).</summary>
    public void ResetPowerComponents()
    {
        foreach (var item in PowerComponentItems)
        {
            item.Model.Watts = PowerComponentDefaults.Watts(item.Model.Kind, item.Model.Name.Contains("DDR5", StringComparison.OrdinalIgnoreCase));
            item.Model.Enabled = true;
            item.Model.Edited = false;
            item.Refresh();
        }
        OnPowerComponentEdited();
    }

    private void ReloadPowerItems()
    {
        var items = PowerComponentItems;
        items.Clear();
        foreach (var c in _settings.PowerComponents ?? new()) items.Add(new PowerComponentItem(c, OnPowerComponentEdited));
        RecomputePowerAllowance();
    }
}
