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
    // 介面寬度達此值時，右側改為「對照圖｜設定＋清單」雙欄，讓直式對照圖與清單都保有足夠高度。
    private const double WideWorkspaceMinWidth = 1100;
    // 窄版上下堆疊時右側的最低總高度；視窗較矮時改由外層 ScrollViewer 捲動，避免清單被擠到看不見。
    private const double StackedWorkspaceMinHeight = 700;
    private readonly DispatcherTimer _timer;
    private bool _isUserDraggingSlider = false;
    private bool? _isWideWorkspace;

    public VideoSnapshotView()
    {
        InitializeComponent();

        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(33) // ~30fps 介面更新頻率
        };
        _timer.Tick += Timer_Tick;

        DataContextChanged += VideoSnapshotView_DataContextChanged;
    }

    private VideoSnapshotViewModel? ViewModel => DataContext as VideoSnapshotViewModel;

    private void UserControl_Loaded(object sender, RoutedEventArgs e)
    {
        UpdateRightWorkspaceLayout();
    }

    private void UserControl_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateRightWorkspaceLayout();
    }

    private void UpdateRightWorkspaceLayout()
    {
        bool useWideWorkspace = ActualWidth >= WideWorkspaceMinWidth;
        if (_isWideWorkspace == useWideWorkspace)
        {
            SyncRightWorkspaceHeight();
            return;
        }

        _isWideWorkspace = useWideWorkspace;

        if (useWideWorkspace)
        {
            RightWorkspace.ColumnDefinitions[0].Width = new GridLength(1, GridUnitType.Star);
            RightWorkspace.ColumnDefinitions[1].Width = new GridLength(1, GridUnitType.Star);
            RightWorkspace.RowDefinitions[0].Height = GridLength.Auto;
            RightWorkspace.RowDefinitions[1].Height = new GridLength(1, GridUnitType.Star);
            RightWorkspace.RowDefinitions[2].Height = new GridLength(0);

            Grid.SetColumn(CompareCard, 0);
            Grid.SetColumnSpan(CompareCard, 1);
            Grid.SetRow(CompareCard, 0);
            Grid.SetRowSpan(CompareCard, 3);
            CompareCard.Margin = new Thickness(0, 0, 8, 0);
            ComparePreview.MinHeight = 300;
            ComparePreview.Height = double.NaN;

            Grid.SetColumn(SettingsCard, 1);
            Grid.SetColumnSpan(SettingsCard, 1);
            Grid.SetRow(SettingsCard, 0);
            Grid.SetRowSpan(SettingsCard, 1);

            Grid.SetColumn(EvidenceCard, 1);
            Grid.SetColumnSpan(EvidenceCard, 1);
            Grid.SetRow(EvidenceCard, 1);
            Grid.SetRowSpan(EvidenceCard, 2);
        }
        else
        {
            RightWorkspace.ColumnDefinitions[0].Width = new GridLength(1, GridUnitType.Star);
            RightWorkspace.ColumnDefinitions[1].Width = new GridLength(0);
            RightWorkspace.RowDefinitions[0].Height = GridLength.Auto;
            RightWorkspace.RowDefinitions[1].Height = GridLength.Auto;
            RightWorkspace.RowDefinitions[2].Height = new GridLength(1, GridUnitType.Star);

            Grid.SetColumn(CompareCard, 0);
            Grid.SetColumnSpan(CompareCard, 2);
            Grid.SetRow(CompareCard, 0);
            Grid.SetRowSpan(CompareCard, 1);
            CompareCard.Margin = new Thickness(0, 0, 0, 10);
            ComparePreview.MinHeight = 0;
            ComparePreview.Height = 170;

            Grid.SetColumn(SettingsCard, 0);
            Grid.SetColumnSpan(SettingsCard, 2);
            Grid.SetRow(SettingsCard, 1);
            Grid.SetRowSpan(SettingsCard, 1);

            Grid.SetColumn(EvidenceCard, 0);
            Grid.SetColumnSpan(EvidenceCard, 2);
            Grid.SetRow(EvidenceCard, 2);
            Grid.SetRowSpan(EvidenceCard, 1);
        }

        SyncRightWorkspaceHeight();
    }

    private void RightScroll_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        SyncRightWorkspaceHeight();
    }

    private void SyncRightWorkspaceHeight()
    {
        // ScrollViewer 會以無限高度量測內容，需明確指定高度，星號列才能依可視高度分配。
        double viewportHeight = RightScroll.ActualHeight;
        if (viewportHeight <= 0) return;

        double minimumHeight = _isWideWorkspace == true ? 0 : StackedWorkspaceMinHeight;
        RightWorkspace.Height = Math.Max(viewportHeight, minimumHeight);
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
            oldVm.RequestSnapshotVisualFeedback -= Vm_RequestSnapshotVisualFeedback;
        }

        if (e.NewValue is VideoSnapshotViewModel newVm)
        {
            newVm.RequestLoadVideo += Vm_RequestLoadVideo;
            newVm.RequestPlay += Vm_RequestPlay;
            newVm.RequestPause += Vm_RequestPause;
            newVm.RequestSeek += Vm_RequestSeek;
            newVm.RequestCurrentFrame += Vm_RequestCurrentFrame;
            newVm.RequestSnapshotVisualFeedback += Vm_RequestSnapshotVisualFeedback;

            if (!string.IsNullOrEmpty(newVm.VideoPath) && File.Exists(newVm.VideoPath))
            {
                Player.Source = new Uri(newVm.VideoPath);
            }
        }
    }

    private void Vm_RequestLoadVideo(string path)
    {
        _timer.Stop();
        BtnPlayPause.Content = "▶ 播放";
        Player.Stop();
        Player.Source = new Uri(path);
        // MediaElement 為 Manual 模式，需 Play 後才會開啟媒體；MediaOpened 會再暫停於第一格。
        Player.Play();
        Player.Pause();
    }

    private void Vm_RequestPlay()
    {
        Player.Play();
        _timer.Start();
        if (ViewModel != null) ViewModel.IsPlaying = true;
        BtnPlayPause.Content = "⏸ 暫停";
    }

    private void Vm_RequestPause()
    {
        Player.Pause();
        _timer.Stop();
        if (ViewModel != null) ViewModel.IsPlaying = false;
        BtnPlayPause.Content = "▶ 播放";
    }

    private void Vm_RequestSeek(TimeSpan position)
    {
        Player.Position = position;
        if (ViewModel != null)
        {
            ViewModel.CurrentPosition = position;
        }
    }

    private BitmapSource? Vm_RequestCurrentFrame()
    {
        try
        {
            int naturalWidth = Player.NaturalVideoWidth;
            int naturalHeight = Player.NaturalVideoHeight;

            if (naturalWidth <= 0 || naturalHeight <= 0)
            {
                naturalWidth = (int)Player.ActualWidth;
                naturalHeight = (int)Player.ActualHeight;
            }

            if (naturalWidth <= 0 || naturalHeight <= 0)
            {
                naturalWidth = 1080;
                naturalHeight = 1920;
            }

            // 捕捉當前 MediaElement 渲染之視覺內容
            var renderTarget = new RenderTargetBitmap(naturalWidth, naturalHeight, 96, 96, PixelFormats.Pbgra32);
            var visualBrush = new VisualBrush(Player) { Stretch = Stretch.Uniform };

            var drawingVisual = new DrawingVisual();
            using (var dc = drawingVisual.RenderOpen())
            {
                dc.DrawRectangle(visualBrush, null, new Rect(0, 0, naturalWidth, naturalHeight));
            }

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
        if (!_isUserDraggingSlider && ViewModel != null && Player.NaturalDuration.HasTimeSpan)
        {
            ViewModel.CurrentPosition = Player.Position;
        }
    }

    private void Player_MediaOpened(object sender, RoutedEventArgs e)
    {
        if (ViewModel != null && Player.NaturalDuration.HasTimeSpan)
        {
            ViewModel.Duration = Player.NaturalDuration.TimeSpan;
            ViewModel.CurrentPosition = TimeSpan.Zero;
            Player.Position = TimeSpan.Zero;
            Player.Pause(); // 預設載入後暫停於第一格
        }
    }

    private void Player_MediaFailed(object? sender, ExceptionRoutedEventArgs e)
    {
        _timer.Stop();
        BtnPlayPause.Content = "▶ 播放";
        Player.Source = null;
        ViewModel?.MarkVideoLoadFailed(e.ErrorException?.Message ?? "系統缺少對應解碼器");
    }

    private void Player_MediaEnded(object sender, RoutedEventArgs e)
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
        if (ViewModel != null)
        {
            Player.Position = ViewModel.CurrentPosition;
        }
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
