using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using MochiDock.Models;
using MochiDock.ViewModels;
using DragDropEffects = System.Windows.DragDropEffects;
using DragEventArgs = System.Windows.DragEventArgs;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using UserControl = System.Windows.Controls.UserControl;

namespace MochiDock.Views.Cards;

public partial class FileShelfCardView : UserControl
{
    public FileShelfCardView()
    {
        InitializeComponent();
    }

    private MainViewModel? ViewModel => DataContext as MainViewModel;

    private void FileShelf_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Copy;
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }
        e.Handled = true;
    }

    private void FileShelf_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop))
        {
            var files = (string[])e.Data.GetData(System.Windows.DataFormats.FileDrop);
            if (files != null && ViewModel != null)
            {
                foreach (var f in files)
                {
                    ViewModel.AddShelfFile(f);
                }
            }
        }
        e.Handled = true;
    }

    private void ShelfItem_MouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed && sender is FrameworkElement fe && fe.DataContext is ShelfFileItem item)
        {
            if (File.Exists(item.FilePath))
            {
                var data = new System.Windows.DataObject(System.Windows.DataFormats.FileDrop, new string[] { item.FilePath });
                System.Windows.DragDrop.DoDragDrop(fe, data, DragDropEffects.Copy);
            }
        }
    }

    private void OpenFile_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is ShelfFileItem item)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"/select,\"{item.FilePath}\"",
                    UseShellExecute = true
                });
            }
            catch { }
        }
    }

    private void RemoveFile_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is ShelfFileItem item)
        {
            ViewModel?.RemoveShelfFile(item);
        }
    }

    private void ClearShelf_Click(object sender, RoutedEventArgs e)
    {
        ViewModel?.ClearShelfFiles();
    }

    private async void TogglePhoneDrop_Click(object sender, RoutedEventArgs e)
    {
        try { if (ViewModel != null) await ViewModel.StartPhoneDropServerAsync(); }
        catch (Exception ex) { System.Windows.MessageBox.Show(ex.Message, "Phone drop could not start"); }
    }
    private async void StopPhoneDrop_Click(object sender, RoutedEventArgs e)
    {
        try { if (ViewModel != null) await ViewModel.StopPhoneDropServerAsync(); }
        catch (Exception ex) { System.Windows.MessageBox.Show(ex.Message, "Phone drop could not stop; retry Stop"); }
    }
    private void ApproveDrop_Click(object sender, RoutedEventArgs e) => ViewModel?.AnswerDropProposal(true);
    private void RejectDrop_Click(object sender, RoutedEventArgs e) => ViewModel?.AnswerDropProposal(false);
    private void CopyPhoneLink_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel?.IsPhoneDropRunning == true)
        {
            System.Windows.Clipboard.SetText(ViewModel.DropServerUrl);
        }
    }
}
