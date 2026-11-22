using System.Windows;
using System.Windows.Controls;

namespace MochiDock.Views.Settings;

/// <summary>One settings row: label, dim description, a control (the content), an inline note and an inline validation message.</summary>
public class SettingRow : ContentControl
{
    public static readonly DependencyProperty LabelProperty =
        DependencyProperty.Register(nameof(Label), typeof(string), typeof(SettingRow), new PropertyMetadata(""));
    public static readonly DependencyProperty DescriptionProperty =
        DependencyProperty.Register(nameof(Description), typeof(string), typeof(SettingRow), new PropertyMetadata(""));
    public static readonly DependencyProperty ValidationProperty =
        DependencyProperty.Register(nameof(Validation), typeof(string), typeof(SettingRow), new PropertyMetadata(""));
    public static readonly DependencyProperty NoteProperty =
        DependencyProperty.Register(nameof(Note), typeof(string), typeof(SettingRow), new PropertyMetadata(""));

    static SettingRow() =>
        DefaultStyleKeyProperty.OverrideMetadata(typeof(SettingRow), new FrameworkPropertyMetadata(typeof(SettingRow)));

    public string Label { get => (string)GetValue(LabelProperty); set => SetValue(LabelProperty, value); }
    public string Description { get => (string)GetValue(DescriptionProperty); set => SetValue(DescriptionProperty, value); }
    /// <summary>Inline error text; empty hides the line.</summary>
    public string Validation { get => (string)GetValue(ValidationProperty); set => SetValue(ValidationProperty, value); }
    /// <summary>Inline result that is not an error (for example "Settings imported"), in the normal secondary ink; empty hides it.</summary>
    public string Note { get => (string)GetValue(NoteProperty); set => SetValue(NoteProperty, value); }
}
