using PoliceImageToolkit.Models;
using PoliceImageToolkit.Services;
using PoliceImageToolkit.ViewModels;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

var tests = new (string Name, Action Execute)[]
{
    ("各段來源紀錄的失敗提示與重試隔離", VideoSegments_IndexRetryIsScoped),
    ("第二段保留上段對照且清單、設定與整批移除分開", VideoSegments_KeepComparisonAndSeparateWork),
    ("還原及清單清除只影響目前影片，對照可手動關閉", VideoSegments_RestoreAndClearAreScoped),
    ("清空介面後仍可重試尚未同步的來源紀錄", IndexRetry_SurvivesClearingLists),
    ("截圖回收可還原且拒絕覆寫新同名檔", VideoRecovery_RestoresWithoutOverwrite),
    ("換片期間存檔仍保留原來源且不污染新片預覽", VideoCapture_KeepsOriginalSourceAcrossLoad),
    ("刪除與截圖互斥且依物件移除清單", VideoDeletion_IsSerialized),
    ("取消後仍為成功輸出建立索引，批次設定固定", ImageBatch_CancelStillIndexesAndFreezesOptions),
    ("多頁 TIFF 不得只轉第一頁而回報成功", ImageConversion_RejectsMultipageTiff),
    ("影片截圖建立獨立資料夾並使用流水號", VideoSnapshot_UsesFolderAndSequentialNames),
    ("影片截圖支援時間戳浮水印渲染、JPG 格式與非法前綴字元過濾", VideoSnapshot_RendersTimestampOverlayAndSanitizesPrefix),
    ("輸出索引保留最小追溯資訊並可移除已刪除檔案", OutputIndex_WritesAndRemovesEntries),
    ("輸出索引遇到損毀或外來 JSON 時拒絕覆寫", OutputIndex_RejectsCorruptedOrForeignIndexWithoutOverwriting),
    ("圖片轉檔不覆寫來源 PNG 或 JPG", ImageConversion_CreatesNewFilesWithoutChangingSources),
    ("圖片轉檔自動校正 Exif 方向並依最大邊等比例縮放", ImageConversion_AutoOrientsExifAndScalesMaxDimension),
    ("圖片轉檔支援同名防碰撞且取消時不殘留檔案", ImageConversion_AvoidsNameCollisionAndCleansCancelledTempFile),
    ("圖片轉檔拒絕動畫 GIF 與不支援之目標格式", ImageConversion_RejectsAnimatedGifAndUnsupportedTargetFormat),
    ("圖片轉檔 ViewModel 支援資料夾遞迴展開、去重與重設重轉", ImageConverterViewModel_ExpandsFoldersDeduplicatesAndResetsTasks),
    ("影片截圖於載入前鎖定操作並支援安全移除", VideoSnapshotViewModel_LocksCaptureUntilMediaOpenedAndSupportsUndo),
    ("影片截圖支援 0.5 秒步進與未載入滑桿邊界歸零", VideoSnapshotViewModel_SupportsHalfSecondStepAndSliderBounds),
    ("影片截圖支援單張移除、對照檢視切換與頂底部對照條生成", VideoSnapshotViewModel_SupportsSingleItemRemovalAndComparisonInspection),
    ("截圖流水號不補空號且還原依時間序插回不蓋過最新對照", VideoSnapshot_DoesNotReuseDeletedSequenceAndRestoresInOrder),
    ("影片倍速切換與最後分頁偏好設定讀寫", VideoSnapshotViewModel_CyclesPlaybackSpeedAndSavesTabPreference),
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
        AssertEqual(directory, Path.GetDirectoryName(Path.GetDirectoryName(first.FilePath))!, "影片輸出應在選定主資料夾下");
        AssertTrue(Path.GetFileName(Path.GetDirectoryName(first.FilePath)!).StartsWith("sample_Snapshots_"), "資料夾保留來源名稱並附識別碼");
        AssertEqual(Path.GetDirectoryName(first.FilePath)!, Path.GetDirectoryName(second.FilePath)!, "同一來源持續使用同一資料夾");
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

        var vm = new VideoSnapshotViewModel(new VideoService(), new OutputIndexService(), deleteFile: File.Delete)
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

static void VideoSegments_IndexRetryIsScoped()
{
    string dir = CreateTempDir(); Directory.CreateDirectory(dir);
    try
    {
        string a = Path.Combine(dir, "a.mp4"), b = Path.Combine(dir, "b.mp4");
        File.WriteAllText(a, "a"); File.WriteAllText(b, "b");
        var service = new DelayedVideoService(dir); service.Complete();
        var index = new RecordingIndexService { FailAppend = true };
        var vm = new VideoSnapshotViewModel(service, index, deleteFile: File.Delete);
        vm.RequestCurrentFrame += () => CreateTestBitmap(16, 32);
        vm.LoadVideo(a); vm.MarkVideoOpened(TimeSpan.FromSeconds(5));
        vm.CaptureSnapshotAsync().GetAwaiter().GetResult();
        AssertTrue(vm.IndexMessage.Contains("失敗"), "第一段保留索引失敗提示");
        vm.LoadVideo(b);
        AssertEqual(string.Empty, vm.IndexMessage, "第二段不誤顯示第一段的索引結果");
        AssertTrue(!vm.RetryIndexCommand.CanExecute(null), "第二段不能操作第一段的索引重試");
        vm.LoadVideo(a);
        AssertTrue(vm.IndexMessage.Contains("失敗") && vm.RetryIndexCommand.CanExecute(null), "切回第一段找回失敗提示與重試");
        index.FailAppend = false;
        vm.RetrySnapshotIndexAsync().GetAwaiter().GetResult();
        AssertEqual("a.mp4", index.Entries.Single().SourceFile, "重試仍使用第一段原來源");
    }
    finally { CleanupTempDir(dir); }
}

static void VideoSegments_KeepComparisonAndSeparateWork()
{
    string dir = CreateTempDir(); Directory.CreateDirectory(dir);
    try
    {
        string a = Path.Combine(dir, "first.mp4"), b = Path.Combine(dir, "second.mp4");
        File.WriteAllText(a, "a"); File.WriteAllText(b, "b");
        var vm = new VideoSnapshotViewModel(new VideoService(), new OutputIndexService()) { OutputDirectory = dir };
        vm.RequestCurrentFrame += () => CreateTestBitmap(16, 32);
        vm.LoadVideo(a); vm.MarkVideoOpened(TimeSpan.FromSeconds(5));
        vm.CasePrefix = "first-case";
        var first = vm.CaptureSnapshotAsync().GetAwaiter().GetResult()!;
        vm.LoadVideo(b); vm.MarkVideoOpened(TimeSpan.FromSeconds(5));
        AssertEqual(0, vm.Snapshots.Count, "第二段不能混入第一段清單");
        AssertTrue(vm.IsPreviousSegmentComparison && vm.LastCapturedSnapshot == first, "第一張截圖前保留上段最後一張並清楚標示");
        AssertEqual(string.Empty, vm.CasePrefix, "新影片前綴不可誤用上一段");
        AssertEqual(dir, vm.OutputDirectory, "儲存主位置沿用並在介面明示");
        AssertTrue(!vm.UndoLastCaptureCommand.CanExecute(null), "第二段尚未截圖時 Z 必須停用");
        vm.ExecuteUndoLastCaptureAsync().GetAwaiter().GetResult();
        AssertTrue(File.Exists(first.FilePath), "不得移除上一段最後一張");
        vm.OutputDirectory = Path.Combine(dir, "other-root");
        var second = vm.CaptureSnapshotAsync().GetAwaiter().GetResult()!;
        AssertTrue(vm.LastCapturedSnapshot == second && !vm.IsPreviousSegmentComparison, "截取第二段後使用本段對照");
        AssertTrue(vm.Snapshots.Count == 1 && vm.Snapshots.Single().SourceFilePath == b, "數量只計目前影片");
        vm.MoveCurrentSnapshotsToRecoveryAsync().GetAwaiter().GetResult();
        AssertTrue(File.Exists(first.FilePath) && !File.Exists(second.FilePath), "整批移除只影響目前影片");
        AssertTrue(vm.IsPreviousSegmentComparison && vm.LastCapturedSnapshot == first, "本段截圖移除後仍能對照上一段");
        vm.RestoreLastRemovedAsync().GetAwaiter().GetResult();
        AssertTrue(File.Exists(second.FilePath) && vm.Snapshots.Count == 1, "第二段可獨立還原");
        vm.LoadVideo(a);
        AssertEqual(1, vm.Snapshots.Count, "切回第一段時找回原清單");
        AssertEqual("first-case", vm.CasePrefix, "切回第一段恢復自己的前綴");
        AssertEqual(dir, vm.OutputDirectory, "切回第一段恢復自己的儲存位置");
    }
    finally { CleanupTempDir(dir); }
}

static void VideoSegments_RestoreAndClearAreScoped()
{
    string dir = CreateTempDir(); Directory.CreateDirectory(dir);
    try
    {
        string a = Path.Combine(dir, "a.mp4"), b = Path.Combine(dir, "b.mp4");
        File.WriteAllText(a, "a"); File.WriteAllText(b, "b");
        var vm = new VideoSnapshotViewModel(new VideoService(), new OutputIndexService()) { OutputDirectory = dir };
        vm.RequestCurrentFrame += () => CreateTestBitmap(16, 32);
        vm.LoadVideo(a); vm.MarkVideoOpened(TimeSpan.FromSeconds(5));
        var first = vm.CaptureSnapshotAsync().GetAwaiter().GetResult()!;
        vm.LoadVideo(b); vm.MarkVideoOpened(TimeSpan.FromSeconds(5));
        vm.ClearPreviousComparisonCommand.Execute(null);
        AssertTrue(vm.LastCapturedSnapshot == null && File.Exists(first.FilePath), "關閉上段對照不刪除截圖");
        vm.LoadVideo(a); vm.MarkVideoOpened(TimeSpan.FromSeconds(5));
        vm.ExecuteUndoLastCaptureAsync().GetAwaiter().GetResult();
        vm.LoadVideo(b); vm.MarkVideoOpened(TimeSpan.FromSeconds(5));
        AssertTrue(!vm.RestoreLastRemovedCommand.CanExecute(null), "第二段不得還原第一段的回收檔");
        vm.RestoreLastRemovedAsync().GetAwaiter().GetResult();
        AssertTrue(!File.Exists(first.FilePath), "跨段還原必須不做任何檔案變更");
        var second = vm.CaptureSnapshotAsync().GetAwaiter().GetResult()!;
        vm.ClearSnapshotsCommand.Execute(null);
        AssertEqual(0, vm.Snapshots.Count, "只清空本段清單");
        AssertTrue(File.Exists(second.FilePath), "清空介面不影響磁碟檔案");
        vm.LoadVideo(a);
        AssertTrue(vm.RestoreLastRemovedCommand.CanExecute(null), "切回第一段仍能還原其回收檔");
        vm.RestoreLastRemovedAsync().GetAwaiter().GetResult();
        AssertTrue(File.Exists(first.FilePath) && vm.Snapshots.Count == 1, "原段清單恢復");
        vm.LoadVideo(b);
        AssertEqual(0, vm.Snapshots.Count, "其他影片的清單維持原狀");
    }
    finally { CleanupTempDir(dir); }
}

static void IndexRetry_SurvivesClearingLists()
{
    string dir = CreateTempDir(); Directory.CreateDirectory(dir);
    try
    {
        var index = new RecordingIndexService { FailAppend = true };
        var service = new DelayedVideoService(dir); service.Complete();
        var video = new VideoSnapshotViewModel(service, index, deleteFile: File.Delete);
        string source = Path.Combine(dir, "clip.mp4"); File.WriteAllText(source, "video");
        video.RequestCurrentFrame += () => CreateTestBitmap(8, 8);
        video.LoadVideo(source); video.MarkVideoOpened(TimeSpan.FromSeconds(3));
        video.CaptureSnapshotAsync().GetAwaiter().GetResult();
        video.ClearSnapshotsCommand.Execute(null);
        AssertTrue(video.RetryIndexCommand.CanExecute(null), "清單已清空仍應保留來源紀錄重試");
        index.FailAppend = false;
        video.RetrySnapshotIndexAsync().GetAwaiter().GetResult();
        AssertEqual("clip.mp4", index.Entries.Single().SourceFile, "必須保留原來源");

        index.Entries.Clear(); index.FailAppend = true;
        string picture = Path.Combine(dir, "picture.png"); SaveTestBitmap(picture, new PngBitmapEncoder());
        var image = new ImageConverterViewModel(new ImageService(), index);
        image.AddFiles([picture]); image.StartConvertAsync().GetAwaiter().GetResult();
        image.ClearAllCommand.Execute(null);
        AssertTrue(image.RetryIndexCommand.CanExecute(null), "圖片清單已清空仍能重試來源紀錄");
        index.FailAppend = false; image.RetryIndexCommand.Execute(null);
        AssertEqual("picture.png", index.Entries.Single().SourceFile, "重試保留圖片來源");
    }
    finally { CleanupTempDir(dir); }
}

static void VideoRecovery_RestoresWithoutOverwrite()
{
    string dir = CreateTempDir(); Directory.CreateDirectory(dir);
    try
    {
        var vm = new VideoSnapshotViewModel(new VideoService(), new RecordingIndexService());
        string path = Path.Combine(dir, "001.png"); File.WriteAllText(path, "original snapshot");
        string source = Path.Combine(dir, "clip.mp4"); File.WriteAllText(source, "video");
        vm.LoadVideo(source);
        vm.Snapshots.Add(new SnapshotResult(path, TimeSpan.Zero, 8, 8, 1, SourceFilePath: source));
        vm.ExecuteUndoLastCaptureAsync().GetAwaiter().GetResult();
        AssertTrue(!File.Exists(path) && vm.RestoreLastRemovedCommand.CanExecute(null), "移除後應可還原");
        File.WriteAllText(path, "new snapshot");
        vm.RestoreLastRemovedAsync().GetAwaiter().GetResult();
        AssertEqual("new snapshot", File.ReadAllText(path), "還原不得覆寫新同名檔");
        AssertTrue(vm.RestoreLastRemovedCommand.CanExecute(null), "撞名後仍應保留回收檔");
        File.Delete(path);
        vm.RestoreLastRemovedAsync().GetAwaiter().GetResult();
        AssertEqual("original snapshot", File.ReadAllText(path), "應還原原截圖內容");
        AssertTrue(!vm.RestoreLastRemovedCommand.CanExecute(null) && vm.Snapshots.Count == 1, "還原後更新清單與按鈕狀態");
    }
    finally { CleanupTempDir(dir); }
}

static void VideoCapture_KeepsOriginalSourceAcrossLoad()
{
    string dir = CreateTempDir();
    Directory.CreateDirectory(dir);
    try
    {
        string a = Path.Combine(dir, "source-a.mp4"), b = Path.Combine(dir, "source-b.mp4");
        File.WriteAllText(a, "a"); File.WriteAllText(b, "b");
        var service = new DelayedVideoService(dir);
        var index = new RecordingIndexService();
        var vm = new VideoSnapshotViewModel(service, index, deleteFile: File.Delete);
        vm.RequestCurrentFrame += () => CreateTestBitmap(16, 16);
        int feedbackCount = 0;
        vm.RequestSnapshotVisualFeedback += () => feedbackCount++;
        vm.LoadVideo(a); vm.MarkVideoOpened(TimeSpan.FromSeconds(10));
        var capture = vm.CaptureSnapshotAsync();
        AssertEqual(0, feedbackCount, "存檔完成前不可回報成功");
        vm.LoadVideo(b); vm.MarkVideoOpened(TimeSpan.FromSeconds(10));
        service.Complete(); capture.GetAwaiter().GetResult();
        AssertEqual("source-a.mp4", index.Entries.Single().SourceFile, "索引來源必須是截圖時的影片");
        AssertTrue(vm.LastCapturedSnapshot == null, "換片後不能出現上一部影片的對照圖");
        AssertEqual(0, feedbackCount, "舊工作不能在新影片顯示成功閃光");
        AssertEqual(0, vm.Snapshots.Count, "舊影片背景完成不得加入目前影片清單");
        vm.LoadVideo(a);
        AssertTrue(vm.Snapshots.Single().SourceFilePath == a, "切回原影片可查看背景完成的截圖");
    }
    finally { CleanupTempDir(dir); }
}

static void VideoDeletion_IsSerialized()
{
    string dir = CreateTempDir(); Directory.CreateDirectory(dir);
    try
    {
        var index = new RecordingIndexService { DelayRemoval = true };
        var vm = new VideoSnapshotViewModel(new VideoService(), index, deleteFile: File.Delete);
        string file = Path.Combine(dir, "001.png"); File.WriteAllText(file, "snapshot");
        vm.Snapshots.Add(new SnapshotResult(file, TimeSpan.Zero, 8, 8, 1));
        var deleting = vm.ExecuteUndoLastCaptureAsync();
        AssertTrue(vm.IsBusy, "索引同步完成前維持忙碌狀態");
        AssertTrue(!vm.ClearSnapshotsCommand.CanExecute(null), "刪除中不得清空清單");
        AssertTrue(!vm.UndoLastCaptureCommand.CanExecute(null), "刪除中不得再次刪除");
        string another = Path.Combine(dir, "another.mp4"); File.WriteAllText(another, "video");
        vm.LoadVideo(another);
        AssertEqual(string.Empty, vm.VideoPath, "移除紀錄同步中不得換片而改變操作範圍");
        vm.ExecuteUndoLastCaptureAsync().GetAwaiter().GetResult();
        index.Removal.SetResult(); deleting.GetAwaiter().GetResult();
        AssertEqual(0, vm.Snapshots.Count, "不得移除其他項目或超出索引");
        AssertTrue(!vm.IsBusy, "刪除完成後恢復操作");
    }
    finally { CleanupTempDir(dir); }
}

static void ImageBatch_CancelStillIndexesAndFreezesOptions()
{
    string dir = CreateTempDir(); Directory.CreateDirectory(dir);
    try
    {
        var image = new ControlledImageService();
        var index = new RecordingIndexService();
        var vm = new ImageConverterViewModel(image, index) { TargetFormat = "JPG", JpgQuality = 90 };
        for (int i = 0; i < 6; i++)
        {
            string path = Path.Combine(dir, $"{i}.png"); File.WriteAllText(path, "source");
            vm.AddFiles([path]);
        }
        var batch = vm.StartConvertAsync();
        image.Started.Task.GetAwaiter().GetResult();
        vm.TargetFormat = "PNG"; vm.JpgQuality = 10;
        image.Release.SetResult();
        image.SecondWave.Task.GetAwaiter().GetResult();
        vm.CancelConvertCommand.Execute(null);
        batch.GetAwaiter().GetResult();
        AssertTrue(index.Entries.Count > 0, "取消後仍必須補齊成功輸出的索引");
        AssertTrue(image.Options.All(x => x.TargetFormat == "JPG" && x.JpgQuality == 90), "整批必須使用開始時的設定");
        AssertTrue(!vm.IsProcessing && vm.CanEditSettings, "取消後設定與操作恢復");
    }
    finally { CleanupTempDir(dir); }
}

static void ImageConversion_RejectsMultipageTiff()
{
    string dir = CreateTempDir(); Directory.CreateDirectory(dir);
    try
    {
        string path = Path.Combine(dir, "pages.tiff");
        var encoder = new TiffBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(CreateTestBitmap(8, 8)));
        encoder.Frames.Add(BitmapFrame.Create(CreateTestBitmap(8, 8)));
        using (var fs = File.Create(path)) encoder.Save(fs);
        var item = CreateImageTask(path);
        new ImageService().ConvertAsync(item, new ImageConvertOptions("PNG", 90, Path.Combine(dir, "out"), true, 0)).GetAwaiter().GetResult();
        AssertTrue(item.Status == PoliceImageToolkit.Models.TaskStatus.Failed && string.IsNullOrEmpty(item.OutputPath), "多頁 TIFF 必須明確拒絕且不產生不完整輸出");
    }
    finally { CleanupTempDir(dir); }
}

static void VideoSnapshotViewModel_SupportsHalfSecondStepAndSliderBounds()
{
    string dir = CreateTempDir(); Directory.CreateDirectory(dir);
    try
    {
        string videoPath = Path.Combine(dir, "clip.mp4");
        File.WriteAllText(videoPath, "video");
        var vm = new VideoSnapshotViewModel(new VideoService(), new RecordingIndexService());

        AssertEqual(1.0, vm.SliderMaximum, "未載入影片時 SliderMaximum 應為 1.0 避免滿條");
        AssertEqual(0.0, vm.CurrentPositionSeconds, "未載入影片時滑桿位置應歸零");
        AssertTrue(!vm.StepForwardHalfCommand.CanExecute(null) && !vm.StepBackwardHalfCommand.CanExecute(null), "未載入影片時 0.5 秒步進應停用");

        var seeked = new List<TimeSpan>();
        vm.RequestSeek += ts =>
        {
            seeked.Add(ts);
            vm.CurrentPosition = ts;
        };

        vm.LoadVideo(videoPath);
        vm.MarkVideoOpened(TimeSpan.FromSeconds(1.2));
        AssertEqual(1.2, vm.SliderMaximum, "載入影片後 SliderMaximum 應等於片長");

        vm.StepForwardHalfCommand.Execute(null);
        AssertEqual(0.5, vm.CurrentPosition.TotalSeconds, "+0.5 秒步進位置");

        vm.StepForwardHalfCommand.Execute(null);
        vm.StepForwardHalfCommand.Execute(null);
        AssertEqual(1.2, vm.CurrentPosition.TotalSeconds, "+0.5 秒超出片尾應限制於片長");

        vm.StepBackwardHalfCommand.Execute(null);
        AssertTrue(Math.Abs(vm.CurrentPosition.TotalSeconds - 0.7) < 0.001, "-0.5 秒步進位置");

        vm.StepBackwardHalfCommand.Execute(null);
        vm.StepBackwardHalfCommand.Execute(null);
        AssertEqual(0.0, vm.CurrentPosition.TotalSeconds, "-0.5 秒超出片頭應限制於 0 秒");
    }
    finally { CleanupTempDir(dir); }
}

static void VideoSnapshotViewModel_SupportsSingleItemRemovalAndComparisonInspection()
{
    string dir = CreateTempDir(); Directory.CreateDirectory(dir);
    try
    {
        string videoPath = Path.Combine(dir, "chat.mp4");
        File.WriteAllText(videoPath, "video");
        var index = new RecordingIndexService();
        var vm = new VideoSnapshotViewModel(new VideoService(), index) { OutputDirectory = dir };
        vm.RequestCurrentFrame += () => CreateTestBitmap(360, 600);
        vm.LoadVideo(videoPath);
        vm.MarkVideoOpened(TimeSpan.FromSeconds(10));

        var first = vm.CaptureSnapshotAsync().GetAwaiter().GetResult()!;
        var second = vm.CaptureSnapshotAsync().GetAwaiter().GetResult()!;
        var third = vm.CaptureSnapshotAsync().GetAwaiter().GetResult()!;

        AssertTrue(first.TopStrip == null && first.BottomStrip == null, "清單項目不常駐大型 Strip 位元圖以節省記憶體");
        var topStrip = VideoService.CreateComparisonStrip(CreateTestBitmap(360, 600), top: true);
        var bottomStrip = VideoService.CreateComparisonStrip(CreateTestBitmap(360, 600), top: false);
        AssertEqual(200, topStrip.PixelHeight, "頂部對照條應為高度 1/3");
        AssertEqual(200, bottomStrip.PixelHeight, "底部對照條應為高度 1/3");

        vm.SelectedSnapshot = first;
        AssertTrue(vm.IsInspectingSelectedSnapshot && vm.ActiveComparisonSnapshot == first, "選取清單項目應切換預覽目標");
        AssertTrue(vm.LastCapturedSnapshot == third, "選取檢視不應覆寫最新截圖參照");

        vm.ComparisonViewMode = "Full";
        AssertEqual(600, vm.ActiveComparisonImage!.PixelHeight, "整張模式應載入原高預覽");
        vm.ComparisonViewMode = "Top";
        AssertEqual(200, vm.ActiveComparisonImage!.PixelHeight, "頂部模式應按需裁切頂部 1/3");
        vm.ComparisonViewMode = "Bottom";
        AssertEqual(200, vm.ActiveComparisonImage!.PixelHeight, "底部模式應按需裁切底部 1/3");

        vm.RemoveSingleSnapshotCommand.Execute(second);
        AssertEqual(2, vm.Snapshots.Count, "單張移除應精準移除指定項目");
        AssertTrue(!File.Exists(second.FilePath) && File.Exists(first.FilePath) && File.Exists(third.FilePath), "單張移除不可影響其他截圖");
        AssertTrue(vm.RestoreLastRemovedCommand.CanExecute(null), "單張移除後應可還原");

        vm.RestoreLastRemovedAsync().GetAwaiter().GetResult();
        AssertTrue(File.Exists(second.FilePath) && vm.Snapshots.Count == 3, "單張移除項目應能正常還原");
    }
    finally { CleanupTempDir(dir); }
}

static void VideoSnapshot_DoesNotReuseDeletedSequenceAndRestoresInOrder()
{
    string dir = CreateTempDir(); Directory.CreateDirectory(dir);
    try
    {
        string videoPath = Path.Combine(dir, "seq.mp4");
        File.WriteAllText(videoPath, "video");
        var vm = new VideoSnapshotViewModel(new VideoService(), new RecordingIndexService()) { OutputDirectory = dir };
        vm.RequestCurrentFrame += () => CreateTestBitmap(160, 300);
        TimeSpan mockPos = TimeSpan.FromSeconds(1);
        vm.RequestCurrentPosition += () => mockPos;

        vm.LoadVideo(videoPath);
        vm.MarkVideoOpened(TimeSpan.FromSeconds(20));

        mockPos = TimeSpan.FromSeconds(1);
        var s1 = vm.CaptureSnapshotAsync().GetAwaiter().GetResult()!;
        mockPos = TimeSpan.FromSeconds(3);
        var s2 = vm.CaptureSnapshotAsync().GetAwaiter().GetResult()!;
        mockPos = TimeSpan.FromSeconds(5);
        var s3 = vm.CaptureSnapshotAsync().GetAwaiter().GetResult()!;

        AssertEqual("001.png", Path.GetFileName(s1.FilePath), "第 1 張檔名");
        AssertEqual("002.png", Path.GetFileName(s2.FilePath), "第 2 張檔名");
        AssertEqual("003.png", Path.GetFileName(s3.FilePath), "第 3 張檔名");

        vm.RemoveSingleSnapshotCommand.Execute(s2);
        AssertTrue(!File.Exists(s2.FilePath) && vm.HasRecoveryFiles, "移除後應移入暫存回收區");

        mockPos = TimeSpan.FromSeconds(7);
        var s4 = vm.CaptureSnapshotAsync().GetAwaiter().GetResult()!;
        AssertEqual("004.png", Path.GetFileName(s4.FilePath), "新截圖不得補佔已移至回收區的 002 空號");

        vm.RestoreLastRemovedAsync().GetAwaiter().GetResult();
        AssertTrue(File.Exists(s2.FilePath), "002 應可無衝突還原");
        AssertEqual("004.png", Path.GetFileName(vm.Snapshots[0].FilePath), "還原舊圖不得蓋掉最新一張作為對照參照");
        AssertEqual("003.png", Path.GetFileName(vm.Snapshots[1].FilePath), "清單應保持由新到舊順序");
        AssertEqual("002.png", Path.GetFileName(vm.Snapshots[2].FilePath), "002 應插回 003 與 001 之間");
        AssertEqual("001.png", Path.GetFileName(vm.Snapshots[3].FilePath), "最舊張應在最末位");
        AssertTrue(vm.LastCapturedSnapshot == s4, "LastCapturedSnapshot 應維持最新 004.png");
    }
    finally { CleanupTempDir(dir); }
}

static void VideoSnapshotViewModel_CyclesPlaybackSpeedAndSavesTabPreference()
{
    string dir = CreateTempDir(); Directory.CreateDirectory(dir);
    try
    {
        string videoPath = Path.Combine(dir, "speed.mp4");
        File.WriteAllText(videoPath, "video");
        var vm = new VideoSnapshotViewModel(new VideoService(), new RecordingIndexService());
        var speeds = new List<double>();
        vm.RequestSpeedChange += s => speeds.Add(s);

        vm.LoadVideo(videoPath);
        vm.MarkVideoOpened(TimeSpan.FromSeconds(10));
        AssertEqual("1x", vm.PlaybackSpeedDisplay, "預設倍速顯示");
        vm.CyclePlaybackSpeedCommand.Execute(null);
        AssertEqual("1.5x", vm.PlaybackSpeedDisplay, "第一次切換應為 1.5x");
        vm.CyclePlaybackSpeedCommand.Execute(null);
        AssertEqual("2x", vm.PlaybackSpeedDisplay, "第二次切換應為 2x");
        vm.CyclePlaybackSpeedCommand.Execute(null);
        AssertEqual("1x", vm.PlaybackSpeedDisplay, "第三次切換應回到 1x");
        AssertEqual(3, speeds.Count, "每次切換應觸發倍速事件");

        string settingsPath = Path.Combine(dir, "settings.json");
        var settings = new UserSettingsService(settingsPath);
        settings.Save(new UserPreferences { LastTabIndex = 1 });
        AssertEqual(1, settings.Load().LastTabIndex, "LastTabIndex 應正常保存與載入");
    }
    finally { CleanupTempDir(dir); }
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

sealed class RecordingIndexService : IOutputIndexService
{
    public List<OutputIndexEntry> Entries { get; } = [];
    public bool DelayRemoval { get; init; }
    public bool FailAppend { get; set; }
    public TaskCompletionSource Removal { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public Task AppendEntriesAsync(string directory, IEnumerable<OutputIndexEntry> entries, CancellationToken cancellationToken = default)
    { if (FailAppend) throw new IOException("simulated index failure"); Entries.AddRange(entries); return Task.CompletedTask; }
    public Task RemoveEntriesAsync(string directory, IEnumerable<string> names, CancellationToken cancellationToken = default) => DelayRemoval ? Removal.Task : Task.CompletedTask;
}
sealed class DelayedVideoService(string directory) : IVideoService
{
    private readonly TaskCompletionSource<SnapshotResult> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public Task<SnapshotResult> SaveFrameSnapshotAsync(BitmapSource frame, TimeSpan position, string path, VideoSnapshotConfig config) => _completion.Task;
    public void Complete()
    {
        string path = Path.Combine(directory, "001.png"); File.WriteAllText(path, "snapshot");
        _completion.SetResult(new SnapshotResult(path, TimeSpan.Zero, 16, 16, 1));
    }
    public BitmapSource RenderFrameWithTimestamp(BitmapSource frame, TimeSpan timestamp, string title, bool includeMs) => frame;
}
sealed class ControlledImageService : IImageService
{
    public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource SecondWave { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public System.Collections.Concurrent.ConcurrentBag<ImageConvertOptions> Options { get; } = [];
    private int _count;
    public async Task ConvertAsync(ImageTaskItem item, ImageConvertOptions options, CancellationToken ct = default)
    {
        Options.Add(options);
        if (Interlocked.Increment(ref _count) == 1)
        {
            Started.TrySetResult(); await Release.Task;
            Directory.CreateDirectory(options.OutputDirectory);
            item.OutputPath = Path.Combine(options.OutputDirectory, "completed.jpg");
            File.WriteAllText(item.OutputPath, "output");
            item.Status = PoliceImageToolkit.Models.TaskStatus.Success;
        }
        else { SecondWave.TrySetResult(); await Task.Delay(Timeout.Infinite, ct); }
    }
}
