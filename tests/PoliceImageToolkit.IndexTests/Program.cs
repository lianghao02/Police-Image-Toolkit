using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using PoliceImageToolkit.Models;
using PoliceImageToolkit.Services;

if (args.Length > 0 && args[0] == "--writer")
{
    while (!File.Exists(Path.Combine(args[1], "start"))) await Task.Delay(10);
    for (int i = 0; i < 20; i++)
        await new OutputIndexService().AppendEntriesAsync(args[1], [Entry($"{args[2]}-{i}.png")]);
    return 0;
}
var tests = new (string Name, Func<string, Task> Run)[]
{
    ("同名錄影分開輸出且同來源路徑穩定", SourceDirectories),
    ("寫入、去重、移除及來源路徑最小化", RoundTrip),
    ("損毀、外來、缺少 schema 與未支援版本均保留原檔", RejectInvalid),
    ("四個獨立程序寫入同一索引不遺失紀錄", CrossProcess),
    ("等待跨程序鎖時可取消且不覆寫索引", CancelLock)
};
int failed = 0;
foreach (var test in tests)
{
    string dir = Path.Combine(Path.GetTempPath(), $"pit-index-{Guid.NewGuid():N}"); Directory.CreateDirectory(dir);
    try { await test.Run(dir); Console.WriteLine($"PASS: {test.Name}"); }
    catch (Exception ex) { failed++; Console.Error.WriteLine($"FAIL: {test.Name}: {ex}"); }
    finally { Directory.Delete(dir, true); }
}
Console.WriteLine($"完成：{tests.Length - failed}/{tests.Length} 項測試通過。");
return failed == 0 ? 0 : 1;

static OutputIndexEntry Entry(string name) => new(name, "source.mp4", "video_snapshot", "00:00:01.000", 1920, 1080);
static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
static async Task RoundTrip(string dir)
{
    var service = new OutputIndexService();
    await service.AppendEntriesAsync(dir, [Entry("001.png") with { SourceFile = Path.Combine(dir, "private", "source.mp4") }]);
    await service.AppendEntriesAsync(dir, [Entry("001.png"), Entry("002.png")]);
    Check(!File.Exists(Path.Combine(dir, ".report_index.lock")), "輸出資料夾不應產生 .report_index.lock");
    string path = Path.Combine(dir, "report_index.json");
    string json = File.ReadAllText(path);
    Check(!json.Contains("private"), "不應持久化絕對路徑");
    using (var document = JsonDocument.Parse(json)) Check(document.RootElement.GetProperty("entries").GetArrayLength() == 2, "重複輸出不得重複建檔");
    await service.RemoveEntriesAsync(dir, ["001.png"]);
    Check(!File.ReadAllText(path).Contains("001.png"), "刪除後應移除來源紀錄");
}
static async Task RejectInvalid(string dir)
{
    string path = Path.Combine(dir, "report_index.json");
    foreach (string json in new[] { "{broken", "{}", "{\"schema\":\"other\",\"schema_version\":1}", "{\"schema\":\"police-image-toolkit-report-index\",\"schema_version\":99}" })
    {
        File.WriteAllText(path, json);
        bool rejected = false;
        try { await new OutputIndexService().AppendEntriesAsync(dir, [Entry("001.png")]); }
        catch (InvalidDataException) { rejected = true; }
        Check(rejected && File.ReadAllText(path) == json, "必須拒絕並保留既有檔案");
    }
}
static async Task CrossProcess(string dir)
{
    var processes = new List<Process>();
    try
    {
        for (int i = 0; i < 4; i++)
        {
            var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false };
            if (Path.GetFileNameWithoutExtension(Environment.ProcessPath) == "dotnet") start.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
            foreach (string arg in new[] { "--writer", dir, i.ToString() }) start.ArgumentList.Add(arg);
            processes.Add(Process.Start(start)!);
        }
        File.WriteAllText(Path.Combine(dir, "start"), "go");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        await Task.WhenAll(processes.Select(p => p.WaitForExitAsync(timeout.Token)));
        Check(processes.All(p => p.ExitCode == 0), "每個程序都必須正常完成");
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(dir, "report_index.json")));
        var entries = document.RootElement.GetProperty("entries").EnumerateArray().ToList();
        Check(entries.Count == 80 && entries.Select(x => x.GetProperty("output_file").GetString()).Distinct().Count() == 80, "跨程序不得遺失更新");
    }
    finally
    {
        foreach (var process in processes) { if (!process.HasExited) process.Kill(true); process.Dispose(); }
    }
}
static async Task CancelLock(string dir)
{
    var service = new OutputIndexService(); await service.AppendEntriesAsync(dir, [Entry("original.png")]);
    string path = Path.Combine(dir, "report_index.json"), before = File.ReadAllText(path);
    string lockPath = OutputIndexService.GetLockFilePath(dir);
    Directory.CreateDirectory(Path.GetDirectoryName(lockPath)!);
    using var held = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    using var cancel = new CancellationTokenSource(150);
    bool cancelled = false;
    try { await service.AppendEntriesAsync(dir, [Entry("new.png")], cancel.Token); }
    catch (OperationCanceledException) { cancelled = true; }
    Check(cancelled && File.ReadAllText(path) == before, "取消等待不得改寫原索引");
}

static Task SourceDirectories(string dir)
{
    string a = Path.Combine(dir, "phone-a", "recording.mp4");
    string b = Path.Combine(dir, "phone-b", "recording.mp4");
    string first = SnapshotOutputDirectory.Resolve(dir, a);
    string second = SnapshotOutputDirectory.Resolve(dir, b);
    Check(first != second, "不同來源的同名影片不得混入同一資料夾");
    Check(first == SnapshotOutputDirectory.Resolve(dir, a), "同來源重新開啟應使用相同資料夾");
    Check(first == SnapshotOutputDirectory.Resolve(dir, Path.Combine(dir, "phone-a", "..", "phone-a", "recording.mp4")), "等價路徑不得分成不同資料夾");
    Check(Path.GetDirectoryName(first) == dir, "不得離開選定儲存主位置");
    Check(!Directory.Exists(first), "計算儲存位置不得建立或改寫資料夾");
    return Task.CompletedTask;
}
