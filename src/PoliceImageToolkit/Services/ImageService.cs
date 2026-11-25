using System.Diagnostics;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PoliceImageToolkit.Models;
using TaskStatus = PoliceImageToolkit.Models.TaskStatus;

namespace PoliceImageToolkit.Services;

public class ImageService : IImageService
{
    private const long MaximumDecodedPixels = 100_000_000;

    public async Task ConvertAsync(ImageTaskItem item, ImageConvertOptions options, CancellationToken ct = default)
    {
        item.Status = TaskStatus.Processing;
        item.StatusMessage = "轉檔處理中...";
        item.Progress = 10;
        var sw = Stopwatch.StartNew();

        await Task.Run(() =>
        {
            string? reservedOutputPath = null;
            try
            {
                ct.ThrowIfCancellationRequested();
                if (!File.Exists(item.FilePath))
                {
                    throw new FileNotFoundException("找不到來源檔案", item.FilePath);
                }

                if (!Directory.Exists(options.OutputDirectory))
                {
                    Directory.CreateDirectory(options.OutputDirectory);
                }

                string baseName = Path.GetFileNameWithoutExtension(item.FilePath);
                string ext = options.TargetFormat.ToLowerInvariant().TrimStart('.');
                if (ext == "jpeg") ext = "jpg";
                ct.ThrowIfCancellationRequested();
                item.Progress = 30;

                // 先讀取標頭及尺寸，再進行像素解碼；串流保持開啟至編碼結束。
                // 優先保留色彩描述檔以維持 Display P3 等廣色域資訊；若遇到異常描述檔再退回 IgnoreColorProfile。
                using var fs = new FileStream(item.FilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                BitmapDecoder decoder = CreateSafeDecoder(fs, BitmapCacheOption.OnDemand);

                if (decoder.Frames.Count == 0)
                {
                    throw new InvalidOperationException("無法解碼此影像之影格。");
                }

                BitmapFrame frame = decoder.Frames[0];
                long pixelCount = (long)frame.PixelWidth * frame.PixelHeight;
                if (pixelCount <= 0 || pixelCount > MaximumDecodedPixels)
                {
                    throw new InvalidDataException($"影像解析度 {frame.PixelWidth} × {frame.PixelHeight} 超過安全處理上限。");
                }

                if (decoder.Frames.Count > 1)
                {
                    throw new NotSupportedException("不支援動畫 GIF 或多頁影像；為避免遺失內容，請先逐頁匯出再轉檔。");
                }

                BitmapSource processedBitmap = frame;
                item.Progress = 50;

                // 處理 Exif 旋轉方向
                if (options.AutoOrient && frame.Metadata is BitmapMetadata metadata)
                {
                    int orientation = GetExifOrientation(metadata);
                    processedBitmap = ApplyOrientation(processedBitmap, orientation);
                }

                // 處理等比例縮放 (若設定 MaxDimension)
                if (options.MaxDimension > 0)
                {
                    int origW = processedBitmap.PixelWidth;
                    int origH = processedBitmap.PixelHeight;
                    int maxSide = Math.Max(origW, origH);
                    if (maxSide > options.MaxDimension)
                    {
                        double scale = (double)options.MaxDimension / maxSide;
                        var scaled = new TransformedBitmap(processedBitmap, new ScaleTransform(scale, scale));
                        scaled.Freeze();
                        processedBitmap = scaled;
                    }
                }

                if (processedBitmap.CanFreeze && !processedBitmap.IsFrozen)
                {
                    processedBitmap.Freeze();
                }

                item.Progress = 75;
                ct.ThrowIfCancellationRequested();

                // 根據目標格式選擇編碼器
                BitmapEncoder encoder = ext switch
                {
                    "png" => new PngBitmapEncoder(),
                    "bmp" => new BmpBitmapEncoder(),
                    "tif" or "tiff" => new TiffBitmapEncoder(),
                    "jpg" => new JpegBitmapEncoder
                    {
                        QualityLevel = Math.Clamp(options.JpgQuality, 10, 100)
                    },
                    _ => throw new NotSupportedException($"不支援的目標輸出格式：.{ext}")
                };

                BitmapMetadata? outputMetadata = TryBuildOutputMetadata(frame.Metadata as BitmapMetadata, ext, options.AutoOrient);
                var outputColorContexts = ext is "jpg" or "png" or "tif" or "tiff" && frame.ColorContexts is { Count: > 0 }
                    ? frame.ColorContexts
                    : null;

                encoder.Frames.Add(CreateOutputFrame(processedBitmap, outputMetadata, outputColorContexts));

                using (var outFs = CreateUniqueOutputFile(options.OutputDirectory, baseName, ext, out reservedOutputPath))
                {
                    try
                    {
                        encoder.Save(outFs);
                    }
                    catch when (outputMetadata != null || outputColorContexts != null)
                    {
                        // 若特定來源的 Metadata 或 ICC 描述檔與目標編碼器不相容，安全退回純像素存檔，不中斷轉檔。
                        outFs.SetLength(0);
                        outFs.Position = 0;
                        BitmapEncoder fallbackEncoder = ext switch
                        {
                            "png" => new PngBitmapEncoder(),
                            "bmp" => new BmpBitmapEncoder(),
                            "tif" or "tiff" => new TiffBitmapEncoder(),
                            "jpg" => new JpegBitmapEncoder { QualityLevel = Math.Clamp(options.JpgQuality, 10, 100) },
                            _ => throw new NotSupportedException($"不支援的目標輸出格式：.{ext}")
                        };
                        fallbackEncoder.Frames.Add(BitmapFrame.Create(processedBitmap));
                        fallbackEncoder.Save(outFs);
                    }
                }

                sw.Stop();
                var outInfo = new FileInfo(reservedOutputPath);

                item.OutputPath = reservedOutputPath;
                item.OutputSizeBytes = outInfo.Length;
                item.OutputWidth = processedBitmap.PixelWidth;
                item.OutputHeight = processedBitmap.PixelHeight;
                item.Elapsed = sw.Elapsed;
                item.Progress = 100;
                item.Status = TaskStatus.Success;
                item.StatusMessage = $"完成 ({sw.ElapsedMilliseconds} ms)";
            }
            catch (OperationCanceledException)
            {
                DeletePartialOutput(reservedOutputPath);
                item.Status = TaskStatus.Failed;
                item.StatusMessage = "已取消";
            }
            catch (Exception ex)
            {
                DeletePartialOutput(reservedOutputPath);
                sw.Stop();
                item.Status = TaskStatus.Failed;
                item.StatusMessage = $"失敗: {GetUserFriendlyError(item.FilePath, ex)}";
            }
        });
    }

    private static FileStream CreateUniqueOutputFile(string outputDirectory, string baseName, string extension, out string outputPath)
    {
        for (int counter = 0; counter <= 10_000; counter++)
        {
            string suffix = counter == 0 ? string.Empty : $"_{counter}";
            string candidate = Path.Combine(outputDirectory, $"{baseName}{suffix}.{extension}");
            try
            {
                var stream = new FileStream(candidate, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                outputPath = candidate;
                return stream;
            }
            catch (IOException) when (File.Exists(candidate))
            {
                // 同名檔已存在或被另一個平行工作搶先建立，改用下一個流水號。
            }
        }

        throw new IOException("同名輸出檔案數量超過安全上限，無法建立新檔。");
    }

    private static void DeletePartialOutput(string? outputPath)
    {
        if (string.IsNullOrEmpty(outputPath)) return;

        try
        {
            if (File.Exists(outputPath)) File.Delete(outputPath);
        }
        catch
        {
            // 保留原始例外訊息；無法清除時不將殘檔誤報為成功輸出。
        }
    }

    private static string GetUserFriendlyError(string sourcePath, Exception ex)
    {
        string extension = Path.GetExtension(sourcePath).ToUpperInvariant();
        if (extension is ".HEIC" or ".HEIF" or ".WEBP" && ex is NotSupportedException or FileFormatException)
        {
            return $"無法解碼 {extension}。請確認 Windows 已具備此格式的影像編解碼元件後重試。";
        }

        return ex.Message;
    }

    public static BitmapSource LoadOrientedBitmap(string imagePath)
    {
        using var fs = new FileStream(imagePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        BitmapDecoder decoder = CreateSafeDecoder(fs, BitmapCacheOption.OnLoad);
        if (decoder.Frames.Count == 0)
        {
            throw new InvalidOperationException("無法解碼此影像之影格。");
        }

        BitmapFrame frame = decoder.Frames[0];
        BitmapSource result = frame;
        if (frame.Metadata is BitmapMetadata metadata)
        {
            int orientation = GetExifOrientation(metadata);
            result = ApplyOrientation(result, orientation);
        }

        if (result.CanFreeze && !result.IsFrozen)
        {
            result.Freeze();
        }

        return result;
    }

    private static BitmapDecoder CreateSafeDecoder(Stream stream, BitmapCacheOption cacheOption)
    {
        try
        {
            stream.Position = 0;
            return BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, cacheOption);
        }
        catch
        {
            stream.Position = 0;
            return BitmapDecoder.Create(
                stream,
                BitmapCreateOptions.PreservePixelFormat | BitmapCreateOptions.IgnoreColorProfile,
                cacheOption
            );
        }
    }

    private static BitmapFrame CreateOutputFrame(
        BitmapSource processedBitmap,
        BitmapMetadata? metadata,
        System.Collections.ObjectModel.ReadOnlyCollection<ColorContext>? colorContexts)
    {
        if (metadata == null && (colorContexts == null || colorContexts.Count == 0))
        {
            return BitmapFrame.Create(processedBitmap);
        }

        try
        {
            return BitmapFrame.Create(processedBitmap, null, metadata, colorContexts);
        }
        catch
        {
            return BitmapFrame.Create(processedBitmap);
        }
    }

    private static BitmapMetadata? TryBuildOutputMetadata(BitmapMetadata? source, string targetExt, bool autoOriented)
    {
        if (source == null || targetExt is not ("jpg" or "tif" or "tiff")) return null;

        try
        {
            string format = targetExt == "jpg" ? "jpg" : "tiff";
            var output = new BitmapMetadata(format);
            bool copiedAny = false;

            string prefix = format == "jpg" ? "/app1" : string.Empty;
            (string SourceKey, string TargetKey)[] textTags =
            [
                ("/app1/ifd/exif:{ushort=36867}", $"{prefix}/ifd/exif:{{ushort=36867}}"), // DateTimeOriginal
                ("/ifd/exif:{ushort=36867}", $"{prefix}/ifd/exif:{{ushort=36867}}"),
                ("/app1/ifd/exif:{ushort=36868}", $"{prefix}/ifd/exif:{{ushort=36868}}"), // DateTimeDigitized
                ("/ifd/exif:{ushort=36868}", $"{prefix}/ifd/exif:{{ushort=36868}}"),
                ("/app1/ifd/{ushort=306}", $"{prefix}/ifd/{{ushort=306}}"),             // DateTime
                ("/ifd/{ushort=306}", $"{prefix}/ifd/{{ushort=306}}"),
                ("/app1/ifd/{ushort=271}", $"{prefix}/ifd/{{ushort=271}}"),             // Make
                ("/ifd/{ushort=271}", $"{prefix}/ifd/{{ushort=271}}"),
                ("/app1/ifd/{ushort=272}", $"{prefix}/ifd/{{ushort=272}}"),             // Model
                ("/ifd/{ushort=272}", $"{prefix}/ifd/{{ushort=272}}"),
                ("/app1/ifd/{ushort=305}", $"{prefix}/ifd/{{ushort=305}}"),             // Software
                ("/ifd/{ushort=305}", $"{prefix}/ifd/{{ushort=305}}"),
            ];

            var writtenTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (srcKey, dstKey) in textTags)
            {
                if (writtenTargets.Contains(dstKey)) continue;
                try
                {
                    if (!source.ContainsQuery(srcKey)) continue;
                    object? val = source.GetQuery(srcKey);
                    if (val is string s && !string.IsNullOrWhiteSpace(s))
                    {
                        output.SetQuery(dstKey, s);
                        writtenTargets.Add(dstKey);
                        copiedAny = true;
                    }
                }
                catch
                {
                    // 單一標籤不支援時略過，不影響其他欄位與轉檔。
                }
            }

            if (autoOriented)
            {
                // 像素已依 Exif 旋轉歸正，寫入 Orientation=1 避免檢視器重複旋轉。
                output.SetQuery($"{prefix}/ifd/{{ushort=274}}", (ushort)1);
                copiedAny = true;
            }
            else
            {
                int origOrientation = GetExifOrientation(source);
                if (origOrientation is >= 1 and <= 8)
                {
                    output.SetQuery($"{prefix}/ifd/{{ushort=274}}", (ushort)origOrientation);
                    copiedAny = true;
                }
            }

            return copiedAny ? output : null;
        }
        catch
        {
            return null;
        }
    }

    private static int GetExifOrientation(BitmapMetadata metadata)
    {
        foreach (string query in new[] { "/app1/ifd/{ushort=274}", "/ifd/{ushort=274}" })
        {
            try
            {
                if (!metadata.ContainsQuery(query)) continue;
                object val = metadata.GetQuery(query);
                if (val is ushort u) return u;
                if (val is int i) return i;
            }
            catch (NotSupportedException) { /* 此格式不提供該 metadata 查詢。 */ }
        }
        return 1;
    }

    private static BitmapSource ApplyOrientation(BitmapSource source, int orientation)
    {
        Transform transform = orientation switch
        {
            2 => new ScaleTransform(-1, 1), // Flip Horizontal
            3 => new RotateTransform(180),  // Rotate 180
            4 => new ScaleTransform(1, -1), // Flip Vertical
            5 => new TransformGroup
            {
                Children = { new RotateTransform(90), new ScaleTransform(-1, 1) }
            },
            6 => new RotateTransform(90),   // Rotate 90 CW
            7 => new TransformGroup
            {
                Children = { new RotateTransform(270), new ScaleTransform(-1, 1) }
            },
            8 => new RotateTransform(270),  // Rotate 270 CW
            _ => Transform.Identity
        };

        if (transform == Transform.Identity)
        {
            return source;
        }

        var transformed = new TransformedBitmap(source, transform);
        transformed.Freeze();
        return transformed;
    }
}
