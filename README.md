# Antigravity Account Switcher

AgySwitch 是一款 Windows 桌面工具，用于备份和切换 Antigravity CLI 的 `.gemini` 认证档案，并查看 CLI 提供的模型额度。

## 功能

- 备份当前账户，并在多个账户档案之间切换。
- 切换前保存当前认证文件；切换失败时尝试恢复，并保留最近一次切换前的恢复副本。
- 管理账户档案：重命名、更新备份、打开档案目录或删除档案。
- 从系统托盘打开或退出程序。
- 手动调用 Antigravity CLI 查询各模型的 `7d`、`5h` 剩余额度和重置时间。

## 快速开始

首次启动时，AgySwitch 会检测 `%USERPROFILE%\.gemini`。如果 CLI 使用其他目录，可在“设置”中选择实际的 `.gemini` 目录。

在主窗口中选择“备份当前账户”保存当前档案，然后即可管理档案并切换账户。切换账户会更新当前 `.gemini` 目录中的认证文件。

## 系统要求

- Windows 10 或 Windows 11
- Antigravity CLI：仅查询额度时需要；账户备份和切换可独立使用
- .NET 8 SDK：从源码构建及使用 `dotnet run` 时需要

## 从源码运行

在仓库根目录执行：

```powershell
dotnet build AgySwitch.sln
dotnet run --project src/AgySwitch/AgySwitch.csproj
```

## 生成安装包

需要安装 .NET 8 SDK 和 Inno Setup 6。运行：

```powershell
.\build-installer.ps1
```

安装包生成在 `dist/AgySwitch-Setup-1.0.0-win-x64.exe`。该安装包包含 .NET 运行时，目标平台为 Windows x64。

## 使用说明

### 账户切换

“备份当前账户”会复制当前 `google_accounts.json` 和 `antigravity-cli\antigravity-oauth-token` 到账户档案目录。账户切换前，应用会再次保存当前认证文件，并在恢复目录中保留切换前的副本；如果切换失败，会尝试恢复这两份文件。

最小化窗口会将程序留在系统托盘。双击托盘图标可重新打开窗口；右键菜单可退出程序。点击窗口关闭按钮会退出程序并移除托盘图标。

### 额度查询

额度查询只会在点击“刷新额度”时运行，不会定时或在后台查询。应用会尝试从 `PATH` 查找 `agy.exe` 或 `antigravity.exe`，也可使用“设置”中指定的 CLI 可执行文件，并调用官方 [`/usage` 命令](https://antigravity.google/docs/cli/commands/usage/)。如果 CLI 没有返回可识别的额度表格，应用会显示 CLI 原始文本。

## 数据与安全

- 账户档案保存在 `.gemini\antigravity-accounts\account_001\` 等目录中。
- 每个 `profile.json` 保存账户名称、邮箱、账户 ID、创建时间、最近使用时间和用于匹配档案的哈希，不保存 OAuth Token。
- 应用设置保存在 `%LOCALAPPDATA%\AntigravityAccountSwitcher\settings.json`，仅记录 Gemini 目录和 CLI 程序路径。
- 最近一次切换前的认证文件副本保存在 `.gemini\antigravity-accounts\.recovery\`。
- 操作日志保存在 `.gemini\antigravity-accounts\logs\app.log`，只记录操作结果，不记录认证文件内容或 CLI 原始输出。

**认证文件包含敏感信息。** 应用会将认证文件明文复制到本机账户档案和恢复目录；本版本不加密这些文件，也不提供 OAuth 登录。请妥善保管这些目录，并在分享日志或档案前检查其中是否包含敏感信息。
