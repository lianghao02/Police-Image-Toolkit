using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows.Input;
using Microsoft.Win32;
using PoliceImageToolkit.Models;
using PoliceImageToolkit.Services;
using TaskStatus = PoliceImageToolkit.Models.TaskStatus;

namespace PoliceImageToolkit.ViewModels;

public sealed record MaxDimensionOption(string Label, int Value);

public class ImageConverterViewModel : ViewModelBase
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".heic", ".heif", ".webp", ".bmp", ".tif", ".tiff", ".gif"
    };

    private readonly IImageService _imageService;
    private readonly IOutputIndexService _outputIndexService;
    private readonly Action? _onPreferencesChanged;
    private CancellationTokenSource? _cts;
    private readonly Dictionary<string, List<OutputIndexEntry>> _pendingIndexEntries = new(StringComparer.OrdinalIgnoreCase);

    private string _targetFormat = "JPG";
    private int _jpgQuality = 90;
    private bool _autoOrient = true;
    private int _maxDimension = 0;
    private string _outputDirectory = string.Empty;
    private bool _useCustomOutputDirectory = false;
    private bool _isProcessing = false;
    private double _overallProgress = 0;
    private string _indexMessage = string.Empty;
    private string _summaryText = "請拖曳圖片或資料夾，或點擊「加入圖片」開始";

    public ImageConverterViewModel(
        IImageService imageService,
        IOutputIndexService outputIndexService,
        UserPreferences? initialPreferences = null,
        Action? onPreferencesChanged = null)
    {
        _imageService = imageService;
        _outputIndexService = outputIndexService;
        _onPreferencesChanged = onPreferencesChanged;
        Tasks = new ObservableCollection<ImageTaskItem>();

        if (initialPreferences != null)
        {
            if (Array.Exists(AvailableFormats, f => string.Equals(f, initialPreferences.ImageTargetFormat, StringComparison.OrdinalIgnoreCase)))
            {
                _targetFormat = initialPreferences.ImageTargetFormat.ToUpperInvariant();
            }
            _jpgQuality = Math.Clamp(initialPreferences.ImageJpgQuality, 10, 100);
            _autoOrient = initialPreferences.ImageAutoOrient;
            _maxDimension = MaxDimensionOptions.Any(o => o.Value == initialPreferences.ImageMaxDimension)
                ? initialPreferences.ImageMaxDimension
                : 0;
        }

        AddFilesCommand = new RelayCommand(_ => ExecuteAddFiles(), _ => !IsProcessing);
        RemoveSelectedCommand = new RelayCommand(ExecuteRemoveSelected, _ => !IsProcessing);
        ClearAllCommand = new RelayCommand(_ => ExecuteClearAll(), _ => !IsProcessing && Tasks.Count > 0);
        ResetStatusCommand = new RelayCommand(_ => ResetTasksForReconvert(), _ => !IsProcessing && Tasks.Any(t => t.Status != TaskStatus.Pending));
        StartConvertCommand = new RelayCommand(async _ => await StartConvertAsync(), _ => !IsProcessing && Tasks.Any(t => t.Status != TaskStatus.Success));
        CancelConvertCommand = new RelayCommand(_ => ExecuteCancelConvert(), _ => IsProcessing);
        ReconvertCommand = new RelayCommand(async _ =>
        {
            var result = System.Windows.MessageBox.Show("依目前設定重新轉換清單中的全部圖片？\n\n先前輸出與原始檔案都會保留，另存為新檔。", "重新轉檔", System.Windows.MessageBoxButton.YesNo);
            if (result != System.Windows.MessageBoxResult.Yes) return;
            ResetTasksForReconvert();
            await StartConvertAsync();
        }, _ => !IsProcessing && Tasks.Any(t => t.Status == TaskStatus.Success));
        RetryIndexCommand = new RelayCommand(async _ =>
        {
            if (IsProcessing) return;
            IsProcessing = true;
            try { IndexMessage = await UpdateOutputIndexesAsync(Tasks); }
            finally { IsProcessing = false; }
        }, _ => !IsProcessing && (Tasks.Any(t => t.Status == TaskStatus.Success) || _pendingIndexEntries.Count > 0));
        BrowseOutputDirectoryCommand = new RelayCommand(_ => ExecuteBrowseOutputDirectory(), _ => !IsProcessing);
        OpenOutputDirectoryCommand = new RelayCommand(
            _ => ExecuteOpenOutputDirectory(),
            _ => Tasks.Count > 0 || (UseCustomOutputDirectory && !string.IsNullOrWhiteSpace(OutputDirectory)));
    }

    public ObservableCollection<ImageTaskItem> Tasks { get; }

    public string[] AvailableFormats { get; } = ["JPG", "PNG", "BMP", "TIFF"];

    public MaxDimensionOption[] MaxDimensionOptions { get; } =
    [
        new("原尺寸 (不縮放)", 0),
        new("長邊上限 2560 px", 2560),
        new("長邊上限 1920 px", 1920),
        new("長邊上限 1280 px", 1280),
        new("長邊上限 800 px", 800)
    ];

    public string TargetFormat
    {
        get => _targetFormat;
        set
        {
            if (SetProperty(ref _targetFormat, value))
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

    public bool AutoOrient
    {
        get => _autoOrient;
        set
        {
            if (SetProperty(ref _autoOrient, value))
            {
                _onPreferencesChanged?.Invoke();
            }
        }
    }

    public int MaxDimension
    {
        get => _maxDimension;
        set
        {
            if (SetProperty(ref _maxDimension, value))
            {
                _onPreferencesChanged?.Invoke();
            }
        }
    }

    public string OutputDirectory
    {
        get => _outputDirectory;
        set
        {
            if (SetProperty(ref _outputDirectory, value)) OnPropertyChanged(nameof(OutputDirectoryHint));
        }
    }

    public bool UseCustomOutputDirectory
    {
        get => _useCustomOutputDirectory;
        set
        {
            if (SetProperty(ref _useCustomOutputDirectory, value)) OnPropertyChanged(nameof(OutputDirectoryHint));
        }
    }

    public string OutputDirectoryHint => UseCustomOutputDirectory && !string.IsNullOrWhiteSpace(OutputDirectory)
        ? $"輸出位置：{OutputDirectory}"
        : "輸出位置：各來源圖片所在資料夾下的 Converted 子資料夾（不覆寫原檔）";

    public bool IsProcessing
    {
        get => _isProcessing;
        set
        {
            if (SetProperty(ref _isProcessing, value))
            {
                OnPropertyChanged(nameof(CanEditSettings));
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    public double OverallProgress
    {
        get => _overallProgress;
        set => SetProperty(ref _overallProgress, value);
    }

    public string SummaryText
    {
        get => _summaryText;
        set => SetProperty(ref _summaryText, value);
    }

    public string IndexMessage { get => _indexMessage; private set => SetProperty(ref _indexMessage, value); }
    public bool CanEditSettings => !IsProcessing;
    public ICommand ReconvertCommand { get; }
    public ICommand RetryIndexCommand { get; }
    public ICommand AddFilesCommand { get; }
    public ICommand RemoveSelectedCommand { get; }
    public ICommand ClearAllCommand { get; }
    public ICommand ResetStatusCommand { get; }
    public ICommand StartConvertCommand { get; }
    public ICommand CancelConvertCommand { get; }
    public ICommand BrowseOutputDirectoryCommand { get; }
    public ICommand OpenOutputDirectoryCommand { get; }

    public int AddFiles(IEnumerable<string> inputPaths)
    {
        if (IsProcessing) return 0;
        var existingPaths = Tasks.Select(t => t.FilePath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        int added = 0;

        foreach (string candidate in ExpandPaths(inputPaths))
        {
            if (!File.Exists(candidate)) continue;

            string ext = Path.GetExtension(candidate);
            if (!SupportedExtensions.Contains(ext)) continue;
            if (!existingPaths.Add(candidate)) continue;

            var fi = new FileInfo(candidate);
            Tasks.Add(new ImageTaskItem
            {
                FilePath = candidate,
                FileName = fi.Name,
                SourceFormat = ext.TrimStart('.').ToUpperInvariant(),
                SourceSizeBytes = fi.Length
            });
            added++;
        }

        UpdateSummary();
        return added;
    }

    private static IEnumerable<string> ExpandPaths(IEnumerable<string> inputPaths)
    {
        foreach (string path in inputPaths)
        {
            if (string.IsNullOrWhiteSpace(path)) continue;

            if (File.Exists(path))
            {
                yield return path;
            }
            else if (Directory.Exists(path))
            {
                IEnumerable<string> files;
                try
                {
                    files = Directory.EnumerateFiles(path, "*.*", new EnumerationOptions
                    {
                        RecurseSubdirectories = true,
                        IgnoreInaccessible = true
                    });
                }
                catch
                {
                    continue;
                }

                foreach (string file in files)
                {
                    if (file.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                        .Any(segment => segment.Equals(".pit-recovery", StringComparison.OrdinalIgnoreCase))) continue;
                    yield return file;
                }
            }
        }
    }

    private void ExecuteAddFiles()
    {
        var dialog = new OpenFileDialog
        {
            Multiselect = true,
            Filter = "支援影像格式 (*.jpg;*.jpeg;*.png;*.heic;*.heif;*.webp;*.bmp;*.tif;*.tiff;*.gif)|*.jpg;*.jpeg;*.png;*.heic;*.heif;*.webp;*.bmp;*.tif;*.tiff;*.gif|所有檔案 (*.*)|*.*",
            Title = "選擇要轉檔的圖片"
        };

        if (dialog.ShowDialog() == true)
        {
            AddFiles(dialog.FileNames);
        }
    }

    private void ExecuteRemoveSelected(object? parameter)
    {
        if (parameter is ImageTaskItem item)
        {
            Tasks.Remove(item);
            UpdateSummary();
        }
    }

    private void ExecuteClearAll()
    {
        Tasks.Clear();
        OverallProgress = 0;
        UpdateSummary();
    }

    public void ResetTasksForReconvert()
    {
        if (IsProcessing || Tasks.Count == 0) return;

        foreach (var task in Tasks)
        {
            task.Status = TaskStatus.Pending;
            task.StatusMessage = "等待中";
            task.Progress = 0;
            task.OutputPath = string.Empty;
            task.OutputSizeBytes = 0;
            task.OutputWidth = 0;
            task.OutputHeight = 0;
            task.Elapsed = TimeSpan.Zero;
        }

        OverallProgress = 0;
        SummaryText = $"已重設 {Tasks.Count} 筆項目為待處理，可調整設定後再次開始轉檔。";
        CommandManager.InvalidateRequerySuggested();
    }

    public async Task StartConvertAsync()
    {
        if (Tasks.Count == 0 || IsProcessing) return;

        // 已成功轉檔的項目不重複處理，避免再次按下時產生 _1、_2 重複檔與重複索引；欲重轉可按「重設狀態」。
        var pendingTasks = Tasks.Where(t => t.Status != TaskStatus.Success).ToList();
        if (pendingTasks.Count == 0)
        {
            SummaryText = "清單中的圖片皆已轉檔完成；若要以新設定重轉，請按「依設定重新轉檔」。";
            return;
        }

        foreach (var task in pendingTasks)
        {
            task.Status = TaskStatus.Pending;
            task.StatusMessage = "等待中";
            task.Progress = 0;
        }

        IsProcessing = true;
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        // 整批固定設定；UI 修改或背景工作不得改變已開始的批次。
        string batchFormat = TargetFormat;
        int batchQuality = JpgQuality;
        bool batchOrient = AutoOrient;
        int batchDimension = MaxDimension;
        string? batchDirectory = UseCustomOutputDirectory && !string.IsNullOrWhiteSpace(OutputDirectory) ? OutputDirectory : null;
        IndexMessage = string.Empty;
        int total = pendingTasks.Count;
        int completed = 0;
        OverallProgress = 0;
        SummaryText = $"正在批次轉檔中 (0 / {total})...";

        var sw = Stopwatch.StartNew();

        try
        {
            var parallelOptions = new ParallelOptions
            {
                MaxDegreeOfParallelism = (int)Math.Clamp(GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / (512L * 1024 * 1024), 1, Math.Min(2, Environment.ProcessorCount)),
                CancellationToken = ct
            };

            await Parallel.ForEachAsync(pendingTasks, parallelOptions, async (task, token) =>
            {
                string targetDir = batchDirectory ?? Path.Combine(Path.GetDirectoryName(task.FilePath) ?? Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "Converted");

                var options = new ImageConvertOptions(
                    batchFormat,
                    batchQuality,
                    targetDir,
                    batchOrient,
                    batchDimension
                );

                await _imageService.ConvertAsync(task, options, token);

                int done = Interlocked.Increment(ref completed);
                double pct = (double)done / total * 100;

                RunOnUIThread(() =>
                {
                    OverallProgress = pct;
                    SummaryText = $"正在批次轉檔中 ({done} / {total}) - {pct:0}%";
                });
            });

            sw.Stop();
            int successCount = pendingTasks.Count(t => t.Status == TaskStatus.Success);
            SummaryText = $"轉檔完成！成功: {successCount} 筆，失敗: {total - successCount} 筆 (總耗時: {sw.Elapsed.TotalSeconds:0.##} 秒)";
        }
        catch (OperationCanceledException)
        {
            SummaryText = "已停止轉檔；完成的圖片已保留，其餘項目可再按「開始轉檔」繼續。";
        }
        catch (Exception ex)
        {
            SummaryText = $"轉檔過程發生例外: {ex.Message}";
        }
        finally
        {
            // 包含取消及失敗前已成功寫出的檔案；重試時也補齊先前漏寫的索引。
            IndexMessage = await UpdateOutputIndexesAsync(Tasks);
            IsProcessing = false;
            _cts?.Dispose();
            _cts = null;
            CommandManager.InvalidateRequerySuggested();
        }
    }

    private void ExecuteCancelConvert()
    {
        _cts?.Cancel();
    }

    private async Task<string> UpdateOutputIndexesAsync(IEnumerable<ImageTaskItem> processedTasks)
    {
        foreach (var group in processedTasks.Where(t => t.Status == TaskStatus.Success && !string.IsNullOrWhiteSpace(t.OutputPath))
            .GroupBy(t => Path.GetDirectoryName(t.OutputPath)!))
        {
            if (!_pendingIndexEntries.TryGetValue(group.Key, out var entries))
                _pendingIndexEntries[group.Key] = entries = [];
            foreach (var task in group)
            {
                string outputName = Path.GetFileName(task.OutputPath);
                entries.RemoveAll(x => string.Equals(x.OutputFile, outputName, StringComparison.OrdinalIgnoreCase));
                entries.Add(new OutputIndexEntry(outputName, task.FileName, "image_conversion", null, task.OutputWidth, task.OutputHeight));
            }
        }
        if (_pendingIndexEntries.Count == 0) return string.Empty;
        int updated = 0, failed = 0;
        foreach (var group in _pendingIndexEntries.ToArray())
        {
            try
            {
                await _outputIndexService.AppendEntriesAsync(group.Key, group.Value);
                _pendingIndexEntries.Remove(group.Key);
                updated++;
            }
            catch { failed++; }
        }
        return failed == 0 ? $"來源紀錄已更新（{updated} 個資料夾）"
            : $"{failed} 個資料夾的來源紀錄更新失敗；圖片已保留，請按「重試來源紀錄」。";
    }

    private void ExecuteBrowseOutputDirectory()
    {
        var dialog = new OpenFolderDialog
        {
            Title = "選擇輸出資料夾"
        };

        if (dialog.ShowDialog() == true)
        {
            OutputDirectory = dialog.FolderName;
            UseCustomOutputDirectory = true;
        }
    }

    private void ExecuteOpenOutputDirectory()
    {
        string dir = UseCustomOutputDirectory && !string.IsNullOrWhiteSpace(OutputDirectory)
            ? OutputDirectory
            : (Tasks.FirstOrDefault(t => !string.IsNullOrEmpty(t.OutputPath))?.OutputPath is { } p ? Path.GetDirectoryName(p) : null)
              ?? (Tasks.FirstOrDefault() is { } first ? Path.Combine(Path.GetDirectoryName(first.FilePath)!, "Converted") : Environment.GetFolderPath(Environment.SpecialFolder.Desktop));

        try
        {
            Directory.CreateDirectory(dir);
            Process.Start(new ProcessStartInfo
            {
                FileName = dir,
                UseShellExecute = true
            });
        }
        catch (Exception ex) { SummaryText = $"無法開啟輸出資料夾：{ex.Message}"; }
    }

    private void UpdateSummary()
    {
        if (Tasks.Count == 0)
        {
            SummaryText = "請拖曳圖片或資料夾，或點擊「加入圖片」開始";
        }
        else
        {
            long totalBytes = Tasks.Sum(t => t.SourceSizeBytes);
            SummaryText = $"已載入 {Tasks.Count} 張圖片 (合計 {ByteSizeFormatter.Format(totalBytes)})";
        }

        CommandManager.InvalidateRequerySuggested();
    }
}
