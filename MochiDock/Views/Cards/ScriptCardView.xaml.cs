using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using MochiDock.Core;
using MochiDock.ViewModels;
using UserControl = System.Windows.Controls.UserControl;
using DragEventArgs = System.Windows.DragEventArgs;
using DataFormats = System.Windows.DataFormats;
using DragDropEffects = System.Windows.DragDropEffects;

namespace MochiDock.Views.Cards;

public partial class ScriptCardView : UserControl
{
    private MainViewModel? ViewModel => DataContext as MainViewModel;
    private readonly DispatcherTimer _scrollTimer;
    private bool _isPlaying;

    public ScriptCardView()
    {
        InitializeComponent();
        _scrollTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(40)
        };
        _scrollTimer.Tick += ScrollTimer_Tick;

        Loaded += (_, _) => Attach();
        Unloaded += (_, _) => Detach();
    }

    private void Attach()
    {
        if (ViewModel != null)
        {
            ExcludeCaptureCheck.IsChecked = ViewModel.ExcludeFromScreenCapture;
            WpmSlider.Value = ViewModel.ScriptWpm > 0 ? ViewModel.ScriptWpm : MochiDock.Core.ScriptReaderEngine.DefaultWpm;
        }
        UpdateWordCount();
    }

    private void Detach()
    {
        _scrollTimer.Stop();
        _isPlaying = false;
        UpdatePlayButton();
    }

    private void PlayPause_Click(object sender, RoutedEventArgs e)
    {
        _isPlaying = !_isPlaying;
        if (_isPlaying)
        {
            _scrollTimer.Start();
        }
        else
        {
            _scrollTimer.Stop();
        }
        UpdatePlayButton();
    }

    private void UpdatePlayButton()
    {
        PlayPauseIcon.Text = _isPlaying ? "⏸" : "▶";
    }

    private void ResetScroll_Click(object sender, RoutedEventArgs e)
    {
        _scrollTimer.Stop();
        _isPlaying = false;
        UpdatePlayButton();
        PrompterScrollViewer.ScrollToTop();
    }

    private void ScrollTimer_Tick(object? sender, EventArgs e)
    {
        if (!_isPlaying) return;
        int wpm = (int)WpmSlider.Value;
        // At 150 WPM, scroll speed is roughly 0.8 DIPs per 40ms tick
        double pixelsPerTick = (wpm / 150.0) * 0.9;
        double newOffset = PrompterScrollViewer.VerticalOffset + pixelsPerTick;

        if (newOffset >= PrompterScrollViewer.ScrollableHeight)
        {
            _scrollTimer.Stop();
            _isPlaying = false;
            UpdatePlayButton();
        }
        else
        {
            PrompterScrollViewer.ScrollToVerticalOffset(newOffset);
        }
    }

    private void WpmSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (WpmText == null) return;
        int wpm = (int)e.NewValue;
        WpmText.Text = $"{wpm} WPM";
        if (ViewModel != null)
        {
            ViewModel.ScriptWpm = wpm;
        }
        UpdateWordCount();
    }

    private void ScriptTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        UpdateWordCount();
    }

    private void UpdateWordCount()
    {
        if (ReadingEstimateText == null) return;
        string text = ScriptTextBox.Text ?? string.Empty;
        var words = text.Split(new[] { ' ', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        int wordCount = words.Length;
        int wpm = (int)WpmSlider.Value;
        var duration = ScriptReaderEngine.CalculateReadingTime(wordCount, wpm);

        ReadingEstimateText.Text = $"{(int)duration.TotalMinutes}m {duration.Seconds:D2}s ({wordCount} words)";
    }

    private void ClearText_Click(object sender, RoutedEventArgs e)
    {
        ScriptTextBox.Text = string.Empty;
        ResetScroll_Click(sender, e);
    }

    private void ExcludeCaptureCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (ViewModel != null)
        {
            ViewModel.ExcludeFromScreenCapture = ExcludeCaptureCheck.IsChecked == true;
        }
    }

    private void ScriptArea_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Copy;
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }
        e.Handled = true;
    }

    private void ScriptArea_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            var files = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (files != null && files.Length > 0)
            {
                var file = files[0];
                var ext = Path.GetExtension(file).ToLowerInvariant();
                if (ext is ".txt" or ".md" or ".rtf" or ".srt")
                {
                    try
                    {
                        var content = File.ReadAllText(file);
                        ScriptTextBox.Text = ScriptReaderEngine.CleanScript(content);
                        ResetScroll_Click(sender, e);
                    }
                    catch { }
                }
            }
        }
        e.Handled = true;
    }
}

