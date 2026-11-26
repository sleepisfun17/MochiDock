using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Animation;
using MochiDock.Services;
using Button = System.Windows.Controls.Button;
using FontFamily = System.Windows.Media.FontFamily;
using Point = System.Windows.Point;
using Orientation = System.Windows.Controls.Orientation;
using HorizontalAlignment = System.Windows.HorizontalAlignment;

namespace MochiDock.Controls;

/// <summary>
/// Visible feedback for a refresh, reload or reconnect button: while busy the button is disabled and its refresh glyph spins; when the
/// work ends it shows "Updated HH:mm" (or the error) in a status text, or in the button's tooltip when there is none. A fast refresh is
/// kept busy for a short minimum time so the click is never silent. With Windows animations off the glyph stays still (the button is
/// still disabled and dimmed).
/// </summary>
public static class RefreshFeedback
{
    /// <summary>Shortest time a button stays busy, so a refresh that finishes at once still shows something happened.</summary>
    public static readonly TimeSpan MinimumBusy = TimeSpan.FromMilliseconds(400);

    private const string Glyph = ""; // Segoe Fluent Icons "Refresh"
    private static readonly FontFamily IconFont = new("Segoe Fluent Icons, Segoe MDL2 Assets");

    public static readonly DependencyProperty IsBusyProperty =
        DependencyProperty.RegisterAttached("IsBusy", typeof(bool), typeof(RefreshFeedback), new PropertyMetadata(false, OnIsBusyChanged));

    public static bool GetIsBusy(DependencyObject element) => (bool)element.GetValue(IsBusyProperty);
    public static void SetIsBusy(DependencyObject element, bool value) => element.SetValue(IsBusyProperty, value);

    // What the button looked like before it became busy (content, bindings, tooltip and width), so it can be put back exactly.
    private sealed record Saved(object? Content, object? ToolTip, double MinWidth, FrameworkElement? Spinner, bool OwnTransform, Transform? Transform, Point Origin, BindingBase? ContentBinding, BindingBase? IsEnabledBinding, bool WasEnabled);
    private static readonly DependencyProperty SavedProperty = DependencyProperty.RegisterAttached("Saved", typeof(Saved), typeof(RefreshFeedback));
    private static readonly DependencyProperty BaseToolTipProperty = DependencyProperty.RegisterAttached("BaseToolTip", typeof(object), typeof(RefreshFeedback));

    private static void OnIsBusyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Button button) return;
        if ((bool)e.NewValue) Begin(button); else End(button);
    }

    private static void Begin(Button button)
    {
        var width = button.ActualWidth;
        var contentBinding = BindingOperations.GetBindingBase(button, ContentControl.ContentProperty);
        var isEnabledBinding = BindingOperations.GetBindingBase(button, UIElement.IsEnabledProperty);
        var wasEnabled = button.IsEnabled;
        var (content, spinner, ownTransform) = Prepare(button);
        button.SetValue(SavedProperty, new Saved(button.Content, button.ToolTip, button.MinWidth, spinner, ownTransform, spinner?.RenderTransform, spinner?.RenderTransformOrigin ?? default, contentBinding, isEnabledBinding, wasEnabled));
        if (!ReferenceEquals(content, button.Content)) button.Content = content;
        if (width > 0) button.MinWidth = width; // the spinner and the text must not make the button jump
        button.IsEnabled = false;
        if (spinner is not null && SystemParameters.ClientAreaAnimation)
        {
            var rotate = new RotateTransform();
            spinner.RenderTransformOrigin = new Point(0.5, 0.5);
            spinner.RenderTransform = rotate;
            rotate.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(0, 360, TimeSpan.FromSeconds(0.9)) { RepeatBehavior = RepeatBehavior.Forever });
        }
    }

    private static void End(Button button)
    {
        if (button.GetValue(SavedProperty) is not Saved saved) { button.IsEnabled = true; return; }
        if (button.GetValue(SavedProperty) is Saved && saved.Spinner is { } spinner)
        {
            if (spinner.RenderTransform is RotateTransform rotate) rotate.BeginAnimation(RotateTransform.AngleProperty, null);
            spinner.RenderTransform = saved.Transform ?? Transform.Identity;
            spinner.RenderTransformOrigin = saved.Origin;
        }
        if (saved.ContentBinding is not null)
            BindingOperations.SetBinding(button, ContentControl.ContentProperty, saved.ContentBinding);
        else
            button.Content = saved.Content;
        button.MinWidth = saved.MinWidth;
        button.ClearValue(SavedProperty);
        if (saved.IsEnabledBinding is not null)
            BindingOperations.SetBinding(button, UIElement.IsEnabledProperty, saved.IsEnabledBinding);
        else
            button.IsEnabled = saved.WasEnabled;
    }

    /// <summary>Picks the element that spins. Plain text content is wrapped (glyph + the text without its arrow); a glyph element inside the content spins as it is.</summary>
    private static (object? Content, FrameworkElement? Spinner, bool OwnTransform) Prepare(Button button)
    {
        if (button.Content is string text)
        {
            var label = text.TrimStart('↻', ' ', '⟳').TrimEnd();
            var spinner = new TextBlock { Text = Glyph, FontFamily = IconFont, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
            if (label.Length == 0) return (spinner, spinner, true);
            var panel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            spinner.Margin = new Thickness(0, 0, 6, 0);
            panel.Children.Add(spinner);
            panel.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });
            return (panel, spinner, true);
        }
        var glyph = FindGlyph(button.Content as DependencyObject);
        return (button.Content, glyph, false);
    }

    private static bool IsRefreshGlyph(string? text)
    {
        var t = text?.Trim();
        return !string.IsNullOrEmpty(t) && (t == Glyph || t[0] is '↻' or '⟳');
    }

    private static FrameworkElement? FindGlyph(DependencyObject? root)
    {
        if (root is TextBlock tb && IsRefreshGlyph(tb.Text)) return tb;
        if (root is null) return null;
        foreach (var child in LogicalTreeHelper.GetChildren(root))
            if (child is DependencyObject d && FindGlyph(d) is { } found) return found;
        return null;
    }

    /// <summary>
    /// Runs <paramref name="work"/> with the button busy. Afterwards writes "Updated HH:mm" (or "Could not refresh: ...") into
    /// <paramref name="status"/>, or into the button's tooltip when there is no status text. A second click while busy is ignored.
    /// </summary>
    public static async Task RunAsync(Button button, Func<Task> work, TextBlock? status = null)
    {
        if (GetIsBusy(button)) return;
        if (button.GetValue(BaseToolTipProperty) is null) button.SetValue(BaseToolTipProperty, button.ToolTip ?? "");
        var clock = Stopwatch.StartNew();
        SetIsBusy(button, true);
        string? error = null;
        try { await work().ConfigureAwait(true); }
        catch (Exception ex)
        {
            error = ex.Message;
            AppLog.Warn("Refresh", ex);
        }
        var remaining = MinimumBusy - clock.Elapsed;
        if (remaining > TimeSpan.Zero) await Task.Delay(remaining).ConfigureAwait(true);
        SetIsBusy(button, false);

        var result = error is null ? $"Updated {DateTime.Now:HH:mm}" : "Could not refresh: " + error;
        if (status is not null) status.Text = result;
        else if (BindingOperations.GetBindingBase(button, FrameworkElement.ToolTipProperty) is null)
        {
            var baseTip = button.GetValue(BaseToolTipProperty)?.ToString();
            button.ToolTip = string.IsNullOrEmpty(baseTip) ? result : baseTip + "\n" + result;
        }
    }
}
