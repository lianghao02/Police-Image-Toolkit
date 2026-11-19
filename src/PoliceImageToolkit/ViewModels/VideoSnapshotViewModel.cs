using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using PoliceImageToolkit.Models;
using PoliceImageToolkit.Services;

namespace PoliceImageToolkit.ViewModels;

public class VideoSnapshotViewModel : ViewModelBase
{
    private readonly IVideoService _videoService;
    private readonly IOutputIndexService _outputIndexService;
    private readonly Action? _onPreferencesChanged;

    private string _videoPath = string.Empty;
    private string _videoTitle = "尚未載入影片";
    private TimeSpan _duration = TimeSpan.Zero;
    private TimeSpan _currentPosition = TimeSpan.Zero;
    private bool _isPlaying = false;
    private bool _hasVideoLoaded = false;
    private bool _isLoadingVideo = false;
    private string _statusMessage = "請拖入手機螢幕錄影，再選擇儲存位置與截圖設定。";

    // 快照設定
    private string _outputDirectory = string.Empty;
    private string _outputFormat = "PNG";
    private int _jpgQuality = 95;
    private bool _addTimestampOverlay = false;
    private bool _includeMilliseconds = true;
    private string _casePrefix = string.Empty;

    // 上頁末行對照圖
    private SnapshotResult? _lastCapturedSnapshot;
    private bool _isCapturing = false;
    private bool _isMutating;
    private bool _isSeeking;
    private long _videoSession;
    private string _indexMessage = string.Empty;
    private readonly Action<string> _deleteFile;

    public VideoSnapshotViewModel(
        IVideoService videoService,
        IOutputIndexService outputIndexService,
        UserPreferences? initialPreferences = null,
        Action? onPreferencesChanged = null,
        Action<string>? deleteFile = null)
    {
        _deleteFile = deleteFile ?? MoveToRecoveryFolder;
        _videoService = videoService;
        _outputIndexService = outputIndexService;
        _onPreferencesChanged = onPreferencesChanged;
        Snapshots.CollectionChanged += (_, _) => CommandManager.InvalidateRequerySuggested();

        if (initialPreferences != null)
        {
            _outputFormat = string.Equals(initialPreferences.VideoOutputFormat, "JPG", StringComparison.OrdinalIgnoreCase)
                ? "JPG"
                : "PNG";
            _jpgQuality = Math.Clamp(initialPreferences.VideoJpgQuality, 10, 100);
            _addTimestampOverlay = initialPreferences.VideoAddTimestampOverlay;
            _includeMilliseconds = initialPreferences.VideoIncludeMilliseconds;
        }

        OpenVideoCommand = new RelayCommand(_ => ExecuteOpenVideo(), _ => !_isMutating);
        PlayPauseCommand = new RelayCommand(_ => ExecutePlayPause(), _ => HasVideoLoaded);
        StepForwardCommand = new RelayCommand(_ => StepTime(0.1), _ => HasVideoLoaded);
        StepBackwardCommand = new RelayCommand(_ => StepTime(-0.1), _ => HasVideoLoaded);
        StepForward1sCommand = new RelayCommand(_ => StepTime(1.0), _ => HasVideoLoaded);
        StepBackward1sCommand = new RelayCommand(_ => StepTime(-1.0), _ => HasVideoLoaded);
        SeekCommand = new RelayCommand(ExecuteSeek, _ => HasVideoLoaded);
        RetryIndexCommand = new RelayCommand(async _ => await RetrySnapshotIndexAsync(), _ => (Snapshots.Count > 0 || _pendingRemovals.Any(IsCurrentSource) || _pendingIndexSnapshots.Any(IsCurrentSource)) && !IsBusy);
        BrowseOutputDirectoryCommand = new RelayCommand(_ => ExecuteBrowseOutputDirectory(), _ => !IsBusy);
        OpenOutputFolderCommand = new RelayCommand(_ => ExecuteOpenOutputFolder(), _ => !string.IsNullOrWhiteSpace(VideoPath));
        ClearSnapshotsCommand = new RelayCommand(_ => ExecuteClearSnapshots(), _ => Snapshots.Count > 0 && !IsBusy);
        DeleteSnapshotFilesCommand = new RelayCommand(async _ => await ExecuteDeleteSnapshotFilesAsync(), _ => Snapshots.Count > 0 && !IsBusy);
        RestoreLastRemovedCommand = new RelayCommand(async _ => await RestoreLastRemovedAsync(), _ => !IsBusy && FindRestorableIndex() >= 0);
        UndoLastCaptureCommand = new RelayCommand(async _ => await ExecuteUndoLastCaptureAsync(), _ => Snapshots.Count > 0 && !IsBusy);
        ClearPreviousComparisonCommand = new RelayCommand(_ =>
        {
            _previousSegmentSnapshot = null;
            RefreshComparison();
        }, _ => !IsBusy && _previousSegmentSnapshot != null);
        CaptureSnapshotCommand = new RelayCommand(async _ => await CaptureSnapshotAsync(), _ => HasVideoLoaded && !IsBusy && !IsSeeking);
    }

    public bool IsBusy => _isCapturing || _isMutating;
    public bool CanEditSettings => !IsBusy;
    public bool IsSeeking
    {
        get => _isSeeking;
        set { if (SetProperty(ref _isSeeking, value)) CommandManager.InvalidateRequerySuggested(); }
    }
    public string IndexMessage
    {
        get => _indexMessage;
        private set
        {
            SetProperty(ref _indexMessage, value);
            if (_segments.TryGetValue(VideoPath, out var state)) state.IndexMessage = value;
        }
    }
    public string EffectiveOutputDirectory => string.IsNullOrWhiteSpace(VideoPath)
        ? "開啟手機錄影後顯示本段儲存位置"
        : SnapshotOutputDirectory.Resolve(string.IsNullOrWhiteSpace(OutputDirectory)
            ? Path.GetDirectoryName(VideoPath)! : OutputDirectory, VideoPath);
    public bool IsPreviousSegmentComparison => LastCapturedSnapshot != null &&
        !string.Equals(LastCapturedSnapshot.SourceFilePath, VideoPath, StringComparison.OrdinalIgnoreCase);
    public string ComparisonTitle => IsPreviousSegmentComparison
        ? "上一段最後截圖 · 底部文字對照" : "本段上一張截圖 · 底部文字對照";
    public string ComparisonSource => LastCapturedSnapshot == null ? ""
        : $"{Path.GetFileName(LastCapturedSnapshot.SourceFilePath)} · {LastCapturedSnapshot.Timestamp:hh\\:mm\\:ss\\.fff}";
    public ICommand ClearPreviousComparisonCommand { get; }
    public ICommand RetryIndexCommand { get; }
    public ICommand RestoreLastRemovedCommand { get; }
    private readonly Dictionary<string, string> _recoveryFiles = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<(SnapshotResult Snapshot, string RecoveryPath)> _removedSnapshots = [];

    private void MoveToRecoveryFolder(string path)
    {
        string recoveryDirectory = Path.Combine(Path.GetDirectoryName(path)!, ".pit-recovery");
        Directory.CreateDirectory(recoveryDirectory);
        string recoveryPath = Path.Combine(recoveryDirectory, $"{Guid.NewGuid():N}_{Path.GetFileName(path)}");
        File.Move(path, recoveryPath, overwrite: false);
        _recoveryFiles[path] = recoveryPath;
    }

    public async Task RestoreLastRemovedAsync()
    {
        int restorableIndex = FindRestorableIndex();
        if (IsBusy || restorableIndex < 0) return;
        var removed = _removedSnapshots[restorableIndex];
        var snapshot = removed.Snapshot;
        string recoveryPath = removed.RecoveryPath;
        _isMutating = true; NotifyBusy();
        try
        {
            // 若流水號已被新截圖使用，拒絕還原並保留回收檔，絕不覆寫。
            File.Move(recoveryPath, snapshot.FilePath, overwrite: false);
            _removedSnapshots.Remove(removed);
            _pendingRemovals.RemoveAll(x => x.FilePath == snapshot.FilePath);
            Snapshots.Insert(0, snapshot);
            RefreshComparison();
            try
            {
                await _outputIndexService.AppendEntriesAsync(Path.GetDirectoryName(snapshot.FilePath)!,
                    [new OutputIndexEntry(Path.GetFileName(snapshot.FilePath), Path.GetFileName(snapshot.SourceFilePath ?? "unknown"),
                    "video_snapshot", snapshot.Timestamp.ToString("hh\\:mm\\:ss\\.fff"), snapshot.Width, snapshot.Height)]);
                IndexMessage = SuccessfulIndexMessage(VideoPath);
            }
            catch { _pendingIndexSnapshots.Add(snapshot); IndexMessage = "圖片已還原；來源紀錄更新失敗，請重試來源紀錄。"; }
            StatusMessage = $"已還原 {Path.GetFileName(snapshot.FilePath)}";
        }
        catch (Exception ex) { StatusMessage = $"無法還原，回收檔仍保留（請確認原位置沒有同名檔）：{ex.Message}"; }
        finally { _isMutating = false; NotifyBusy(); }
    }
    private void NotifyBusy()
    {
        OnPropertyChanged(nameof(IsBusy));
        OnPropertyChanged(nameof(CanEditSettings));
        CommandManager.InvalidateRequerySuggested();
    }
    private SnapshotResult? PreviousForCurrentVideo() => Snapshots.FirstOrDefault(x =>
        string.Equals(x.SourceFilePath, VideoPath, StringComparison.OrdinalIgnoreCase));

    private sealed class SegmentState
    {
        public ObservableCollection<SnapshotResult> Snapshots { get; } = [];
        public string Prefix { get; set; } = string.Empty;
        public string IndexMessage { get; set; } = string.Empty;
        public string OutputRoot { get; set; } = string.Empty;
    }
    private readonly Dictionary<string, SegmentState> _segments = new(StringComparer.OrdinalIgnoreCase);
    private ObservableCollection<SnapshotResult> _snapshots = [];
    private SnapshotResult? _previousSegmentSnapshot;
    public ObservableCollection<SnapshotResult> Snapshots
    {
        get => _snapshots;
        private set => SetProperty(ref _snapshots, value);
    }
    private bool IsCurrentSource(SnapshotResult snapshot) =>
        string.Equals(snapshot.SourceFilePath ?? string.Empty, VideoPath, StringComparison.OrdinalIgnoreCase);
    private int FindRestorableIndex() => _removedSnapshots.FindLastIndex(x => IsCurrentSource(x.Snapshot));
    private void RefreshComparison() => LastCapturedSnapshot = PreviousForCurrentVideo() ?? _previousSegmentSnapshot;

    public event Action<string>? RequestLoadVideo;
    public event Action? RequestPlay;
    public event Action? RequestPause;
    public event Action<TimeSpan>? RequestSeek;
    public event Func<BitmapSource?>? RequestCurrentFrame;
    public event Func<TimeSpan>? RequestCurrentPosition;
    public event Action? RequestSnapshotVisualFeedback; // 截圖視覺閃爍回饋

    public string VideoPath
    {
        get => _videoPath;
        set { if (SetProperty(ref _videoPath, value)) OnPropertyChanged(nameof(EffectiveOutputDirectory)); }
    }

    public string VideoTitle
    {
        get => _videoTitle;
        set => SetProperty(ref _videoTitle, value);
    }

    public TimeSpan Duration
    {
        get => _duration;
        set
        {
            if (SetProperty(ref _duration, value))
            {
                OnPropertyChanged(nameof(DurationSeconds));
                OnPropertyChanged(nameof(FormattedDuration));
            }
        }
    }

    public TimeSpan CurrentPosition
    {
        get => _currentPosition;
        set
        {
            if (SetProperty(ref _currentPosition, value))
            {
                OnPropertyChanged(nameof(CurrentPositionSeconds));
                OnPropertyChanged(nameof(FormattedPosition));
            }
        }
    }

    public double DurationSeconds => Duration.TotalSeconds;
    public double CurrentPositionSeconds
    {
        get => CurrentPosition.TotalSeconds;
        set
        {
            if (HasVideoLoaded && Math.Abs(CurrentPosition.TotalSeconds - value) > 0.03)
            {
                var ts = TimeSpan.FromSeconds(Math.Clamp(value, 0, DurationSeconds));
                RequestSeek?.Invoke(ts);
            }
        }
    }

    public string FormattedPosition => $"{(int)CurrentPosition.TotalHours:00}:{CurrentPosition.Minutes:00}:{CurrentPosition.Seconds:00}.{CurrentPosition.Milliseconds:000}";
    public string FormattedDuration => $"{(int)Duration.TotalHours:00}:{Duration.Minutes:00}:{Duration.Seconds:00}";

    public bool IsPlaying
    {
        get => _isPlaying;
        set => SetProperty(ref _isPlaying, value);
    }

    public bool HasVideoLoaded
    {
        get => _hasVideoLoaded;
        set
        {
            if (SetProperty(ref _hasVideoLoaded, value))
            {
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    public bool IsLoadingVideo
    {
        get => _isLoadingVideo;
        set => SetProperty(ref _isLoadingVideo, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    public string OutputDirectory
    {
        get => _outputDirectory;
        set { if (SetProperty(ref _outputDirectory, value)) OnPropertyChanged(nameof(EffectiveOutputDirectory)); }
    }

    public string OutputFormat
    {
        get => _outputFormat;
        set
        {
            if (SetProperty(ref _outputFormat, value))
            {
                _onPreferencesChanged?.Invoke();
            }
        }
    }

    public int JpgQuality
    {
        get => _jpgQuality;
        set
        {
            if (SetProperty(ref _jpgQuality, value))
            {
                _onPreferencesChanged?.Invoke();
            }
        }
    }

    public bool AddTimestampOverlay
    {
        get => _addTimestampOverlay;
        set
        {
            if (SetProperty(ref _addTimestampOverlay, value))
            {
                _onPreferencesChanged?.Invoke();
            }
        }
    }

    public bool IncludeMilliseconds
    {
        get => _includeMilliseconds;
        set
        {
            if (SetProperty(ref _includeMilliseconds, value))
            {
                _onPreferencesChanged?.Invoke();
            }
        }
    }

    public string CasePrefix
    {
        get => _casePrefix;
        set => SetProperty(ref _casePrefix, value);
    }

    public SnapshotResult? LastCapturedSnapshot
    {
        get => _lastCapturedSnapshot;
        set
        {
            if (SetProperty(ref _lastCapturedSnapshot, value))
            {
                OnPropertyChanged(nameof(HasLastCapturedSnapshot));
                OnPropertyChanged(nameof(IsPreviousSegmentComparison));
                OnPropertyChanged(nameof(ComparisonTitle));
                OnPropertyChanged(nameof(ComparisonSource));
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    public bool HasLastCapturedSnapshot => LastCapturedSnapshot != null;

    public ICommand OpenVideoCommand { get; }
    public ICommand PlayPauseCommand { get; }
    public ICommand StepForwardCommand { get; }
    public ICommand StepBackwardCommand { get; }
    public ICommand StepForward1sCommand { get; }
    public ICommand StepBackward1sCommand { get; }
    public ICommand SeekCommand { get; }
    public ICommand BrowseOutputDirectoryCommand { get; }
    public ICommand OpenOutputFolderCommand { get; }
    public ICommand ClearSnapshotsCommand { get; }
    public ICommand DeleteSnapshotFilesCommand { get; }
    public ICommand UndoLastCaptureCommand { get; }
    public ICommand CaptureSnapshotCommand { get; }

    public void LoadVideo(string path)
    {
        if (!File.Exists(path)) return;
        if (_isMutating) { StatusMessage = "正在更新目前影片的截圖，完成後即可換片。"; return; }
        path = Path.GetFullPath(path);
        bool differentSource = !string.Equals(VideoPath, path, StringComparison.OrdinalIgnoreCase);
        var previousLast = differentSource ? PreviousForCurrentVideo() : _previousSegmentSnapshot;
        if (_segments.TryGetValue(VideoPath, out var oldState))
        {
            oldState.Prefix = CasePrefix;
            oldState.OutputRoot = OutputDirectory;
        }
        if (!_segments.TryGetValue(path, out var nextState))
        {
            nextState = new SegmentState { OutputRoot = OutputDirectory };
            nextState.Snapshots.CollectionChanged += (_, _) => CommandManager.InvalidateRequerySuggested();
            _segments[path] = nextState;
        }
        _videoSession++;
        IsSeeking = false;

        if (IsPlaying) RequestPause?.Invoke();
        IsPlaying = false;
        // 播放器非同步開檔完成並觸發 MediaOpened 前，先鎖定截圖與播放操作，避免截到空白黑畫面。
        HasVideoLoaded = false;
        IsLoadingVideo = true;
        VideoPath = path;
        VideoTitle = Path.GetFileName(path);
        Duration = TimeSpan.Zero;
        CurrentPosition = TimeSpan.Zero;
        // 每段保留自己的工作清單；上一段對照單獨標示，不能成為本段移除的目標。
        Snapshots = nextState.Snapshots;
        CasePrefix = nextState.Prefix;
        OutputDirectory = nextState.OutputRoot;
        _previousSegmentSnapshot = previousLast;
        RefreshComparison();
        OnPropertyChanged(nameof(ComparisonTitle));
        OnPropertyChanged(nameof(IsPreviousSegmentComparison));
        OnPropertyChanged(nameof(ComparisonSource));
        IndexMessage = nextState.IndexMessage;
        StatusMessage = $"正在載入影片：{VideoTitle}...";

        // 由 View 實際指定播放器來源；按鈕開檔與拖曳開檔共用同一路徑。
        RequestLoadVideo?.Invoke(path);
    }

    public void MarkVideoOpened(TimeSpan duration)
    {
        IsLoadingVideo = false;
        Duration = duration;
        CurrentPosition = TimeSpan.Zero;
        HasVideoLoaded = true;
        StatusMessage = $"已載入影片：{VideoTitle} （Space 儲存畫面，←／→ 移動 0.1 秒）";
    }

    public void MarkVideoLoadFailed(string reason)
    {
        IsLoadingVideo = false;
        HasVideoLoaded = false;
        IsPlaying = false;
        Duration = TimeSpan.Zero;
        CurrentPosition = TimeSpan.Zero;
        StatusMessage = $"無法載入手機錄影 {VideoTitle}：{reason}（可先以其他播放器確認格式，或轉為 MP4 H.264 後再載入）";
    }

    private void ExecuteOpenVideo()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "影片 (*.mov;*.mp4;*.m4v;*.avi;*.mkv;*.webm)|*.mov;*.mp4;*.m4v;*.avi;*.mkv;*.webm|所有檔案 (*.*)|*.*",
            Title = "選擇手機螢幕錄影（支援格式依系統解碼器而定）"
        };

        if (dialog.ShowDialog() == true)
        {
            LoadVideo(dialog.FileName);
        }
    }

    public void ExecutePlayPause()
    {
        if (!HasVideoLoaded) return;

        if (IsPlaying)
        {
            RequestPause?.Invoke();
            IsPlaying = false;
        }
        else
        {
            // 已播放至片尾時從頭播放，否則按下播放不會有任何反應。
            if (Duration > TimeSpan.Zero && CurrentPosition >= Duration - TimeSpan.FromMilliseconds(100))
            {
                RequestSeek?.Invoke(TimeSpan.Zero);
            }

            RequestPlay?.Invoke();
            IsPlaying = true;
        }
    }

    public void StepTime(double deltaSeconds)
    {
        if (!HasVideoLoaded) return;

        if (IsPlaying)
        {
            ExecutePlayPause();
        }

        var next = CurrentPosition + TimeSpan.FromSeconds(deltaSeconds);
        if (next < TimeSpan.Zero) next = TimeSpan.Zero;
        if (next > Duration) next = Duration;

        RequestSeek?.Invoke(next);
    }

    private void ExecuteSeek(object? param)
    {
        if (param is double seconds)
        {
            RequestSeek?.Invoke(TimeSpan.FromSeconds(seconds));
        }
    }

    public async Task<SnapshotResult?> CaptureSnapshotAsync()
    {
        if (!HasVideoLoaded || RequestCurrentFrame == null || IsBusy || IsSeeking) return null;

        _isCapturing = true;
        NotifyBusy();
        string capturedVideoPath = VideoPath;
        long capturedSession = _videoSession;
        var capturedSnapshots = Snapshots;
        IndexMessage = string.Empty;
        try
        {
            if (IsPlaying) { RequestPause?.Invoke(); IsPlaying = false; }
            TimeSpan capturePos = RequestCurrentPosition?.Invoke() ?? CurrentPosition;
            StatusMessage = "正在儲存截圖…";
            // 1. 擷取暫停後的畫面；時間為播放器回報位置，並非影格 PTS。
            var frame = RequestCurrentFrame.Invoke();
            if (frame == null)
            {
                StatusMessage = "截圖未儲存：尚無可用畫面，請等待影片載入或移動播放位置後重試。";
                return null;
            }


            var config = new VideoSnapshotConfig
            {
                OutputDirectory = OutputDirectory,
                OutputFormat = OutputFormat,
                JpgQuality = JpgQuality,
                AddTimestampOverlay = AddTimestampOverlay,
                IncludeMilliseconds = IncludeMilliseconds,
                // 每部影片固定輸出至自己的資料夾，流水號才可從 001 穩定排序。
                AutoCreateSubfolder = true,
                CasePrefix = CasePrefix
            };


            // 3. 背景非同步存檔，不阻礙後續移動與播放
            var res = await _videoService.SaveFrameSnapshotAsync(frame, capturePos, capturedVideoPath, config);
            res = res with { SourceFilePath = capturedVideoPath };
            string indexStatus;
            try
            {
                await _outputIndexService.AppendEntriesAsync(Path.GetDirectoryName(res.FilePath)!,
                [new OutputIndexEntry(
                    Path.GetFileName(res.FilePath),
                    Path.GetFileName(capturedVideoPath),
                    "video_snapshot",
                    res.Timestamp.ToString("hh\\:mm\\:ss\\.fff"),
                    res.Width,
                    res.Height)]);
                indexStatus = SuccessfulIndexMessage(capturedVideoPath);
            }
            catch
            {
                _pendingIndexSnapshots.Add(res);
                indexStatus = "來源紀錄更新失敗；截圖已保留，請按「重試來源紀錄」。";
            }

            // 4. 更新 UI 與「上頁末行對照圖」
            RunOnUIThread(() =>
            {
                capturedSnapshots.Insert(0, res);
                if (_segments.TryGetValue(capturedVideoPath, out var state)) state.IndexMessage = indexStatus;
                if (capturedSession == _videoSession)
                {
                    LastCapturedSnapshot = res;
                    IndexMessage = string.IsNullOrEmpty(indexStatus) ? SuccessfulIndexMessage(VideoPath) : indexStatus;
                    StatusMessage = $"已儲存 {Path.GetFileName(res.FilePath)}（{res.Width} × {res.Height}）";
                    RequestSnapshotVisualFeedback?.Invoke();
                }

            });

            return res;
        }
        catch (Exception ex)
        {
            if (capturedSession == _videoSession) StatusMessage = $"截圖未儲存：{ex.Message}。請檢查輸出位置後重試。";
            return null;
        }
        finally
        {
            _isCapturing = false;
            NotifyBusy();
        }
    }

    public async Task ExecuteUndoLastCaptureAsync()
    {
        if (IsBusy || Snapshots.Count == 0) return;
        _isMutating = true;
        NotifyBusy();
        var last = Snapshots[0];
        try
        {
            if (File.Exists(last.FilePath)) _deleteFile(last.FilePath);
            if (File.Exists(last.FilePath)) throw new IOException("檔案仍存在，未移除清單項目。");
            if (_recoveryFiles.Remove(last.FilePath, out string? recoveryPath)) _removedSnapshots.Add((last, recoveryPath));
            _pendingIndexSnapshots.RemoveAll(x => x.FilePath == last.FilePath);
            Snapshots.Remove(last);
            RefreshComparison();
            try
            {
                await _outputIndexService.RemoveEntriesAsync(Path.GetDirectoryName(last.FilePath)!, [Path.GetFileName(last.FilePath)]);
                IndexMessage = SuccessfulIndexMessage(VideoPath);
            }
            catch { IndexMessage = "刪除紀錄尚未同步，請按「重試來源紀錄」。"; _pendingRemovals.Add(last); }
            StatusMessage = $"已移至回收區：{Path.GetFileName(last.FilePath)}。按「還原本段最近移除」可復原；回收檔保留在輸出資料夾的 .pit-recovery 子資料夾。";
        }
        catch (Exception ex) { StatusMessage = $"無法移至回收區，清單已保留：{ex.Message}"; }
        finally { _isMutating = false; NotifyBusy(); }
    }

    private string SuccessfulIndexMessage(string sourcePath) =>
        _pendingIndexSnapshots.Concat(_pendingRemovals).Any(x =>
            string.Equals(x.SourceFilePath ?? string.Empty, sourcePath, StringComparison.OrdinalIgnoreCase))
        ? "本次來源紀錄已更新；本段仍有紀錄未同步，請重試來源紀錄。"
        : "來源紀錄已更新";

    private readonly List<SnapshotResult> _pendingRemovals = [];
    private readonly List<SnapshotResult> _pendingIndexSnapshots = [];
    public async Task RetrySnapshotIndexAsync()
    {
        if (IsBusy) return;
        _isMutating = true;
        NotifyBusy();
        try
        {
            foreach (var removed in _pendingRemovals.Where(IsCurrentSource).ToArray())
            {
                await _outputIndexService.RemoveEntriesAsync(Path.GetDirectoryName(removed.FilePath)!, [Path.GetFileName(removed.FilePath)]);
                _pendingRemovals.Remove(removed);
            }
            foreach (var group in Snapshots.Concat(_pendingIndexSnapshots.Where(IsCurrentSource)).DistinctBy(x => x.FilePath).Where(x => !string.IsNullOrEmpty(x.SourceFilePath) && File.Exists(x.FilePath))
                .GroupBy(x => Path.GetDirectoryName(x.FilePath)!))
                await _outputIndexService.AppendEntriesAsync(group.Key, group.Select(x => new OutputIndexEntry(
                    Path.GetFileName(x.FilePath), Path.GetFileName(x.SourceFilePath!), "video_snapshot",
                    x.Timestamp.ToString("hh\\:mm\\:ss\\.fff"), x.Width, x.Height)));
            _pendingIndexSnapshots.RemoveAll(x => IsCurrentSource(x));
            IndexMessage = SuccessfulIndexMessage(VideoPath);
        }
        catch (Exception ex) { IndexMessage = $"來源紀錄更新失敗：{ex.Message}"; }
        finally { _isMutating = false; NotifyBusy(); }
    }

    private void ExecuteClearSnapshots()
    {
        if (IsBusy) return;
        int count = Snapshots.Count;
        Snapshots.Clear();
        RefreshComparison();
        StatusMessage = $"已清空本段截圖清單（保留磁碟上的 {count} 個檔案與其他影片清單）。";
    }

    private async Task ExecuteDeleteSnapshotFilesAsync()
    {
        if (IsBusy) return;
        int requestedCount = Snapshots.Count;
        var choice = System.Windows.MessageBox.Show(
            $"確定將目前影片「{VideoTitle}」的 {requestedCount} 個截圖移至回收區？\n\n" +
            "按「還原本段最近移除」可復原；回收檔保留在輸出資料夾的 .pit-recovery 子資料夾。來源影片會保留。",
            "將目前影片的截圖移至回收區",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Warning);

        if (choice != System.Windows.MessageBoxResult.Yes) return;

        await MoveCurrentSnapshotsToRecoveryAsync();
    }

    public async Task MoveCurrentSnapshotsToRecoveryAsync()
    {
        if (IsBusy || Snapshots.Count == 0) return;
        _isMutating = true;
        NotifyBusy();
        try
        {
        int deletedCount = 0;
        int missingCount = 0;
        int failedCount = 0;
        var removable = new List<SnapshotResult>();

        foreach (var snapshot in Snapshots)
        {
            try
            {
                if (File.Exists(snapshot.FilePath))
                {
                    _deleteFile(snapshot.FilePath);
                    if (File.Exists(snapshot.FilePath)) throw new IOException("檔案仍存在");
                    deletedCount++;
                }
                else
                {
                    missingCount++;
                }

                removable.Add(snapshot);
            }
            catch
            {
                failedCount++;
            }
        }

        foreach (var snapshot in removable)
        {
            if (_recoveryFiles.Remove(snapshot.FilePath, out string? recoveryPath)) _removedSnapshots.Add((snapshot, recoveryPath));
            _pendingIndexSnapshots.RemoveAll(x => x.FilePath == snapshot.FilePath);
            Snapshots.Remove(snapshot);
        }
        RefreshComparison();

        string indexStatus = string.Empty;
        try
        {
            foreach (var group in removable.GroupBy(snapshot => Path.GetDirectoryName(snapshot.FilePath)!))
            {
                await _outputIndexService.RemoveEntriesAsync(group.Key, group.Select(snapshot => Path.GetFileName(snapshot.FilePath)));
            }
        }
        catch
        {
            indexStatus = "；刪除紀錄尚未同步，請重試來源紀錄";
            _pendingRemovals.AddRange(removable);
        }

        IndexMessage = string.IsNullOrEmpty(indexStatus) ? SuccessfulIndexMessage(VideoPath) : indexStatus;
        StatusMessage = failedCount == 0
            ? $"已移至回收區 {deletedCount} 個截圖；另有 {missingCount} 個檔案原本不存在。{indexStatus}"
            : $"已移至回收區 {deletedCount} 個截圖；{failedCount} 個檔案無法刪除並保留在清單中。{indexStatus}";
    }

        finally { _isMutating = false; NotifyBusy(); }
    }

    private void ExecuteBrowseOutputDirectory()
    {
        var dialog = new OpenFolderDialog
        {
            Title = "選擇截圖儲存資料夾"
        };

        if (dialog.ShowDialog() == true)
        {
            OutputDirectory = dialog.FolderName;
        }
    }

    private void ExecuteOpenOutputFolder()
    {
        if (string.IsNullOrWhiteSpace(VideoPath)) return;
        string dir = EffectiveOutputDirectory;

        try
        {
            Directory.CreateDirectory(dir);
            Process.Start(new ProcessStartInfo
            {
                FileName = dir,
                UseShellExecute = true
            });
        }
        catch (Exception ex) { StatusMessage = $"無法開啟輸出資料夾：{ex.Message}"; }
    }
}
