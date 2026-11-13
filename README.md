# Antigravity Account Switcher

AgySwitch 是一款 Windows 桌面工具，用于备份和切换 Antigravity CLI 的 Windows 凭据，并查看 CLI 提供的模型额度。

## 功能

- 备份当前账户，并在多个账户档案之间切换。
- 切换前保存当前凭据；切换失败时尝试恢复，并保留最近一次切换前的恢复凭据。
- 管理账户档案：重命名、更新备份、打开档案目录或删除档案。
- 从系统托盘打开或退出程序。
- 手动调用 Antigravity CLI 查询各模型的 `7d`、`5h` 剩余额度和重置时间。

## 快速开始

首次启动时，AgySwitch 会检测 `%USERPROFILE%\.gemini`。如果 CLI 使用其他目录，可在“设置”中选择实际的 `.gemini` 目录。该目录只用于 CLI 配置和额度查询；账户档案统一保存在 `%USERPROFILE%\antigravity-accounts`。

在主窗口中选择“备份当前账户”保存当前档案，然后即可管理档案并切换账户。

## 系统要求

- Windows 10 或 Windows 11
- Antigravity CLI：账户须先通过 CLI 登录；查询额度时需要 CLI
- .NET 8 SDK：从源码构建及使用 `dotnet run` 时需要

## 从源码运行

```powershell
dotnet build AgySwitch.sln
dotnet run --project src/AgySwitch/AgySwitch.csproj
```

## 生成安装包

需要安装 .NET 8 SDK 和 Inno Setup 6。运行 `./build-installer.ps1`，安装包生成在 `dist/AgySwitch-Setup-1.0.0-win-x64.exe`。

## 使用说明

### 账户切换

“备份当前账户”读取 Windows 凭据管理器中的通用凭据 `gemini:antigravity`，从其中的 `id_token_email` 识别账户，并将完整凭据备份到 AgySwitch 专用的 Windows 凭据项。切换前会更新当前账户备份并保存恢复凭据，再将目标凭据写回 `gemini:antigravity`；失败时尝试恢复。建议先关闭 CLI，以免运行中的进程继续使用或覆盖旧凭据。

最小化窗口会将程序留在系统托盘。双击托盘图标可重新打开窗口；右键菜单可退出程序。点击窗口关闭按钮会退出程序并移除托盘图标。

### 额度查询

额度查询只会在点击“刷新额度”时运行，不会定时或在后台查询。应用会尝试从 `PATH` 查找 `agy.exe` 或 `antigravity.exe`，也可使用“设置”中指定的 CLI 可执行文件，并调用官方 [`/usage` 命令](https://antigravity.google/docs/cli/commands/usage/)。如果 CLI 没有返回可识别的额度表格，应用会显示 CLI 原始文本。

## 数据与安全

- 账户元数据保存在 `%USERPROFILE%\antigravity-accounts\account_001\profile.json` 等目录中，包含账户名称、邮箱和时间，不包含令牌。
- 账户令牌及最近一次切换前的恢复副本保存在 Windows 凭据管理器的 `AgySwitch:` 项中。CLI 的当前凭据为 `gemini:antigravity`。
- 应用设置保存在 `%LOCALAPPDATA%\AntigravityAccountSwitcher\settings.json`。日志保存在 `%USERPROFILE%\antigravity-accounts\logs\app.log`，不记录令牌内容。
- 首次启动时，旧版 CLI 配置目录下的 `antigravity-accounts` 中的档案元数据和日志会复制到用户目录；旧版已保存在 Windows 凭据管理器的账户令牌会在读取时迁移。旧目录中的明文令牌和恢复副本不会复制或删除，确认不再需要后请自行清理。
- 若凭据中缺少可识别的 `id_token_email`，应用会拒绝备份或切换，以免将令牌保存到错误的账户。
