using System.IO;
using System.Text;
using System.Text.Json;

namespace PoliceImageToolkit.Services;

/// <summary>
/// 僅保存工具介面偏好設定（格式、品質、開關狀態）；不儲存任何證物路徑、檔名或案號。
/// </summary>
public sealed record UserPreferences
{
    public string ImageTargetFormat { get; init; } = "JPG";
    public int ImageJpgQuality { get; init; } = 90;
    public bool ImageAutoOrient { get; init; } = true;
    public int ImageMaxDimension { get; init; } = 0;

    public string VideoOutputFormat { get; init; } = "PNG";
    public int VideoJpgQuality { get; init; } = 95;
    public bool VideoAddTimestampOverlay { get; init; } = false;
    public bool VideoIncludeMilliseconds { get; init; } = true;
}

public interface IUserSettingsService
{
    UserPreferences Load();
    void Save(UserPreferences preferences);
}

public sealed class UserSettingsService : IUserSettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _settingsFilePath;

    public UserSettingsService(string? settingsFilePath = null)
    {
        _settingsFilePath = string.IsNullOrWhiteSpace(settingsFilePath)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "PoliceImageToolkit",
                "settings.json")
            : settingsFilePath;
    }

    public UserPreferences Load()
    {
        try
        {
            if (!File.Exists(_settingsFilePath)) return new UserPreferences();
            string json = File.ReadAllText(_settingsFilePath, Encoding.UTF8);
            return JsonSerializer.Deserialize<UserPreferences>(json, JsonOptions) ?? new UserPreferences();
        }
        catch
        {
            return new UserPreferences();
        }
    }

    public void Save(UserPreferences preferences)
    {
        try
        {
            string? dir = Path.GetDirectoryName(_settingsFilePath);
            if (!string.IsNullOrWhiteSpace(dir)) Directory.CreateDirectory(dir);
            string json = JsonSerializer.Serialize(preferences, JsonOptions);
            File.WriteAllText(_settingsFilePath, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }
        catch
        {
            // 設定儲存失敗不影響主工具離線運作
        }
    }
}
