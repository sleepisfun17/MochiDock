using System;
using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using MochiDock.Models;

namespace MochiDock.Services;

public class ClipboardService
{
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AddClipboardFormatListener(IntPtr hwnd);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RemoveClipboardFormatListener(IntPtr hwnd);

    private const int WM_CLIPBOARDUPDATE = 0x031D;
    private IntPtr _hwnd = IntPtr.Zero;
    private HwndSource? _hwndSource;
    private string _lastCopiedText = "";

    public ObservableCollection<ClipboardHistoryItem> History { get; } = new();

    public void Initialize(Window window)
    {
        var helper = new WindowInteropHelper(window);
        _hwnd = helper.EnsureHandle();
        _hwndSource = HwndSource.FromHwnd(_hwnd);
        _hwndSource?.AddHook(HwndHook);
        AddClipboardFormatListener(_hwnd);
    }

    public void Dispose()
    {
        if (_hwnd != IntPtr.Zero)
        {
            RemoveClipboardFormatListener(_hwnd);
            _hwndSource?.RemoveHook(HwndHook);
            _hwnd = IntPtr.Zero;
        }
    }

    private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_CLIPBOARDUPDATE)
        {
            OnClipboardChanged();
        }
        return IntPtr.Zero;
    }

    private void OnClipboardChanged()
    {
        try
        {
            if (System.Windows.Clipboard.ContainsText())
            {
                string text = System.Windows.Clipboard.GetText();
                if (!string.IsNullOrWhiteSpace(text) && !text.Equals(_lastCopiedText, StringComparison.Ordinal))
                {
                    _lastCopiedText = text;
                    App.Current.Dispatcher.Invoke(() =>
                    {
                        // Remove duplicate if exists
                        for (int i = 0; i < History.Count; i++)
                        {
                            if (History[i].Text.Equals(text, StringComparison.Ordinal))
                            {
                                if (History[i].IsPinned) return; // keep pinned in place
                                History.RemoveAt(i);
                                break;
                            }
                        }

                        History.Insert(0, new ClipboardHistoryItem { Text = text });
                        while (History.Count > 30)
                        {
                            // Remove oldest unpinned
                            int removeIdx = -1;
                            for (int i = History.Count - 1; i >= 0; i--)
                            {
                                if (!History[i].IsPinned) { removeIdx = i; break; }
                            }
                            if (removeIdx != -1) History.RemoveAt(removeIdx);
                            else break;
                        }
                    });
                }
            }
        }
        catch { }
    }

    public void CopyToClipboard(ClipboardHistoryItem item)
    {
        try
        {
            _lastCopiedText = item.Text;
            System.Windows.Clipboard.SetText(item.Text);
        }
        catch { }
    }

    public void TogglePin(ClipboardHistoryItem item)
    {
        item.IsPinned = !item.IsPinned;
    }

    public void DeleteItem(ClipboardHistoryItem item)
    {
        History.Remove(item);
    }

    public void ClearUnpinned()
    {
        for (int i = History.Count - 1; i >= 0; i--)
        {
            if (!History[i].IsPinned) History.RemoveAt(i);
        }
    }
}

