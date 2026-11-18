using System.Text.RegularExpressions;

namespace FolderSentinel.Core.Monitoring;

/// <summary>
/// 路径过滤器：决定哪些文件“不值得记录”，直接忽略。
///
/// 为什么需要它？Windows 上有大量临时文件在不停被写入
/// （比如 Office 打开文档时会生成 ~$xxx.docx 这种锁定文件），
/// 如果照单全收，时间轴会被垃圾信息淹没。
///
/// 规则写法：支持 * （任意多个字符）和 ? （任意一个字符）通配符。
/// 例如 "*.tmp" 忽略所有临时文件；"build\*.log" 只忽略 build 文件夹下的日志。
/// 独立成一个类，是为了单元测试方便。
/// </summary>
public sealed class PathFilter
{
    /// <summary>忽略规则列表，例如 "*.tmp"、"~$*"。</summary>
    private readonly List<string> _patterns;

    public PathFilter(IEnumerable<string> patterns)
    {
        _patterns = new List<string>(patterns);
    }

    /// <summary>
    /// 创建默认过滤器：忽略 Windows 常见的临时文件。
    /// </summary>
    /// <param name="ignoreLogFiles">是否同时忽略 *.log 日志文件（可选开关）</param>
    public static PathFilter CreateDefault(bool ignoreLogFiles = false)
    {
        var patterns = new List<string> { "*.tmp", "~$*" };
        if (ignoreLogFiles)
        {
            patterns.Add("*.log");
        }
        return new PathFilter(patterns);
    }

    /// <summary>
    /// 判断某个文件是否应该被忽略。
    /// </summary>
    /// <param name="fullPath">文件的完整路径</param>
    /// <returns>true = 忽略它（不记录）；false = 记录它</returns>
    public bool ShouldIgnore(string fullPath)
    {
        if (string.IsNullOrEmpty(fullPath))
        {
            return false;
        }

        var fileName = System.IO.Path.GetFileName(fullPath);
        foreach (var pattern in _patterns)
        {
            // 把规则里的“/”统一成 Windows 的“\”，两种写法都支持
            var normalizedPattern = pattern.Replace('/', '\\');

            // 规则里带“\”的（例如 build\*.log），按完整路径的后缀匹配；
            // 不带的（例如 *.tmp），只按文件名整体匹配。
            var hasDirectoryPart = normalizedPattern.Contains('\\');
            var hit = hasDirectoryPart
                ? WildcardMatches(normalizedPattern, fullPath, matchWholeText: false)
                : WildcardMatches(normalizedPattern, fileName, matchWholeText: true);

            if (hit)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// 通配符匹配：* 代表任意多个字符，? 代表任意一个字符。
    /// Windows 文件名不区分大小写，所以这里用“忽略大小写”的比较。
    /// 实现技巧：把通配符表达式转换成正则表达式来匹配。
    /// </summary>
    /// <param name="pattern">通配符规则</param>
    /// <param name="text">被检查的文本（完整路径或文件名）</param>
    /// <param name="matchWholeText">
    /// true = 文本要和规则“整体一模一样”（按文件名匹配时用）；
    /// false = 只要求是“后缀匹配”，且匹配点必须紧跟在目录分隔符之后
    /// （按完整路径匹配时用，例如规则 build\*.log 要命中 C:\proj\build\x.log）
    /// </param>
    private static bool WildcardMatches(string pattern, string text, bool matchWholeText)
    {
        // Regex.Escape 会把正则特殊字符转义；然后我们把被转义的 \* 换成“任意长度”、\? 换成“任意单字符”。
        var body = Regex.Escape(pattern)
            .Replace(@"\*", ".*")
            .Replace(@"\?", ".");

        var regex = matchWholeText
            ? "^" + body + "$"
            : @"(^|\\)" + body + "$";   // 匹配点必须是路径开头，或紧跟一个“\”，避免误伤 mybuild 这类名字

        return Regex.IsMatch(text, regex, RegexOptions.IgnoreCase);
    }
}
