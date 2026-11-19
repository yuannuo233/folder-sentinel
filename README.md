[English](README.en.md) | 简体中文

<div align="center">

# 文件哨兵 FolderSentinel

[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
[![Platform](https://img.shields.io/badge/Platform-Windows-lightgrey.svg)]()
[![.NET](https://img.shields.io/badge/.NET-10.0-blue.svg)]()

</div>

**看看谁在什么时候动了你的哪些文件——免费、开源、纯本地。** Process Monitor 太专业，Disk Pulse 要收费，文件哨兵是一个友好的中文替代品。

- 📡 同时监控多个文件夹（含子文件夹），实时记录新建 / 修改 / 重命名 / 删除
- 🕐 所有事件存进本地 SQLite，随时按天回看，还能按关键字搜索
- 🎯 自动忽略 `*.tmp`、`~$*` 等临时文件垃圾，合并重复事件，时间轴干净清爽
- 🛡️ 自带"防自记录"保险：绝不监控自己，避免死循环；支持最小化到托盘后台监控

## 下载 & 使用三步

从 [Releases](../../releases) 下载最新版 `FolderSentinel-v0.1.0-win-x64.zip` 并解压（单文件、免安装），然后：

1. 打开程序，点左侧 **添加目录**，选中你想盯着的文件夹；
2. 之后任何软件对这个文件夹的操作都会实时出现在右侧列表；
3. 想翻旧账？顶部选个日期即可回看当天记录，最小化到托盘可继续后台监控。

## 构建与运行

需要 [.NET 10 SDK](https://dotnet.microsoft.com/download)，然后：

```
dotnet build FolderSentinel.sln -c Release
dotnet test  tests/FolderSentinel.Tests
dotnet publish src/FolderSentinel.App -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o dist
```

> 🔒 **隐私**：全部数据只保存在程序目录的 `data\` 文件夹里（SQLite 数据库 + 日志），不上传任何东西，删掉文件夹即彻底清除。
> 📖 工作原理与已知限制（如进程归属将在 v0.2 用 ETW 实现）见源码注释——每个文件都有中文讲解。采用 [MIT](LICENSE) 协议开源。

## 问题反馈 / 寻求帮助

遇到问题或有建议？请到本仓库的 **[Issues](../../issues)**（留言板）发一条，我会在那里回复你。带上 Windows 版本、复现步骤和报错截图能帮我更快定位问题。

> 💡 为什么用 Issues？问题和解答是公开的——其他人遇到同样的问题，搜一下就能找到答案，等于大家共建一本说明书。
