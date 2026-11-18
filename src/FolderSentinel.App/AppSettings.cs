using System.IO;
using System.Text.Json;
using FolderSentinel.Core;

namespace FolderSentinel.App;

/// <summary>
/// 程序设置（目前只有“界面语言”一项），存放在数据目录的 settings.json 里。
/// 读取出错时安静地用默认值——启动流程绝不能被一个坏掉的配置文件卡住。
/// </summary>
public sealed class AppSettings
{
    /// <summary>界面语言代码："zh-CN" 或 "en-US"。</summary>
    public string Language { get; set; } = "zh-CN";

    /// <summary>设置文件位置：数据目录\data\settings.json。</summary>
    private static string SettingsFilePath =>
        Path.Combine(SelfGuard.CreateDefault().DataDirectory, "settings.json");

    /// <summary>读取设置；文件不存在或损坏时返回默认值。</summary>
    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsFilePath))
            {
                var json = File.ReadAllText(SettingsFilePath);
                return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
            }
        }
        catch
        {
            // 配置坏了就用默认值，不影响启动
        }
        return new AppSettings();
    }

    /// <summary>保存设置到 settings.json。</summary>
    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsFilePath)!);
            var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(SettingsFilePath, json);
        }
        catch
        {
            // 保存失败不影响运行，只是下次启动会回到默认语言
        }
    }
}
