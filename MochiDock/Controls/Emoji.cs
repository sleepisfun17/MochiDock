using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using MochiDock.Core;
using MochiDock.Services;

namespace MochiDock.Controls;

/// <summary>
/// <c>controls:Emoji.Text="{Binding Message}"</c> in place of <c>Text</c>: the text is shown as usual, but each emoji is drawn as its
/// coloured picture (WPF text draws emoji in black and white). Text without emoji costs nothing extra. An emoji with no picture stays text.
/// </summary>
public static class Emoji
{
    public static readonly DependencyProperty TextProperty =
        DependencyProperty.RegisterAttached("Text", typeof(string), typeof(Emoji), new PropertyMetadata(null, OnTextChanged));

    public static string? GetText(DependencyObject element) => (string?)element.GetValue(TextProperty);
    public static void SetText(DependencyObject element, string? value) => element.SetValue(TextProperty, value);

    private static void OnTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TextBlock block) Apply(block, e.NewValue as string ?? "");
    }

    private static void Apply(TextBlock block, string value)
    {
        var runs = EmojiText.Split(value);
        bool any = false;
        foreach (var run in runs)
        {
            if (run.IsEmoji && EmojiImages.Get(run.Key!) != null) { any = true; break; }
        }
        if (!any)
        {
            block.Text = value;   // also clears the inlines of an earlier message
            return;
        }

        block.Inlines.Clear();
        double size = Math.Max(8, block.FontSize * 1.25);
        foreach (var run in runs)
        {
            var image = run.IsEmoji ? EmojiImages.Get(run.Key!) : null;
            if (image == null) { block.Inlines.Add(new Run(run.Text)); continue; }
            var picture = new System.Windows.Controls.Image
            {
                Source = image,
                Width = size,
                Height = size,
                Stretch = Stretch.Uniform,
                Margin = new Thickness(0.5, 0, 0.5, 0),
            };
            RenderOptions.SetBitmapScalingMode(picture, BitmapScalingMode.HighQuality);
            block.Inlines.Add(new InlineUIContainer(picture) { BaselineAlignment = BaselineAlignment.Center });
        }
    }
}
