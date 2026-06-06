# 全能脚本精灵

全能脚本精灵是一款 Windows 桌面自动化工具，用来录制鼠标、键盘和窗口操作，并按方案重复回放。它适合把固定流程做成可复用脚本，比如批量填写、窗口切换、点击等待、变量粘贴和多轮执行。

![动作脚本界面](说明截图/ScreenShot_2026-03-29_212003_069.png)

## 下载

请到 GitHub 仓库的 [Releases](https://github.com/yehuili1/xiaoyedj-site/releases) 下载最新版压缩包。

当前版本：`v2.2.1`

发布包内是独立可执行文件，解压后运行 `全能脚本V2.2.1.exe` 即可。

## 主要功能

- 录制鼠标移动、点击、键盘输入等操作。
- 按方案保存脚本，支持新建、重命名、删除、导入和导出。
- 支持循环次数和回放倍速设置。
- 支持变量表，回放时按行读取并粘贴变量。
- 支持图片点击、等待图片、等待窗口、激活窗口等增强动作。
- 支持全局热键，包括开始/停止录制、暂停录制、开始/停止回放、紧急停止，以及为单个方案绑定快捷键。
- 支持系统托盘和迷你模式，方便在自动化任务中快速控制。
- 运行日志会记录回放、窗口、剪贴板、图片识别等关键状态，便于排查问题。

## 快速使用

1. 下载并解压 Release 包。
2. 双击运行 `全能脚本V2.2.1.exe`。
3. 新建一个方案。
4. 点击“开始录制”，完成目标操作后停止录制。
5. 根据需要调整循环次数、回放倍速或变量表。
6. 点击“开始回放”执行脚本。

## 热键

热键可在应用内设置。默认配置会保存在程序目录旁的 `hotkey_settings.json` 中。

建议至少设置：

- 开始/停止录制
- 暂停/继续录制
- 开始/停止回放
- 紧急停止

如果需要一键启动某个方案，可以在热键设置中为该方案单独绑定快捷键。

## 方案数据

应用会在程序目录旁创建 `Profiles/` 目录保存方案数据：

- `profile.json`：方案配置
- `record.json`：动作脚本
- `variables.csv`：变量表
- `Images/`：图片点击和等待图片使用的模板图

需要迁移方案时，优先使用应用内的“导出方案”和“导入方案”。

## 本地开发

开发环境：

- Windows
- .NET 8 SDK

构建：

```powershell
dotnet build AutoMacro.sln
```

发布 win-x64：

```powershell
dotnet publish AutoMacro\AutoMacro\AutoMacro.csproj -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true
```

## 发版流程

1. 确认 `AutoMacro/AutoMacro/AutoMacro.csproj` 中的版本号已更新。
2. 执行 `dotnet build AutoMacro.sln`，确保构建通过。
3. 打包发布产物为 zip。
4. 创建版本标签，例如 `v2.2.1`。
5. 在 GitHub Releases 创建同名版本，并上传 zip 附件。

本仓库已忽略构建输出、发布目录、zip 包、日志和运行配置，避免把本机产物提交进源码仓库。
