using System.Windows.Controls;
using MochiDock.Core;
using MochiDock.ViewModels;
using ComboBox = System.Windows.Controls.ComboBox;
using UserControl = System.Windows.Controls.UserControl;

namespace MochiDock.Views.Settings.Sections;

/// <summary>Which tabs the dock shows, and the three currency pairs of the FX card. Every switch binds two-way to the view model, which saves and updates the dock.</summary>
public partial class TabsSectionView : UserControl
{
    private bool _loading;
    private MainViewModel? _subscribed;
    private readonly ComboBox[] _bases;
    private readonly ComboBox[] _quotes;
    private readonly SettingRow[] _rows;

    public TabsSectionView()
    {
        InitializeComponent();
        _bases = [FxBase1, FxBase2, FxBase3];
        _quotes = [FxQuote1, FxQuote2, FxQuote3];
        _rows = [FxRow1, FxRow2, FxRow3];
        foreach (var combo in _bases.Concat(_quotes))
            foreach (var currency in FxPairPolicy.Currencies)
                combo.Items.Add(new ComboBoxItem { Content = currency.Label, Tag = currency.Code });
        Loaded += (_, _) => { Subscribe(); LoadFromSettings(); };
        Unloaded += (_, _) => Unsubscribe();
        IsVisibleChanged += (_, _) => { if (IsVisible) LoadFromSettings(); };
    }

    private MainViewModel? ViewModel => DataContext as MainViewModel;

    private void Subscribe()
    {
        if (ReferenceEquals(_subscribed, ViewModel)) return;
        Unsubscribe();
        _subscribed = ViewModel;
        if (_subscribed != null) _subscribed.SettingsChanged += OnSettingsChanged;
    }

    private void Unsubscribe()
    {
        if (_subscribed != null) _subscribed.SettingsChanged -= OnSettingsChanged;
        _subscribed = null;
    }

    private void OnSettingsChanged(SettingsApply effects) => Dispatcher.BeginInvoke(LoadFromSettings);

    private static void Select(ComboBox combo, string code) =>
        combo.SelectedItem = combo.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == code);

    private static string? Code(ComboBox combo) => (combo.SelectedItem as ComboBoxItem)?.Tag as string;

    private void LoadFromSettings()
    {
        var vm = ViewModel;
        if (vm == null) return;
        _loading = true;
        try
        {
            var pairs = vm.FxPairs;
            for (int i = 0; i < pairs.Length; i++)
            {
                FxPairPolicy.TryParse(pairs[i], out var baseCode, out var quoteCode);
                Select(_bases[i], baseCode);
                Select(_quotes[i], quoteCode);
            }
        }
        finally { _loading = false; }
    }

    private void FxPair_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || _loading || ViewModel is not { } vm) return;
        var pairs = vm.FxPairs;
        int row = Array.FindIndex(_bases, c => ReferenceEquals(c, sender));
        if (row < 0) row = Array.FindIndex(_quotes, c => ReferenceEquals(c, sender));
        if (row < 0) return;
        foreach (var r in _rows) r.Validation = "";
        var baseCode = Code(_bases[row]);
        var quoteCode = Code(_quotes[row]);
        if (baseCode is null || quoteCode is null) return;
        string? refusal = null;
        if (baseCode == quoteCode) refusal = "Pick two different currencies.";
        else if (pairs.Where((p, i) => i != row).Contains(FxPairPolicy.Join(baseCode, quoteCode)))
            refusal = "That pair is already shown in another tile.";
        if (refusal != null)
        {
            LoadFromSettings();   // snap back to the saved pair
            _rows[row].Validation = refusal;
            return;
        }
        pairs[row] = FxPairPolicy.Join(baseCode, quoteCode);
        vm.SetFxPairs(pairs);
    }

    private void FxReset_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        foreach (var r in _rows) r.Validation = "";
        ViewModel?.SetFxPairs(FxPairPolicy.Defaults);
    }
}
