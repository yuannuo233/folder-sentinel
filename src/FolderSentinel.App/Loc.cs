namespace FolderSentinel.App;

/// <summary>
/// 界面文字的“总翻译官”。
///
/// 项目约定（所有界面文字都必须遵守）：
/// 1. 界面上任何文字都不允许直接写在代码或 XAML 里，必须通过 Loc.T("键名") 从字典里取；
/// 2. 支持简体中文（zh-CN）和英文（en-US）两套完整文案，运行时可切换；
/// 3. 切换语言会触发 LanguageChanged 事件，主窗口收到后把所有文字重新设置一遍；
/// 4. 用户选择的语言保存在 data\settings.json 里，下次启动自动恢复。
/// </summary>
public static class Loc
{
    /// <summary>当前语言代码（"zh-CN" 或 "en-US"）。</summary>
    public static string CurrentLanguage { get; private set; } = "zh-CN";

    /// <summary>语言切换完成后触发，主窗口订阅它来刷新全部界面文字。</summary>
    public static event Action? LanguageChanged;

    /// <summary>
    /// 切换语言。不认识的语言代码会回退到中文。
    /// </summary>
    public static void SetLanguage(string languageCode)
    {
        if (languageCode != "zh-CN" && languageCode != "en-US")
        {
            languageCode = "zh-CN";
        }
        if (CurrentLanguage == languageCode)
        {
            return;   // 没变化就不用动（程序启动时设一次不会重复刷新界面）
        }
        CurrentLanguage = languageCode;
        LanguageChanged?.Invoke();
    }

    /// <summary>
    /// 取一条界面文字。键不存在时先回退到中文，再不行就显示键名本身
    /// （方便开发时一眼发现漏翻的键）。
    /// </summary>
    public static string T(string key)
    {
        if (LocStrings.Catalogs.TryGetValue(CurrentLanguage, out var catalog) &&
            catalog.TryGetValue(key, out var text))
        {
            return text;
        }
        if (LocStrings.Catalogs["zh-CN"].TryGetValue(key, out var fallback))
        {
            return fallback;
        }
        return key;
    }

    /// <summary>带参数的格式化文字，例如 TF("status.monitoring", 3) → “监控 3 个目录”。</summary>
    public static string TF(string key, params object[] args)
    {
        return string.Format(T(key), args);
    }
}
