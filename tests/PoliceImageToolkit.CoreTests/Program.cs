using PoliceImageToolkit.Models;
using PoliceImageToolkit.Services;
using PoliceImageToolkit.ViewModels;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

var tests = new (string Name, Action Execute)[]
{
    ("影片截圖建立獨立資料夾並使用流水號", VideoSnapshot_UsesFolderAndSequentialNames),
    ("影片截圖支援時間戳浮水印渲染、JPG 格式與非法前綴字元過濾", VideoSnapshot_RendersTimestampOverlayAndSanitizesPrefix),
    ("輸出索引保留最小追溯資訊並可移除已刪除檔案", OutputIndex_WritesAndRemovesEntries),
    ("輸出索引遇到損毀或外來 JSON 時拒絕覆寫", OutputIndex_RejectsCorruptedOrForeignIndexWithoutOverwriting),
    ("圖片轉檔不覆寫來源 PNG 或 JPG", ImageConversion_CreatesNewFilesWithoutChangingSources),
    ("圖片轉檔自動校正 Exif 方向並依最大邊等比例縮放", ImageConversion_AutoOrientsExifAndScalesMaxDimension),
    ("圖片轉檔支援同名防碰撞且取消時不殘留檔案", ImageConversion_AvoidsNameCollisionAndCleansCancelledTempFile),
    ("圖片轉檔拒絕動畫 GIF 與不支援之目標格式", ImageConversion_RejectsAnimatedGifAndUnsupportedTargetFormat),
    ("圖片轉檔 ViewModel 支援資料夾遞迴展開、去重與重設重轉", ImageConverterViewModel_ExpandsFoldersDeduplicatesAndResetsTasks),
    ("影片截圖 ViewModel 於 MediaOpened 前鎖定截圖並支援復原刪除", VideoSnapshotViewModel_LocksCaptureUntilMediaOpenedAndSupportsUndo),
    ("更新服務可正確比對版本並只允許 HTTPS 下載網址", UpdateService_ComparesSemanticVersionsAndValidatesHttpsUrls),
    ("偏好設定服務可正常讀寫並在設定損毀時退回預設值", UserSettingsService_RoundTripsAndHandlesCorruptedFile)
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
        Console.Error.WriteLine($"FAIL: {test.Name}\n{ex}");
    }
}

Console.WriteLine($"完成：{tests.Length - failed}/{tests.Length} 項測試通過。");
return failed == 0 ? 0 : 1;

static void VideoSnapshot_UsesFolderAndSequentialNames()
{
    string directory = CreateTempDir();
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
        CleanupTempDir(directory);
    }
}

static void VideoSnapshot_RendersTimestampOverlayAndSanitizesPrefix()
{
    string directory = CreateTempDir();
    try
    {
        var service = new VideoService();
        var config = new VideoSnapshotConfig
        {
            OutputDirectory = directory,
            OutputFormat = "JPG",
            JpgQuality = 85,
            AddTimestampOverlay = true,
            IncludeMilliseconds = true,
            AutoCreateSubfolder = true,
            CasePrefix = "案號/A:B*01"
        };
        var frame = CreateTestBitmap(640, 360);
        string videoPath = Path.Combine(directory, "cam01.mov");

        var result = service.SaveFrameSnapshotAsync(frame, TimeSpan.FromSeconds(12.345), videoPath, config).GetAwaiter().GetResult();

        AssertEqual("案號_A_B_01_001.jpg", Path.GetFileName(result.FilePath), "過濾後的前綴檔名");
        AssertTrue(File.Exists(result.FilePath), "JPG 浮水印輸出檔案應存在");
        AssertEqual(640, result.Width, "浮水印輸出寬度");
        AssertEqual(360, result.Height, "浮水印輸出高度");
    }
    finally
    {
        CleanupTempDir(directory);
    }
}

static void OutputIndex_WritesAndRemovesEntries()
{
    string directory = CreateTempDir();
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
        CleanupTempDir(directory);
    }
}

static void OutputIndex_RejectsCorruptedOrForeignIndexWithoutOverwriting()
{
    string directory = CreateTempDir();
    try
    {
        Directory.CreateDirectory(directory);
        string indexPath = Path.Combine(directory, "report_index.json");
        string foreignContent = "{\"schema\":\"other-tool\",\"entries\":[]}";
        File.WriteAllText(indexPath, foreignContent);

        var service = new OutputIndexService();
        bool threwForeign = false;
        try
        {
            service.AppendEntriesAsync(directory, [new OutputIndexEntry("001.png", "a.jpg", "image_conversion", null, 8, 8)]).GetAwaiter().GetResult();
        }
        catch (InvalidDataException)
        {
            threwForeign = true;
        }
        AssertTrue(threwForeign, "非本工具之 report_index.json 應拒絕覆寫");
        AssertEqual(foreignContent, File.ReadAllText(indexPath), "外來索引內容不應被變更");

        string badJson = "{not-valid-json";
        File.WriteAllText(indexPath, badJson);
        bool threwJson = false;
        try
        {
            service.AppendEntriesAsync(directory, [new OutputIndexEntry("001.png", "a.jpg", "image_conversion", null, 8, 8)]).GetAwaiter().GetResult();
        }
        catch (InvalidDataException)
        {
            threwJson = true;
        }
        AssertTrue(threwJson, "損毀 JSON 應拋出 InvalidDataException");
        AssertEqual(badJson, File.ReadAllText(indexPath), "損毀 JSON 不應被靜默覆寫");
    }
    finally
    {
        CleanupTempDir(directory);
    }
}

static void ImageConversion_CreatesNewFilesWithoutChangingSources()
{
    string directory = CreateTempDir();
    try
    {
        Directory.CreateDirectory(directory);
        string pngPath = Path.Combine(directory, "source.png");
        string jpgPath = Path.Combine(directory, "source.jpg");
        SaveTestBitmap(pngPath, new PngBitmapEncoder());
        SaveTestBitmap(jpgPath, new JpegBitmapEncoder { QualityLevel = 90 });
        string pngHash = ComputeSha256(pngPath);
        string jpgHash = ComputeSha256(jpgPath);

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
        AssertEqual(pngHash, ComputeSha256(pngPath), "PNG 來源雜湊");
        AssertEqual(jpgHash, ComputeSha256(jpgPath), "JPG 來源雜湊");
    }
    finally
    {
        CleanupTempDir(directory);
    }
}

static void ImageConversion_AutoOrientsExifAndScalesMaxDimension()
{
    string directory = CreateTempDir();
    try
    {
        Directory.CreateDirectory(directory);
        string jpgPath = Path.Combine(directory, "rotated.jpg");

        // 建立 40x20 影像並寫入 Exif Orientation = 6 (順時針 90 度，旋轉後應為 20x40)
        var metadata = new BitmapMetadata("jpg");
        metadata.SetQuery("/app1/ifd/{ushort=274}", (ushort)6);
        var encoder = new JpegBitmapEncoder { QualityLevel = 95 };
        encoder.Frames.Add(BitmapFrame.Create(CreateTestBitmap(40, 20), null, metadata, null));
        using (var fs = new FileStream(jpgPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            encoder.Save(fs);
        }

        string outDir = Path.Combine(directory, "Out");
        var service = new ImageService();
        var task = CreateImageTask(jpgPath);
        service.ConvertAsync(task, new ImageConvertOptions("PNG", 90, outDir, AutoOrient: true, MaxDimension: 20)).GetAwaiter().GetResult();

        AssertTrue(task.Status == PoliceImageToolkit.Models.TaskStatus.Success, "Exif 旋轉與縮放轉檔應成功");
        AssertEqual(10, task.OutputWidth, "順時針 90 度並縮放長邊至 20 後寬度");
        AssertEqual(20, task.OutputHeight, "順時針 90 度並縮放長邊至 20 後高度");
    }
    finally
    {
        CleanupTempDir(directory);
    }
}

static void ImageConversion_AvoidsNameCollisionAndCleansCancelledTempFile()
{
    string directory = CreateTempDir();
    try
    {
        Directory.CreateDirectory(directory);
        string srcPath = Path.Combine(directory, "photo.png");
        SaveTestBitmap(srcPath, new PngBitmapEncoder());

        string outDir = Path.Combine(directory, "Out");
        var service = new ImageService();
        var t1 = CreateImageTask(srcPath);
        var t2 = CreateImageTask(srcPath);
        service.ConvertAsync(t1, new ImageConvertOptions("JPG", 90, outDir, true, 0)).GetAwaiter().GetResult();
        service.ConvertAsync(t2, new ImageConvertOptions("JPG", 90, outDir, true, 0)).GetAwaiter().GetResult();

        AssertEqual("photo.jpg", Path.GetFileName(t1.OutputPath), "第 1 筆輸出檔名");
        AssertEqual("photo_1.jpg", Path.GetFileName(t2.OutputPath), "撞名後應產生流水號 _1");

        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var tCancelled = CreateImageTask(srcPath);
        service.ConvertAsync(tCancelled, new ImageConvertOptions("JPG", 90, outDir, true, 0), cts.Token).GetAwaiter().GetResult();

        AssertTrue(tCancelled.Status == PoliceImageToolkit.Models.TaskStatus.Failed, "取消任務應標記為失敗/已取消");
        AssertTrue(string.IsNullOrEmpty(tCancelled.OutputPath), "取消任務不可寫入 OutputPath");
        AssertEqual(2, Directory.GetFiles(outDir).Length, "取消後不可留下多餘輸出殘檔");
    }
    finally
    {
        CleanupTempDir(directory);
    }
}

static void ImageConversion_RejectsAnimatedGifAndUnsupportedTargetFormat()
{
    string directory = CreateTempDir();
    try
    {
        Directory.CreateDirectory(directory);
        string gifPath = Path.Combine(directory, "anim.gif");
        var gifEncoder = new GifBitmapEncoder();
        gifEncoder.Frames.Add(BitmapFrame.Create(CreateTestBitmap(8, 8)));
        gifEncoder.Frames.Add(BitmapFrame.Create(CreateTestBitmap(8, 8)));
        using (var fs = new FileStream(gifPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            gifEncoder.Save(fs);
        }

        string outDir = Path.Combine(directory, "Out");
        var service = new ImageService();
        var gifTask = CreateImageTask(gifPath);
        service.ConvertAsync(gifTask, new ImageConvertOptions("PNG", 90, outDir, true, 0)).GetAwaiter().GetResult();
        AssertTrue(gifTask.Status == PoliceImageToolkit.Models.TaskStatus.Failed, "多影格 GIF 應拒絕轉檔");
        AssertTrue(gifTask.StatusMessage.Contains("動畫 GIF", StringComparison.Ordinal), "錯誤訊息應明確說明不支援動畫 GIF");

        string pngPath = Path.Combine(directory, "single.png");
        SaveTestBitmap(pngPath, new PngBitmapEncoder());
        var badFormatTask = CreateImageTask(pngPath);
        service.ConvertAsync(badFormatTask, new ImageConvertOptions("XYZ", 90, outDir, true, 0)).GetAwaiter().GetResult();
        AssertTrue(badFormatTask.Status == PoliceImageToolkit.Models.TaskStatus.Failed, "不支援之目標格式應失敗");
    }
    finally
    {
        CleanupTempDir(directory);
    }
}

static void ImageConverterViewModel_ExpandsFoldersDeduplicatesAndResetsTasks()
{
    string directory = CreateTempDir();
    try
    {
        string subDir = Path.Combine(directory, "Sub");
        Directory.CreateDirectory(subDir);
        string img1 = Path.Combine(directory, "a.png");
        string img2 = Path.Combine(subDir, "b.png");
        SaveTestBitmap(img1, new PngBitmapEncoder());
        SaveTestBitmap(img2, new PngBitmapEncoder());

        var vm = new ImageConverterViewModel(new ImageService(), new OutputIndexService())
        {
            OutputDirectory = Path.Combine(directory, "Out"),
            UseCustomOutputDirectory = true,
            TargetFormat = "JPG",
            MaxDimension = 0
        };

        int addedFirst = vm.AddFiles([directory]);
        int addedDup = vm.AddFiles([img1, img2]);
        AssertEqual(2, addedFirst, "資料夾遞迴展開圖片數");
        AssertEqual(0, addedDup, "重複加入應被去重");

        vm.StartConvertAsync().GetAwaiter().GetResult();
        AssertTrue(vm.Tasks.All(t => t.Status == PoliceImageToolkit.Models.TaskStatus.Success), "全部轉檔應成功");

        vm.ResetTasksForReconvert();
        vm.TargetFormat = "PNG";
        vm.StartConvertAsync().GetAwaiter().GetResult();
        AssertTrue(vm.Tasks.All(t => t.Status == PoliceImageToolkit.Models.TaskStatus.Success && t.OutputPath.EndsWith(".png", StringComparison.OrdinalIgnoreCase)), "重設後應能依新格式重轉");
    }
    finally
    {
        CleanupTempDir(directory);
    }
}

static void VideoSnapshotViewModel_LocksCaptureUntilMediaOpenedAndSupportsUndo()
{
    string directory = CreateTempDir();
    try
    {
        Directory.CreateDirectory(directory);
        string fakeVideo = Path.Combine(directory, "clip.mp4");
        File.WriteAllBytes(fakeVideo, [1, 2, 3]);

        var vm = new VideoSnapshotViewModel(new VideoService(), new OutputIndexService())
        {
            OutputDirectory = directory
        };
        vm.RequestCurrentFrame += () => CreateTestBitmap(16, 16);

        vm.LoadVideo(fakeVideo);
        AssertTrue(!vm.HasVideoLoaded, "MediaOpened 觸發前 HasVideoLoaded 必須為 false");
        AssertTrue(!vm.CaptureSnapshotCommand.CanExecute(null), "影片尚未開啟完成前不可截圖");

        vm.MarkVideoOpened(TimeSpan.FromSeconds(10));
        AssertTrue(vm.HasVideoLoaded, "MarkVideoOpened 後 HasVideoLoaded 應為 true");

        var snap = vm.CaptureSnapshotAsync().GetAwaiter().GetResult();
        AssertTrue(snap != null && File.Exists(snap.FilePath), "截圖應成功寫出檔案");
        string indexPath = Path.Combine(Path.GetDirectoryName(snap!.FilePath)!, "report_index.json");
        AssertTrue(File.Exists(indexPath), "應建立 report_index.json");

        vm.ExecuteUndoLastCaptureAsync().GetAwaiter().GetResult();
        AssertTrue(!File.Exists(snap.FilePath), "復原後應刪除截圖檔");
        AssertTrue(!File.ReadAllText(indexPath).Contains(Path.GetFileName(snap.FilePath), StringComparison.Ordinal), "復原後索引應移除對應項目");
    }
    finally
    {
        CleanupTempDir(directory);
    }
}

static void UpdateService_ComparesSemanticVersionsAndValidatesHttpsUrls()
{
    AssertTrue(UpdateService.CompareVersions("v11.5.0", "v11.4.0") > 0, "新版應大於舊版");
    AssertTrue(UpdateService.CompareVersions("v11.5.0-beta", "11.5.0") == 0, "預發布後綴應採相同核心版本");
    AssertTrue(UpdateService.CompareVersions("v11.1.9", "v11.5.0") < 0, "舊版應小於新版");

    AssertTrue(UpdateService.IsSafeExternalUrl("https://github.com/lianghao02/Police-Image-Toolkit/releases"), "HTTPS 應允許");
    AssertTrue(!UpdateService.IsSafeExternalUrl("http://github.com/test"), "HTTP 不安全連線應拒絕");
    AssertTrue(!UpdateService.IsSafeExternalUrl("file:///C:/Windows/System32/cmd.exe"), "本機 file:// 協定應拒絕");
}

static void UserSettingsService_RoundTripsAndHandlesCorruptedFile()
{
    string directory = CreateTempDir();
    try
    {
        string settingsPath = Path.Combine(directory, "settings.json");
        var service = new UserSettingsService(settingsPath);
        var saved = new UserPreferences
        {
            ImageTargetFormat = "PNG",
            ImageJpgQuality = 88,
            ImageAutoOrient = false,
            ImageMaxDimension = 1920,
            VideoOutputFormat = "JPG",
            VideoJpgQuality = 92,
            VideoAddTimestampOverlay = true,
            VideoIncludeMilliseconds = false
        };

        service.Save(saved);
        var loaded = service.Load();
        AssertEqual("PNG", loaded.ImageTargetFormat, "圖片目標格式設定");
        AssertEqual(1920, loaded.ImageMaxDimension, "圖片最大尺寸設定");
        AssertEqual("JPG", loaded.VideoOutputFormat, "影片格式設定");
        AssertTrue(loaded.VideoAddTimestampOverlay && !loaded.VideoIncludeMilliseconds, "影片浮水印偏好設定");

        File.WriteAllText(settingsPath, "{broken-json");
        var fallback = service.Load();
        AssertEqual("JPG", fallback.ImageTargetFormat, "損毀設定檔應退回預設值");
    }
    finally
    {
        CleanupTempDir(directory);
    }
}

static string CreateTempDir() =>
    Path.Combine(Path.GetTempPath(), $"PoliceImageToolkit-{Guid.NewGuid():N}");

static void CleanupTempDir(string directory)
{
    if (Directory.Exists(directory)) Directory.Delete(directory, true);
}

static string ComputeSha256(string path) =>
    Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path)));

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
