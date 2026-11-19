using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using PoliceImageToolkit.ViewModels;

namespace PoliceImageToolkit.Views;

public partial class VideoSnapshotView : UserControl
{
    private readonly DispatcherTimer _timer;
    private readonly MediaPlayer _mediaPlayer;
    private readonly VideoDrawing _videoDrawing;
    private readonly DrawingImage _playerImage;
    private bool _isUserDraggingSlider = false;
    private int _seekGeneration;
    private readonly DispatcherTimer _seekTimer = new() { Interval = TimeSpan.FromMilliseconds(200) };

    public VideoSnapshotView()
    {
        InitializeComponent();

        _mediaPlayer = new MediaPlayer
        {
            ScrubbingEnabled = true
        };
        _mediaPlayer.MediaOpened += MediaPlayer_MediaOpened;
        _mediaPlayer.MediaEnded += MediaPlayer_MediaEnded;
        _mediaPlayer.MediaFailed += MediaPlayer_MediaFailed;

        _videoDrawing = new VideoDrawing
        {
            Player = _mediaPlayer,
            Rect = new Rect(0, 0, 1, 1)
        };
        _playerImage = new DrawingImage(_videoDrawing);
        PlayerDisplay.Source = _playerImage;

        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(33) // ~30fps 介面更新頻率
        };
        _timer.Tick += Timer_Tick;

        _seekTimer.Tick += async (_, _) =>
        {
            int generation = _seekGeneration;
            _seekTimer.Stop();
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
            if (generation == _seekGeneration && ViewModel?.HasVideoLoaded == true)
            {
                ViewModel.CurrentPosition = _mediaPlayer.Position;
                ViewModel.IsSeeking = false;
            }
        };
        DataContextChanged += VideoSnapshotView_DataContextChanged;
    }

    private VideoSnapshotViewModel? ViewModel => DataContext as VideoSnapshotViewModel;

    private void UserControl_Unloaded(object sender, RoutedEventArgs e)
    {
        _seekGeneration++;
        _timer.Stop();
        _seekTimer.Stop();
        _mediaPlayer.Close();
        if (ViewModel != null) { ViewModel.HasVideoLoaded = false; ViewModel.IsPlaying = false; }
    }

    private void OpenLastSnapshot_Click(object sender, RoutedEventArgs e)
    {
        var snapshot = ViewModel?.LastCapturedSnapshot;
        if (snapshot == null) return;
        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.UriSource = new Uri(snapshot.FilePath); bitmap.EndInit(); bitmap.Freeze();
            var image = new Image { Source = bitmap, Stretch = Stretch.None };
            var scroll = new ScrollViewer { Content = image, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            var window = new Window { Title = $"{ViewModel!.ComparisonSource}（原尺寸，可捲動）", Width = 900, Height = 700, Content = scroll, Owner = Window.GetWindow(this), WindowStartupLocation = WindowStartupLocation.CenterOwner };
            window.ShowDialog();
        }
        catch (Exception ex) { MessageBox.Show($"無法開啟截圖：{ex.Message}", "截圖預覽"); }
    }

    private void VideoSnapshotView_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is VideoSnapshotViewModel oldVm)
        {
            oldVm.RequestLoadVideo -= Vm_RequestLoadVideo;
            oldVm.RequestPlay -= Vm_RequestPlay;
            oldVm.RequestPause -= Vm_RequestPause;
            oldVm.RequestSeek -= Vm_RequestSeek;
            oldVm.RequestCurrentFrame -= Vm_RequestCurrentFrame;
            oldVm.RequestCurrentPosition -= Vm_RequestCurrentPosition;
            oldVm.RequestSnapshotVisualFeedback -= Vm_RequestSnapshotVisualFeedback;
        }

        if (e.NewValue is VideoSnapshotViewModel newVm)
        {
            newVm.RequestLoadVideo += Vm_RequestLoadVideo;
            newVm.RequestPlay += Vm_RequestPlay;
            newVm.RequestPause += Vm_RequestPause;
            newVm.RequestSeek += Vm_RequestSeek;
            newVm.RequestCurrentFrame += Vm_RequestCurrentFrame;
            newVm.RequestCurrentPosition += Vm_RequestCurrentPosition;
            newVm.RequestSnapshotVisualFeedback += Vm_RequestSnapshotVisualFeedback;

            if (!string.IsNullOrEmpty(newVm.VideoPath) && File.Exists(newVm.VideoPath))
            {
                Vm_RequestLoadVideo(newVm.VideoPath);
            }
        }
    }

    private void Vm_RequestLoadVideo(string path)
    {
        _seekGeneration++;
        _timer.Stop();
        _seekTimer.Stop();
        BtnPlayPause.Content = "播放";
        _mediaPlayer.Stop();
        _mediaPlayer.Open(new Uri(path));
        // 預先啟動媒體管線解碼首幀；MediaOpened 會立即暫停於第 0 秒並通知 ViewModel。
        _mediaPlayer.Play();
        _mediaPlayer.Pause();
    }

    private void Vm_RequestPlay()
    {
        _mediaPlayer.Play();
        _timer.Start();
        if (ViewModel != null) ViewModel.IsPlaying = true;
        BtnPlayPause.Content = "暫停";
    }

    private void Vm_RequestPause()
    {
        _mediaPlayer.Pause();
        _timer.Stop();
        if (ViewModel != null) ViewModel.IsPlaying = false;
        BtnPlayPause.Content = "播放";
    }

    private void Vm_RequestSeek(TimeSpan position)
    {
        _seekGeneration++;
        if (ViewModel != null) ViewModel.IsSeeking = true;
        _seekTimer.Stop();
        _mediaPlayer.Position = position;
        _seekTimer.Start();
        if (ViewModel != null)
        {
            ViewModel.CurrentPosition = position;
        }
    }

    private TimeSpan Vm_RequestCurrentPosition() => _mediaPlayer.Position;

    private BitmapSource? Vm_RequestCurrentFrame()
    {
        try
        {
            int naturalWidth = _mediaPlayer.NaturalVideoWidth;
            int naturalHeight = _mediaPlayer.NaturalVideoHeight;

            if (!_mediaPlayer.HasVideo || naturalWidth <= 0 || naturalHeight <= 0)
            {
                return null;
            }

            // 直接以視訊軌原始像素寬高繪製，完全不受預覽手機外框縮放或黑邊影響。
            var drawingVisual = new DrawingVisual();
            using (var dc = drawingVisual.RenderOpen())
            {
                dc.DrawVideo(_mediaPlayer, new Rect(0, 0, naturalWidth, naturalHeight));
            }

            var renderTarget = new RenderTargetBitmap(naturalWidth, naturalHeight, 96, 96, PixelFormats.Pbgra32);
            renderTarget.Render(drawingVisual);
            renderTarget.Freeze();
            return renderTarget;
        }
        catch
        {
            return null;
        }
    }

    private void Vm_RequestSnapshotVisualFeedback()
    {
        // 快門白色閃爍回饋動畫 (120ms 淡出)
        var anim = new DoubleAnimation
        {
            From = 0.75,
            To = 0.0,
            Duration = TimeSpan.FromMilliseconds(120),
            FillBehavior = FillBehavior.Stop
        };
        FlashOverlay.BeginAnimation(UIElement.OpacityProperty, anim);
    }

    private void Timer_Tick(object? sender, EventArgs e)
    {
        if (!_isUserDraggingSlider && ViewModel != null && _mediaPlayer.NaturalDuration.HasTimeSpan)
        {
            ViewModel.CurrentPosition = _mediaPlayer.Position;
        }
    }

    private void MediaPlayer_MediaOpened(object? sender, EventArgs e)
    {
        if (ViewModel == null) return;

        int naturalWidth = _mediaPlayer.NaturalVideoWidth;
        int naturalHeight = _mediaPlayer.NaturalVideoHeight;
        if (!_mediaPlayer.HasVideo || naturalWidth <= 0 || naturalHeight <= 0)
        {
            _mediaPlayer.Close();
            ViewModel.MarkVideoLoadFailed("檔案未包含可顯示的視訊軌");
            return;
        }

        _videoDrawing.Rect = new Rect(0, 0, naturalWidth, naturalHeight);
        _mediaPlayer.Position = TimeSpan.Zero;
        _mediaPlayer.Pause();

        TimeSpan duration = _mediaPlayer.NaturalDuration.HasTimeSpan
            ? _mediaPlayer.NaturalDuration.TimeSpan
            : TimeSpan.Zero;
        ViewModel.MarkVideoOpened(duration);
        ViewModel.IsSeeking = true;
        _seekTimer.Start();
    }

    private void MediaPlayer_MediaFailed(object? sender, ExceptionEventArgs e)
    {
        _seekGeneration++;
        _timer.Stop();
        _seekTimer.Stop();
        BtnPlayPause.Content = "播放";
        _mediaPlayer.Close();
        ViewModel?.MarkVideoLoadFailed(e.ErrorException?.Message ?? "系統缺少對應解碼器");
    }

    private void MediaPlayer_MediaEnded(object? sender, EventArgs e)
    {
        Vm_RequestPause();
        if (ViewModel != null)
        {
            ViewModel.CurrentPosition = ViewModel.Duration;
        }
    }

    private void Slider_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        _isUserDraggingSlider = true;
    }

    private void Slider_PreviewMouseUp(object sender, MouseButtonEventArgs e)
    {
        _isUserDraggingSlider = false;
    }

    private void UserControl_DragOver(object sender, DragEventArgs e)
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

    private void UserControl_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
            {
                string path = files[0];
                string ext = Path.GetExtension(path).ToLowerInvariant();
                if (ext is ".mov" or ".mp4" or ".m4v" or ".avi" or ".mkv" or ".webm")
                {
                    ViewModel?.LoadVideo(path);
                }
            }
        }
    }
}
