using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PoliceImageToolkit.Models;

namespace PoliceImageToolkit.Services;

public class VideoService : IVideoService
{
    public async Task<SnapshotResult> SaveFrameSnapshotAsync(
        BitmapSource sourceFrame,
        TimeSpan currentPosition,
        string videoFilePath,
        VideoSnapshotConfig config)
    {
        return await Task.Run(() =>
        {
            string? reservedOutputPath = null;
            try
            {
            string outDir = string.IsNullOrWhiteSpace(config.OutputDirectory)
                ? Path.GetDirectoryName(videoFilePath) ?? Environment.GetFolderPath(Environment.SpecialFolder.Desktop)
                : config.OutputDirectory;

            if (config.AutoCreateSubfolder)
            {
                outDir = SnapshotOutputDirectory.Resolve(outDir, videoFilePath);
            }

            if (!Directory.Exists(outDir))
            {
                Directory.CreateDirectory(outDir);
            }

            BitmapSource processedBitmap = sourceFrame;

            // 1. 烙印時間戳浮水印 (若開啟)
            if (config.AddTimestampOverlay)
            {
                string title = string.IsNullOrWhiteSpace(config.CasePrefix)
                    ? Path.GetFileName(videoFilePath)
                    : $"{config.CasePrefix} - {Path.GetFileName(videoFilePath)}";

                processedBitmap = RenderFrameWithTimestamp(processedBitmap, currentPosition, title, config.IncludeMilliseconds);
            }

            // 2. 檔名構造：預設純流水號，選填前綴才加在前方。
            string safeCasePrefix = SanitizeFileNameSegment(config.CasePrefix);
            string ext = config.OutputFormat.ToLowerInvariant() == "jpg" ? "jpg" : "png";
            // 3. 編碼並存檔
            BitmapEncoder encoder = ext == "jpg"
                ? new JpegBitmapEncoder { QualityLevel = Math.Clamp(config.JpgQuality, 10, 100) }
                : new PngBitmapEncoder();

            encoder.Frames.Add(BitmapFrame.Create(processedBitmap));

            using (var fs = CreateSequentialOutputFile(outDir, safeCasePrefix, ext, out reservedOutputPath))
            {
                encoder.Save(fs);
            }

            var fi = new FileInfo(reservedOutputPath);

            // 產生輕量縮圖供清單顯示 (縮小至寬度 200px，降低長工作階段記憶體佔用)
            BitmapSource thumb = processedBitmap;
            if (processedBitmap.PixelWidth > 200)
            {
                double scale = 200.0 / processedBitmap.PixelWidth;
                var scaled = new TransformedBitmap(processedBitmap, new ScaleTransform(scale, scale));
                scaled.Freeze();
                thumb = scaled;
            }

            return new SnapshotResult(
                reservedOutputPath,
                currentPosition,
                processedBitmap.PixelWidth,
                processedBitmap.PixelHeight,
                fi.Length,
                thumb,
                null,
                videoFilePath,
                null
            );
            }
            catch
            {
                DeletePartialOutput(reservedOutputPath);
                throw;
            }
        });
    }

    public BitmapSource RenderFrameWithTimestamp(
        BitmapSource frame,
        TimeSpan timestamp,
        string videoTitle,
        bool includeMs)
    {
        int width = frame.PixelWidth;
        int height = frame.PixelHeight;

        var visual = new DrawingVisual();
        using (DrawingContext dc = visual.RenderOpen())
        {
            // 繪製原始影格
            dc.DrawImage(frame, new Rect(0, 0, width, height));

            // 計算字體與浮水印尺寸 (依影像寬度動態縮放)
            double fontSize = Math.Clamp(width * 0.024, 14, 42);
            var typeface = new Typeface(new FontFamily("Segoe UI, Microsoft JhengHei"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);

            string timeText = includeMs
                ? $"⏱ {(int)timestamp.TotalHours:00}:{timestamp.Minutes:00}:{timestamp.Seconds:00}.{timestamp.Milliseconds:000}"
                : $"⏱ {(int)timestamp.TotalHours:00}:{timestamp.Minutes:00}:{timestamp.Seconds:00}";

            var ftTime = new FormattedText(
                timeText,
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                typeface,
                fontSize,
                Brushes.White,
                1.0
            );

            var titleBrush = new SolidColorBrush(Color.FromRgb(220, 225, 235));
            titleBrush.Freeze();

            var ftTitle = new FormattedText(
                videoTitle,
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                typeface,
                fontSize * 0.75,
                titleBrush,
                1.0
            );

            double padding = fontSize * 0.5;
            double maxTitleWidth = Math.Max(40, width - ftTime.Width - padding * 3);
            ftTitle.MaxTextWidth = maxTitleWidth;
            ftTitle.MaxLineCount = 1;
            ftTitle.Trimming = TextTrimming.CharacterEllipsis;

            double barHeight = ftTime.Height + padding * 2;
            double barY = height - barHeight;

            // 繪製半透明深色底條 (現代深邃黑藍底條)
            var bgBrush = new SolidColorBrush(Color.FromArgb(175, 10, 18, 30));
            bgBrush.Freeze();
            dc.DrawRectangle(bgBrush, null, new Rect(0, barY, width, barHeight));

            // 繪製時間戳記與檔名
            dc.DrawText(ftTime, new Point(padding, barY + padding));
            double titleX = Math.Max(padding + ftTime.Width + padding, width - ftTitle.Width - padding);
            dc.DrawText(ftTitle, new Point(titleX, barY + padding + (ftTime.Height - ftTitle.Height) / 2));
        }

        var renderBitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        renderBitmap.Render(visual);
        renderBitmap.Freeze();
        return renderBitmap;
    }

    public static BitmapSource CreateComparisonStrip(BitmapSource source, bool top)
    {
        int stripHeight = Math.Max(1, source.PixelHeight / 3);
        int y = top ? 0 : Math.Max(0, source.PixelHeight - stripHeight);
        BitmapSource strip = new CroppedBitmap(source, new Int32Rect(0, y, source.PixelWidth, stripHeight));
        if (strip.PixelWidth > 1080)
        {
            double stripScale = 1080.0 / strip.PixelWidth;
            strip = new TransformedBitmap(strip, new ScaleTransform(stripScale, stripScale));
        }
        strip.Freeze();
        return strip;
    }

    private static FileStream CreateSequentialOutputFile(string outputDirectory, string prefix, string extension, out string outputPath)
    {
        // 從輸出資料夾與暫存回收區的最大既有流水號之後起算，避免刪圖後補空號打亂順序或導致無法還原。
        int startSequence = FindMaxSequence(outputDirectory, prefix) + 1;
        for (int sequence = startSequence; sequence < startSequence + 100_000; sequence++)
        {
            string fileName = string.IsNullOrWhiteSpace(prefix)
                ? $"{sequence:000}.{extension}"
                : $"{prefix}_{sequence:000}.{extension}";
            string candidate = Path.Combine(outputDirectory, fileName);
            try
            {
                var stream = new FileStream(candidate, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                outputPath = candidate;
                return stream;
            }
            catch (IOException) when (File.Exists(candidate))
            {
                // 同名截圖已存在或被其他工作搶先建立，改用下一個流水號。
            }
        }

        throw new IOException("無法建立不重複的截圖輸出檔案。");
    }

    private static int FindMaxSequence(string outputDirectory, string prefix)
    {
        int max = 0;
        string pattern = string.IsNullOrWhiteSpace(prefix)
            ? @"^(\d+)\.[^.]+$"
            : $@"^{Regex.Escape(prefix)}_(\d+)\.[^.]+$";
        var regex = new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        if (Directory.Exists(outputDirectory))
        {
            foreach (string file in Directory.EnumerateFiles(outputDirectory))
            {
                var m = regex.Match(Path.GetFileName(file));
                if (m.Success && int.TryParse(m.Groups[1].Value, out int n) && n > max)
                {
                    max = n;
                }
            }

            string recoveryDir = Path.Combine(outputDirectory, ".pit-recovery");
            if (Directory.Exists(recoveryDir))
            {
                foreach (string file in Directory.EnumerateFiles(recoveryDir))
                {
                    string name = Path.GetFileName(file);
                    // 回收檔格式為 "{32hex}_{原檔名}"
                    int sep = name.IndexOf('_');
                    string originalName = sep == 32 ? name[(sep + 1)..] : name;
                    var m = regex.Match(originalName);
                    if (m.Success && int.TryParse(m.Groups[1].Value, out int n) && n > max)
                    {
                        max = n;
                    }
                }
            }
        }

        return max;
    }

    private static string SanitizeFileNameSegment(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;

        var invalidChars = Path.GetInvalidFileNameChars()
            .Append(Path.DirectorySeparatorChar)
            .Append(Path.AltDirectorySeparatorChar)
            .ToHashSet();
        return new string(value.Trim().Select(c => invalidChars.Contains(c) ? '_' : c).ToArray());
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
            // 保留原始例外，讓呼叫端回報實際存檔失敗原因。
        }
    }
}
