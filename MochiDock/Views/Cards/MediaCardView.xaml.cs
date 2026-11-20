using System.Windows;
using System.Windows.Input;
using MochiDock.ViewModels;
using UserControl = System.Windows.Controls.UserControl;

namespace MochiDock.Views.Cards;

public partial class MediaCardView : UserControl
{
    private bool _isDraggingSlider;

    public MediaCardView()
    {
        InitializeComponent();
        Loaded+=(_,_)=>{Detach();_owner=ViewModel;if(_owner!=null)_owner.PropertyChanged+=Changed;Sync();};
        Unloaded+=(_,_)=>{Cancel();Detach();};
    }

    private async void RetryLyrics_Click(object sender,RoutedEventArgs e){if(ViewModel!=null)await ViewModel.RetryLyricsAsync();}
    private void LyricsOffsetEarlier_Click(object sender,RoutedEventArgs e)=>ViewModel?.NudgeLyricsOffset(MochiDock.Core.LyricOffset.StepMs);
    private void LyricsOffsetLater_Click(object sender,RoutedEventArgs e)=>ViewModel?.NudgeLyricsOffset(-MochiDock.Core.LyricOffset.StepMs);
    private void LyricsOffsetReset_Click(object sender,System.Windows.Input.MouseButtonEventArgs e)=>ViewModel?.ResetLyricsOffset();
    private MainViewModel? ViewModel => DataContext as MainViewModel;

    private async void PlayPauseButton_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel != null) await ViewModel.TogglePlayPauseAsync();
    }

    private async void PrevButton_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel != null) await ViewModel.SkipPreviousAsync();
    }

    private async void NextButton_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel != null) await ViewModel.SkipNextAsync();
    }

    private void AudioSwitch_Click(object sender, RoutedEventArgs e)
    {
        ViewModel?.SwitchNextAudioDevice();
    }

    // Right-click lists every active output (the current one ticked) so one can be chosen directly instead of cycling.
    private void AudioSwitch_RightClick(object sender, MouseButtonEventArgs e)
    {
        if (ViewModel == null || sender is not System.Windows.Controls.Button button) return;
        e.Handled = true;
        var devices = ViewModel.GetAudioDevices();
        if (devices.Count == 0) return;

        var menu = new System.Windows.Controls.ContextMenu
        {
            Style = (Style)FindResource("ModernTrayMenuStyle"),
            PlacementTarget = button,
            Placement = System.Windows.Controls.Primitives.PlacementMode.Top,
        };
        var itemStyle = (Style)FindResource("ModernTrayMenuItemStyle");
        foreach (var device in devices)
        {
            var item = new System.Windows.Controls.MenuItem { Header = device.Name, IsCheckable = false, IsChecked = device.IsDefault, Style = itemStyle };
            string id = device.Id;
            item.Click += (s, args) => ViewModel?.SelectAudioDevice(id);
            menu.Items.Add(item);
        }
        menu.IsOpen = true;
    }

    private void MediaCard_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (ViewModel == null) return;
        e.Handled = true;
        float delta = e.Delta > 0 ? 2f : -2f;
        ViewModel.AdjustMasterVolume(delta);
    }


    private MainViewModel? _owner;
    private string _track="";
    private long _commandGeneration;
    private void Detach(){if(_owner!=null)_owner.PropertyChanged-=Changed;_owner=null;}
    private void Changed(object? s,System.ComponentModel.PropertyChangedEventArgs e){if(e.PropertyName==nameof(MainViewModel.CurrentMedia))Sync();}
    private string Identity=>ViewModel==null?"":MochiDock.Services.LrclibService.CreateIdentity(ViewModel.CurrentMedia.SessionIdentity,ViewModel.CurrentMedia.Title,ViewModel.CurrentMedia.Artist,ViewModel.CurrentMedia.Album,ViewModel.CurrentMedia.Duration.TotalSeconds).Key;
    private void Sync(){if(ViewModel==null)return;if(_track!=Identity){_track=Identity;Cancel();SeekErrorText.Text="";}if(!_isDraggingSlider){TrackSlider.SetCurrentValue(System.Windows.Controls.Slider.ValueProperty,ViewModel.CurrentMedia.Position.TotalSeconds);SeekPositionLabel.Text=ViewModel.CurrentMedia.Position.ToString(@"m\:ss");}}
    private void Preview(){SeekPositionLabel.Text=TimeSpan.FromSeconds(TrackSlider.Value).ToString(@"m\:ss");}
    private void Cancel(){_commandGeneration++;_isDraggingSlider=false;if(System.Windows.Input.Mouse.Captured is System.Windows.Controls.Primitives.Thumb thumb && thumb.IsDragging)thumb.CancelDrag();if(TrackSlider.IsMouseCaptured)TrackSlider.ReleaseMouseCapture();}
    private void Seek_DragStarted(object s,System.Windows.Controls.Primitives.DragStartedEventArgs e){_isDraggingSlider=true;SeekErrorText.Text="";}
    private async void Seek_DragCompleted(object s,System.Windows.Controls.Primitives.DragCompletedEventArgs e){if(e.Canceled){Cancel();Sync();return;}await Commit();}
    private async Task Commit(){if(!_isDraggingSlider||ViewModel==null)return;_isDraggingSlider=false;if(TrackSlider.IsMouseCaptured)TrackSlider.ReleaseMouseCapture();var identity=Identity;var target=TrackSlider.Value;var command=++_commandGeneration;try{var result=await ViewModel.SeekMediaAsync(target);if(identity!=Identity||command!=_commandGeneration)return;SeekErrorText.Text=result is MochiDock.Core.MediaSeekResult.Accepted or MochiDock.Core.MediaSeekResult.Cancelled?"":$"Seek {result.ToString().ToLowerInvariant()}. Try again in Spotify.";Sync();}finally{}}
    private void TrackSlider_PreviewMouseDown(object s,MouseButtonEventArgs e){
        if(e.ChangedButton!=MouseButton.Left||!TrackSlider.IsEnabled)return;
        var node=e.OriginalSource as DependencyObject;while(node!=null){if(node is System.Windows.Controls.Primitives.Thumb)return;node=System.Windows.Media.VisualTreeHelper.GetParent(node);}
        _isDraggingSlider=true;SeekErrorText.Text="";TrackSlider.CaptureMouse();Move(e.GetPosition(TrackSlider).X);e.Handled=true;
    }
    private void Move(double x){TrackSlider.SetCurrentValue(System.Windows.Controls.Slider.ValueProperty,Math.Clamp(x/Math.Max(1,TrackSlider.ActualWidth),0,1)*TrackSlider.Maximum);Preview();}
    private void TrackSlider_Move(object s,System.Windows.Input.MouseEventArgs e){if(_isDraggingSlider&&TrackSlider.IsMouseCaptured)Move(e.GetPosition(TrackSlider).X);else if(_isDraggingSlider)Preview();}
    private async void TrackSlider_PreviewMouseUp(object s,MouseButtonEventArgs e){if(TrackSlider.IsMouseCaptured){e.Handled=true;await Commit();}}
    private void TrackSlider_LostCapture(object s,System.Windows.Input.MouseEventArgs e){if(TrackSlider.IsMouseCaptured||e.OriginalSource is System.Windows.Controls.Primitives.Thumb)return;if(_isDraggingSlider){Cancel();Sync();}}
    private async void TrackSlider_KeyDown(object s,System.Windows.Input.KeyEventArgs e){if(e.Key==Key.Escape){Cancel();Sync();e.Handled=true;return;}if(!TrackSlider.IsEnabled)return;double? target=e.Key switch{Key.Home=>0,Key.End=>TrackSlider.Maximum,Key.Left=>TrackSlider.Value-5,Key.Right=>TrackSlider.Value+5,_=>null};if(target==null)return;_isDraggingSlider=true;TrackSlider.SetCurrentValue(System.Windows.Controls.Slider.ValueProperty,Math.Clamp(target.Value,0,TrackSlider.Maximum));e.Handled=true;await Commit();}
}
