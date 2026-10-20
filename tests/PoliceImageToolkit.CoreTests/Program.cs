using PoliceImageToolkit.Services;
using PoliceImageToolkit.Models;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

var tests = new (string Name, Action Execute)[]
{
    ("影片截圖建立獨立資料夾並使用流水號", VideoSnapshot_UsesFolderAndSequentialNames),
    ("輸出索引保留最小追溯資訊並可移除已刪除檔案", OutputIndex_WritesAndRemovesEntries),
    ("圖片轉檔不覆寫來源 PNG 或 JPG", ImageConversion_CreatesNewFilesWithoutChangingSources),
    ("版本比較可辨識新舊版本", UpdateService_ComparesSemanticVersions)
};

int failed = 0;
foreach (var test in tests)
{
    try
    {
        test.Execute();
        Console.WriteLine($"PASS: {test.Name}");
    }
    catch (Exception ex)
    {
        failed++;
        Console.Error.WriteLine($"FAIL: {test.Name}\n{ex.Message}");
    }
}

Console.WriteLine($"完成：{tests.Length - failed}/{tests.Length} 項測試通過。");
return failed == 0 ? 0 : 1;

static void VideoSnapshot_UsesFolderAndSequentialNames()
{
    string directory = Path.Combine(Path.GetTempPath(), $"PoliceImageToolkit-{Guid.NewGuid():N}");
    try
    {
        var service = new VideoService();
        var config = new VideoSnapshotConfig
        {
            OutputDirectory = directory,
            OutputFormat = "PNG",
            AutoCreateSubfolder = true,
            CasePrefix = string.Empty
        };
        var frame = CreateTestBitmap(8, 8);
        string videoPath = Path.Combine(directory, "sample.mp4");

        var first = service.SaveFrameSnapshotAsync(frame, TimeSpan.Zero, videoPath, config).GetAwaiter().GetResult();
        var second = service.SaveFrameSnapshotAsync(frame, TimeSpan.FromSeconds(1), videoPath, config).GetAwaiter().GetResult();

        AssertEqual("001.png", Path.GetFileName(first.FilePath), "影片第 1 張檔名");
        AssertEqual("002.png", Path.GetFileName(second.FilePath), "影片第 2 張檔名");
        AssertEqual(Path.Combine(directory, "sample_Snapshots"), Path.GetDirectoryName(first.FilePath)!, "影片輸出資料夾");
    }
    finally
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, true);
    }
}

static void OutputIndex_WritesAndRemovesEntries()
{
    string directory = Path.Combine(Path.GetTempPath(), $"PoliceImageToolkit-{Guid.NewGuid():N}");
    try
    {
        var service = new OutputIndexService();
        string sourcePath = Path.Combine(directory, "private", "evidence.mp4");
        service.AppendEntriesAsync(directory,
        [new OutputIndexEntry("001.png", Path.GetFileName(sourcePath), "video_snapshot", "00:00:01.250", 1080, 1920)])
            .GetAwaiter().GetResult();

        string indexPath = Path.Combine(directory, "report_index.json");
        string json = File.ReadAllText(indexPath);
        AssertTrue(json.Contains("\"output_file\": \"001.png\"", StringComparison.Ordinal), "索引應包含輸出檔名");
        AssertTrue(json.Contains("\"source_file\": \"evidence.mp4\"", StringComparison.Ordinal), "索引應只包含來源檔名");
        AssertTrue(json.Contains("\"media_timestamp\": \"00:00:01.250\"", StringComparison.Ordinal), "索引應包含影片時間");
        AssertTrue(!json.Contains("private", StringComparison.OrdinalIgnoreCase), "索引不可包含來源絕對路徑");

        service.RemoveEntriesAsync(directory, ["001.png"]).GetAwaiter().GetResult();
        string updatedJson = File.ReadAllText(indexPath);
        AssertTrue(!updatedJson.Contains("001.png", StringComparison.Ordinal), "刪除輸出後索引不可保留該項目");
    }
    finally
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, true);
    }
}

static void ImageConversion_CreatesNewFilesWithoutChangingSources()
{
    string directory = Path.Combine(Path.GetTempPath(), $"PoliceImageToolkit-{Guid.NewGuid():N}");
    try
    {
        Directory.CreateDirectory(directory);
        string pngPath = Path.Combine(directory, "source.png");
        string jpgPath = Path.Combine(directory, "source.jpg");
        SaveTestBitmap(pngPath, new PngBitmapEncoder());
        SaveTestBitmap(jpgPath, new JpegBitmapEncoder { QualityLevel = 90 });
        string pngHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(pngPath)));
        string jpgHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(jpgPath)));

        string outputDirectory = Path.Combine(directory, "Converted");
        var service = new ImageService();
        var pngTask = CreateImageTask(pngPath);
        var jpgTask = CreateImageTask(jpgPath);
        service.ConvertAsync(pngTask, new ImageConvertOptions("JPG", 90, outputDirectory, true, 0)).GetAwaiter().GetResult();
        service.ConvertAsync(jpgTask, new ImageConvertOptions("PNG", 90, outputDirectory, true, 0)).GetAwaiter().GetResult();

        AssertTrue(pngTask.Status == PoliceImageToolkit.Models.TaskStatus.Success, "PNG 轉 JPG 應成功");
        AssertTrue(jpgTask.Status == PoliceImageToolkit.Models.TaskStatus.Success, "JPG 轉 PNG 應成功");
        AssertTrue(File.Exists(pngTask.OutputPath) && File.Exists(jpgTask.OutputPath), "轉檔輸出應存在");
        AssertTrue(!string.Equals(pngTask.OutputPath, pngPath, StringComparison.OrdinalIgnoreCase), "PNG 不可覆寫來源");
        AssertTrue(!string.Equals(jpgTask.OutputPath, jpgPath, StringComparison.OrdinalIgnoreCase), "JPG 不可覆寫來源");
        AssertEqual(pngHash, Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(pngPath))), "PNG 來源雜湊");
        AssertEqual(jpgHash, Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(jpgPath))), "JPG 來源雜湊");
    }
    finally
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, true);
    }
}

static ImageTaskItem CreateImageTask(string path)
{
    var info = new FileInfo(path);
    return new ImageTaskItem
    {
        FilePath = path,
        FileName = info.Name,
        SourceFormat = info.Extension.TrimStart('.').ToUpperInvariant(),
        SourceSizeBytes = info.Length
    };
}

static void SaveTestBitmap(string path, BitmapEncoder encoder)
{
    encoder.Frames.Add(BitmapFrame.Create(CreateTestBitmap(8, 8)));
    using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
    encoder.Save(output);
}

static BitmapSource CreateTestBitmap(int width, int height)
{
    int stride = width * 4;
    var source = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, new byte[stride * height], stride);
    source.Freeze();
    return source;
}

static void UpdateService_ComparesSemanticVersions()
{
    AssertTrue(UpdateService.CompareVersions("v11.3.0", "v11.2.0") > 0, "新版應大於舊版");
    AssertTrue(UpdateService.CompareVersions("v11.2.0-beta", "11.2.0") == 0, "預發布後綴應採相同核心版本");
    AssertTrue(UpdateService.CompareVersions("v11.1.9", "v11.2.0") < 0, "舊版應小於新版");
}

static void AssertEqual<T>(T expected, T actual, string name) where T : IEquatable<T>
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        throw new InvalidOperationException($"{name}預期為 {expected}，實際為 {actual}。");
    }
}

static void AssertTrue(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
