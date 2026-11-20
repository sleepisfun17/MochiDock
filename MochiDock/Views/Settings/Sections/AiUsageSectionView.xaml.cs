using System.Windows;
using CheckBox = System.Windows.Controls.CheckBox;

namespace MochiDock.Views.Settings.Sections;

/// <summary>AI usage tracking options.</summary>
public partial class AiUsageSectionView : SettingsSectionBase
{
    private static readonly (string Id, string Name)[] Providers =
    {
        ("claude", "Claude Code"), ("codex", "Codex"), ("antigravity", "Antigravity"),
        ("cursor", "Cursor"), ("gemini", "Gemini CLI"), ("copilot", "GitHub Copilot")
    };

    public AiUsageSectionView() => InitializeComponent();

    protected override void LoadFromSettings()
    {
        var vm = ViewModel;
        if (vm == null) return;
        ProviderPanel.Children.Clear();
        foreach (var (id, name) in Providers)
        {
            var box = new CheckBox { Content = name, Tag = id, IsChecked = vm.IsAiProviderEnabled(id), Margin = new Thickness(0, 2, 0, 2) };
            box.SetResourceReference(FrameworkElement.StyleProperty, "ModernDarkCheckBox");
            box.Checked += Provider_Changed;
            box.Unchecked += Provider_Changed;
            ProviderPanel.Children.Add(box);
        }
    }

    private void Provider_Changed(object sender, RoutedEventArgs e)
    {
        if (Loading || ViewModel == null || sender is not CheckBox { Tag: string id } box) return;
        ViewModel.SetAiProviderEnabled(id, box.IsChecked == true);
    }
}
