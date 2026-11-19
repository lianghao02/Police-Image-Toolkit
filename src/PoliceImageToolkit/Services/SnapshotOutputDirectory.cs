using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace PoliceImageToolkit.Services;

/// <summary>
/// 同名手機錄影依來源位置分開輸出。識別碼只用於資料夾分流，不是內容雜湊或鑑識證明。
/// 不寫入來源絕對路徑，也不建立或覆寫檔案。
/// </summary>
public static class SnapshotOutputDirectory
{
    public static string Resolve(string outputRoot, string videoPath)
    {
        string fullSource = Path.GetFullPath(videoPath);
        string identity = OperatingSystem.IsWindows() ? fullSource.ToUpperInvariant() : fullSource;
        string sourceId = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)))[..32].ToLowerInvariant();
        string name = Path.GetFileNameWithoutExtension(fullSource);
        if (name.Length > 80) name = name[..80];
        return Path.Combine(outputRoot, $"{name}_Snapshots_{sourceId}");
    }
}
