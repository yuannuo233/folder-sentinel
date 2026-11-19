[English](README.en.md) | 简体中文

<div align="center">

# FolderSentinel 文件哨兵

[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
[![Platform](https://img.shields.io/badge/Platform-Windows-lightgrey.svg)]()
[![.NET](https://img.shields.io/badge/.NET-10.0-blue.svg)]()

</div>

**Find out who touched which files on your disk, and when — free, open source, 100% local.** A friendly alternative to Process Monitor (too cryptic) and Disk Pulse (paid & closed source).

- 📡 Watch multiple folders (recursively) and log every create, modify, rename and delete in real time
- 🕐 Everything lands in a local SQLite database — browse by day, search by keyword
- 🎯 Debounced and noise-filtered: temp files like `*.tmp` and `~$*` are ignored automatically, duplicate events merged
- 🛡️ Self-exclusion guard: the app never records itself (no runaway loops); minimize to tray to keep watching in the background

## Download & Usage in three steps

Download the latest `FolderSentinel-v0.1.0-win-x64.zip` from [Releases](../../releases) and extract it (single file, no installer), then:

1. Launch the app, click **Add folder** on the left and pick a folder to watch.
2. Any change made by any program shows up instantly in the list on the right.
3. Want history? Pick a date at the top to replay that day; minimizing to the tray keeps monitoring alive.

## Build & run

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download), then:

```
dotnet build FolderSentinel.sln -c Release
dotnet test  tests/FolderSentinel.Tests
dotnet publish src/FolderSentinel.App -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o dist
```

> 🔒 **Privacy**: all data stays in the `data\` folder next to the executable (SQLite database + log). Nothing is uploaded; delete the folder and it's gone for good.
> 📖 For how it works and known limitations (e.g. per-process attribution arrives in v0.2 via ETW), read the source — every file is commented in Chinese. Released under the [MIT](LICENSE) license.
