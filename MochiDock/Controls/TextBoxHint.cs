using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace MochiDock.Controls;

/// <summary>
/// The grey hint shown inside an empty text box. The hint is drawn by the shared text box template next to the text host, so
/// it starts exactly where the first typed character and the caret would, instead of being a separate overlay with its own margin.
/// </summary>
public static class TextBoxHint
{
    public static readonly DependencyProperty TextProperty =
        DependencyProperty.RegisterAttached("Text", typeof(string), typeof(TextBoxHint), new PropertyMetadata(""));

    public static string GetText(DependencyObject element) => (string)element.GetValue(TextProperty);
    public static void SetText(DependencyObject element, string value) => element.SetValue(TextProperty, value);
}

/// <summary>
/// Keeps only the left padding of a text box for its hint. The hint is inset like the first typed character, but its right side needs
/// no padding: repeating it on both sides made the hint trim early (the usable width was the box width minus 36).
/// </summary>
public sealed class LeftOnlyPaddingConverter : IValueConverter
{
    public static readonly LeftOnlyPaddingConverter Instance = new();
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is Thickness t ? new Thickness(t.Left, 0, 0, 0) : new Thickness(0);
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => System.Windows.Data.Binding.DoNothing;
}

/// <summary>An empty or missing string becomes null, so an empty hint does not produce an empty tooltip.</summary>
public sealed class NullIfEmptyConverter : IValueConverter
{
    public static readonly NullIfEmptyConverter Instance = new();
    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is string { Length: > 0 } s ? s : null;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => System.Windows.Data.Binding.DoNothing;
}
