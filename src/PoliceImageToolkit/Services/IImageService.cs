using PoliceImageToolkit.Models;

namespace PoliceImageToolkit.Services;

public record ImageConvertOptions(
    string TargetFormat,
    int JpgQuality,
    string OutputDirectory,
    bool AutoOrient = true,
    int MaxDimension = 0
);

public interface IImageService
{
    Task ConvertAsync(ImageTaskItem item, ImageConvertOptions options, CancellationToken ct = default);
}
