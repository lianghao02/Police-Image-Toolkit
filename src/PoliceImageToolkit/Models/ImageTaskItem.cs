using System.Windows.Media.Imaging;
using PoliceImageToolkit.ViewModels;

namespace PoliceImageToolkit.Models;

public enum TaskStatus
{
    Pending,
    Processing,
    Success,
    Failed
}

public class ImageTaskItem : ViewModelBase
{
    private TaskStatus _status = TaskStatus.Pending;
    private string _statusMessage = "等待中";
    private double _progress = 0;
    private string _outputPath = string.Empty;
    private long _outputSizeBytes = 0;
    private int _outputWidth;
    private int _outputHeight;
    private TimeSpan _elapsed = TimeSpan.Zero;
    private BitmapSource? _thumbnail;

    public required string FilePath { get; init; }
    public required string FileName { get; init; }
    public required string SourceFormat { get; init; }
    public required long SourceSizeBytes { get; init; }

    public TaskStatus Status
    {
        get => _status;
        set => SetProperty(ref _status, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    public double Progress
    {
        get => _progress;
        set => SetProperty(ref _progress, value);
    }

    public string OutputPath
    {
        get => _outputPath;
        set => SetProperty(ref _outputPath, value);
    }

    public long OutputSizeBytes
    {
        get => _outputSizeBytes;
        set
        {
            if (SetProperty(ref _outputSizeBytes, value)) OnPropertyChanged(nameof(OutputSizeFormatted));
        }
    }

    public int OutputWidth
    {
        get => _outputWidth;
        set => SetProperty(ref _outputWidth, value);
    }

    public int OutputHeight
    {
        get => _outputHeight;
        set => SetProperty(ref _outputHeight, value);
    }

    public TimeSpan Elapsed
    {
        get => _elapsed;
        set => SetProperty(ref _elapsed, value);
    }

    public BitmapSource? Thumbnail
    {
        get => _thumbnail;
        set => SetProperty(ref _thumbnail, value);
    }

    public string SourceSizeFormatted => ByteSizeFormatter.Format(SourceSizeBytes);
    public string OutputSizeFormatted => OutputSizeBytes > 0 ? ByteSizeFormatter.Format(OutputSizeBytes) : "-";
}
