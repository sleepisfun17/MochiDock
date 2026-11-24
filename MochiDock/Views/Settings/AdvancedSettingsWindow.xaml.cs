using UserControl = System.Windows.Controls.UserControl;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using MochiDock.Core;
using MochiDock.Services;
using MochiDock.ViewModels;
using MochiDock.Views.Settings.Sections;

namespace MochiDock.Views.Settings;

/// <summary>
/// The Advanced Settings window: section sidebar on the left, the selected section on the right.
/// It is an ordinary top-level window (no owner, not topmost) so it neither hides with the dock nor inherits its z-order.
/// </summary>
public partial class AdvancedSettingsWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly Dictionary<string, UserControl> _sections = new(StringComparer.OrdinalIgnoreCase);
    private bool _selecting;
    private readonly Dictionary<string, double> _scrollOffsets = new(StringComparer.OrdinalIgnoreCase);
    private string? _shownSectionId;

    public AdvancedSettingsWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        // Grouped view: the group headings come from SettingsSection.Group; selection still holds the section objects.
        var sections = new System.Windows.Data.ListCollectionView(SettingsSections.All.ToList());
        sections.GroupDescriptions.Add(new System.Windows.Data.PropertyGroupDescription(nameof(SettingsSection.Group)));
        SidebarList.ItemsSource = sections;
        SourceInitialized += (s, e) => ApplyChrome(_viewModel.CapsuleTheme);
        PreviewKeyDown += AdvancedSettingsWindow_PreviewKeyDown;
        ApplyInterfaceScale();
        _viewModel.PropertyChanged += ViewModelPropertyChanged;
        Closed += (s, e) => _viewModel.PropertyChanged -= ViewModelPropertyChanged;
    }

    // The unscaled design size; the window is this times the interface scale, never larger than the display it is on.
    private const double BaseWidth = 900, BaseHeight = 620, BaseMinWidth = 760, BaseMinHeight = 520;
    private double _appliedScale = 1.0;
    private bool _scaleInitialised;

    private void AdvancedSettingsWindow_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != Key.Escape || e.Handled) return;
        if (Keyboard.FocusedElement is System.Windows.Controls.TextBox { IsReadOnly: true }) return;
        if (Keyboard.FocusedElement is System.Windows.Controls.ComboBox { IsDropDownOpen: true }) return;
        Close();
        e.Handled = true;
    }

    private void ViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.EffectiveInterfaceScalePercent)) Dispatcher.BeginInvoke(ApplyInterfaceScale);
    }

    /// <summary>
    /// Scales the settings content like the dock (same effective size, lowered if the window would not fit the display), live: a window the user
    /// already resized keeps its proportions when the size changes.
    /// </summary>
    private void ApplyInterfaceScale()
    {
        double scale = _viewModel.EffectiveInterfaceScalePercent / 100.0;
        double workWidth = SystemParameters.WorkArea.Width, workHeight = SystemParameters.WorkArea.Height;
        try
        {
            var screen = MultiMonitorHelper.GetTargetScreen(_viewModel.Settings.TargetMonitor);
            if (screen != null && screen.WorkingArea.Width > 0 && screen.WorkingArea.Height > 0)
            {
                var (dpiX, dpiY) = MultiMonitorHelper.GetDpiScale(this);
                workWidth = screen.WorkingArea.Width / dpiX;
                workHeight = screen.WorkingArea.Height / dpiY;
            }
        }
        catch (Exception ex) { AppLog.Warn("Settings", ex); }
        scale = Math.Max(0.5, Math.Min(scale, Math.Min(workWidth / BaseMinWidth, workHeight / BaseMinHeight)));

        SettingsRoot.LayoutTransform = new System.Windows.Media.ScaleTransform(scale, scale);
        MinWidth = Math.Min(BaseMinWidth * scale, workWidth);
        MinHeight = Math.Min(BaseMinHeight * scale, workHeight);
        double ratio = _scaleInitialised ? scale / _appliedScale : 1.0;
        double width = _scaleInitialised ? Width * ratio : BaseWidth * scale;
        double height = _scaleInitialised ? Height * ratio : BaseHeight * scale;
        Width = Math.Min(Math.Max(width, MinWidth), workWidth);
        Height = Math.Min(Math.Max(height, MinHeight), workHeight);
        _appliedScale = scale;
        _scaleInitialised = true;
    }

    public string CurrentSectionId => (SidebarList.SelectedItem as SettingsSection)?.Id ?? SettingsSections.All[0].Id;

    /// <summary>Selects a section by id or deep-link alias; unknown targets select the first section.</summary>
    public void NavigateTo(string? target)
    {
        var section = SettingsSections.Find(SettingsSections.Resolve(target))!;
        if (!ReferenceEquals(SidebarList.SelectedItem, section)) SidebarList.SelectedItem = section;
        else ShowSection(section);
    }

    private void SidebarList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SidebarList.SelectedItem is SettingsSection section) ShowSection(section);
    }

    private void ShowSection(SettingsSection section)
    {
        if (_selecting) return;
        _selecting = true;
        try
        {
            // Text boxes commit on LostFocus, so leaving the section commits whatever is being edited.
            Keyboard.ClearFocus();
            if (!_sections.TryGetValue(section.Id, out var view))
            {
                view = CreateSection(section.Id);
                _sections[section.Id] = view;
            }
            // Each section keeps its own scroll position while the window is open; a section not visited yet starts at the top.
            bool switching = _shownSectionId != section.Id;
            if (switching && _shownSectionId != null) _scrollOffsets[_shownSectionId] = SectionScroller.VerticalOffset;
            SectionTitle.Text = section.Title;
            SectionDescription.Text = section.Description;
            SectionHost.Content = view;
            _shownSectionId = section.Id;
            if (switching)
            {
                double offset = _scrollOffsets.TryGetValue(section.Id, out double saved) ? saved : 0;
                SectionScroller.ScrollToVerticalOffset(0);
                // The new content's height is known only after layout; skip the restore if another section was opened meanwhile.
                if (offset > 0)
                    Dispatcher.BeginInvoke(() => { if (ReferenceEquals(SectionHost.Content, view)) SectionScroller.ScrollToVerticalOffset(offset); },
                        System.Windows.Threading.DispatcherPriority.Loaded);
            }
        }
        finally { _selecting = false; }
    }

    // Sections are created on first visit and then kept. Sections not yet moved here show a placeholder.
    private UserControl CreateSection(string id) => id switch
    {
        SettingsSections.General => new GeneralSectionView { DataContext = _viewModel },
        SettingsSections.Data => new DataSectionView { DataContext = _viewModel },
        SettingsSections.WhatsApp => new WhatsAppSectionView { DataContext = _viewModel },
        SettingsSections.Email => new EmailSectionView { DataContext = _viewModel },
        SettingsSections.Notifications => new NotificationsSectionView { DataContext = _viewModel },
        SettingsSections.Appearance => new AppearanceSectionView { DataContext = _viewModel },
        SettingsSections.Gaming => new GamingSectionView { DataContext = _viewModel },
        SettingsSections.Hardware => new HardwareSectionView { DataContext = _viewModel },
        SettingsSections.Taskbar => new TaskbarSectionView { DataContext = _viewModel },
        SettingsSections.Location => new LocationSectionView { DataContext = _viewModel },
        SettingsSections.Calendar => new CalendarSectionView { DataContext = _viewModel },
        SettingsSections.Tabs => new TabsSectionView { DataContext = _viewModel },
        SettingsSections.Music => new MusicSectionView { DataContext = _viewModel },
        SettingsSections.AiUsage => new AiUsageSectionView { DataContext = _viewModel },
        _ => new PlaceholderSectionView { DataContext = _viewModel },
    };

    protected override void OnClosing(CancelEventArgs e)
    {
        Keyboard.ClearFocus(); // commit a text box that still has focus
        base.OnClosing(e);
    }

    // ---- Caption colour: follow the capsule theme (dark caption for Default, light for Matcha) ------------------------------
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

    public void ApplyChrome(string theme)
    {
        try
        {
            var handle = new WindowInteropHelper(this).Handle;
            if (handle == IntPtr.Zero) return;
            int dark = MochiDock.Core.ThemePolicy.IsDark(theme) ? 1 : 0;   // Default, Hojicha and KuroNight get the dark caption
            DwmSetWindowAttribute(handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));
        }
        catch (Exception ex) { AppLog.Warn("Settings", ex); }
    }
}
