using Xunit;
using FolderSentinel.Core.Monitoring;

namespace FolderSentinel.Tests;

/// <summary>
/// 路径过滤器单元测试：验证忽略规则（*.tmp、~$* 等）的行为。
/// </summary>
public class PathFilterTests
{
    [Fact]
    public void 默认规则_忽略tmp临时文件()
    {
        var filter = PathFilter.CreateDefault();
        Assert.True(filter.ShouldIgnore(@"C:\下载\安装包.tmp"));
        Assert.False(filter.ShouldIgnore(@"C:\下载\安装包.txt"));
    }

    [Fact]
    public void 默认规则_忽略Office锁定文件_以波浪号美元开头()
    {
        var filter = PathFilter.CreateDefault();
        Assert.True(filter.ShouldIgnore(@"C:\docs\~$毕业论文.docx"));
        Assert.False(filter.ShouldIgnore(@"C:\docs\毕业论文.docx"));
    }

    [Fact]
    public void log文件_默认记录_可选忽略()
    {
        // 默认不忽略 .log（要不要忽略让用户选）
        Assert.False(PathFilter.CreateDefault(ignoreLogFiles: false).ShouldIgnore(@"C:\logs\app.log"));
        // 打开开关后才忽略
        Assert.True(PathFilter.CreateDefault(ignoreLogFiles: true).ShouldIgnore(@"C:\logs\app.log"));
    }

    [Fact]
    public void 匹配不区分大小写()
    {
        var filter = PathFilter.CreateDefault();
        Assert.True(filter.ShouldIgnore(@"C:\x\TEMP_FILE.TMP"));
        Assert.True(filter.ShouldIgnore(@"C:\x\~$Report.DOCX"));
    }

    [Fact]
    public void 带目录的规则_按完整路径匹配()
    {
        // 规则里带了目录分隔符：只忽略 build 文件夹下的日志，其他位置的日志照常记录
        var filter = new PathFilter(new[] { @"build\*.log" });
        Assert.True(filter.ShouldIgnore(@"C:\proj\build\x.log"));
        Assert.False(filter.ShouldIgnore(@"C:\proj\x.log"));
    }

    [Fact]
    public void 问号通配符_恰好匹配一个字符()
    {
        var filter = new PathFilter(new[] { "file?.txt" });
        Assert.True(filter.ShouldIgnore(@"C:\a\file1.txt"));
        Assert.False(filter.ShouldIgnore(@"C:\a\file10.txt"));   // 两个字符，不匹配
        Assert.False(filter.ShouldIgnore(@"C:\a\file.txt"));     // 零个字符，不匹配
    }

    [Fact]
    public void 星号通配符_匹配任意长度()
    {
        var filter = PathFilter.CreateDefault();
        Assert.True(filter.ShouldIgnore(@"C:\a\b\很长的文件名.最终版.真的最终版.tmp"));
    }

    [Fact]
    public void 子目录里的文件同样接受过滤()
    {
        var filter = PathFilter.CreateDefault();
        Assert.True(filter.ShouldIgnore(@"C:\proj\子目录\更深处\草稿.tmp"));
    }

    [Fact]
    public void 文件名和规则完全相同_也命中()
    {
        var filter = new PathFilter(new[] { "exact.tmp" });
        Assert.True(filter.ShouldIgnore(@"C:\x\exact.tmp"));
        Assert.False(filter.ShouldIgnore(@"C:\x\exact.tmp2"));
    }

    [Fact]
    public void 空路径_不忽略()
    {
        var filter = PathFilter.CreateDefault();
        Assert.False(filter.ShouldIgnore(""));
    }
}
