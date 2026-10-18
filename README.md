# Codex Quota Bar · Codex 额度条

Windows 上跟随 Codex 桌面窗口的额度浮条，支持拖动、位置保存和系统托盘。

**[下载最新版本](https://github.com/wangguichao/codex-quota-bar/releases/latest)** · [使用说明](docs/share/使用说明.txt) · [验证范围](docs/VALIDATION.md)

## 开始使用

1. 在 Releases 下载 `CodexQuotaBar-0.5.0-Windows-x64.zip`，**全部解压**到固定文件夹。
2. 先打开官方 Codex 桌面端，登录自己的 ChatGPT 账号，确认使用个人空间。
3. 双击解压后的 **启动.cmd**，按首次引导检查连接、登录并核对桌面邮箱、个人空间与额度周期。
4. 确认并开始使用。以后双击启动入口即可；已运行时会恢复原窗口。

便携包包含 .NET 运行环境和官方 Codex CLI 0.154.0 连接组件，无需另装 Node.js、Python 或开发工具。程序与登录服务仍需要自己的账号及可用网络。

## 功能

- 显示 Codex 套餐剩余额度、实际额度周期与重置倒计时。
- 默认每60秒刷新；可选30秒、2分钟、5分钟，支持立即刷新。
- 多个额度池、周期分别显示；身份不符、读取失败或过期时不显示旧百分比。
- 按住横条鼠标左键，在 Codex 窗口内自由拖动，松开自动保存位置。
- 锁定位置后鼠标可穿透横条；支持重置、宽度调整和深浅主题。
- 最小化到系统托盘后继续更新；双击绿色 Q 恢复，右键可退出。
- 低额度提醒按账号、额度池和周期去重。
- 首次使用与重新核对账号引导，保存前再次检查身份。

## 使用范围与数据

适用于 **Windows x64、官方 Codex 桌面端、ChatGPT 个人空间**。建议 Windows 11 x64；本机验证环境为 Windows 10 Pro 22H2、单屏125%。Mac、Windows ARM及公司/团队空间未验证。

这是独立制作的小工具，不是 OpenAI 官方客户端。只显示 Codex 套餐额度，不显示普通 ChatGPT 聊天剩余次数或 API 余额。

发布包没有作者的账号绑定、个人设置、诊断或登录凭据。分享版将本机绑定、偏好及脱敏快照写入 `%LOCALAPPDATA%\CodexQuotaBar`。登录由官方 Codex 管理，本工具不读取凭据文件。

CLI 与桌面可能单独切换账号。首次连接和换号后，需要使用者亲自核对桌面邮箱、个人空间、周期及重置时间；不能仅用相近的百分比判断身份。

## 开发

源码使用 C# / WPF / .NET 10。源码与便携包分开保存，运行组件通过 Releases 提供。

用 PowerShell 7 在仓库根目录执行：

```powershell
./scripts/Initialize-DevTools.ps1
./scripts/Build.ps1
./scripts/Test.ps1
```

SDK 10.0.402 下载到仓库上级的 `.tools`，不修改系统 PATH。已安装 SDK 的开发者也可直接构建；进程测试需要 `DOTNET_ROOT` 指向 SDK 目录。

```powershell
$env:DOTNET_ROOT = Split-Path -Parent (Get-Command dotnet).Source
dotnet build CodexQuotaBar.sln -c Release
dotnet test tests/CodexQuotaBar.Tests -c Release
```

生成分享包（需要官方 `codex.exe` 0.154.0）：

```powershell
./scripts/Publish-Share.ps1 -CodexPath 'C:\path\to\codex.exe'
```

[源码说明](源码说明.txt)包含其他检查入口。GitHub Actions 在 Windows 上构建并运行自动测试；原生窗口交互检查在本地执行。

## 当前验证

v0.5.0 已通过103项自动测试和66项本地窗口/界面检查，并完成全新解压启动、随包 CLI 只读查询及归档隐私检查。首次引导测试使用模拟账号，未代替其他使用者完成实际浏览器登录。

实际物理鼠标连续操作、跨屏/其他真实DPI、物理休眠/断网和8小时运行仍需进一步验收。详见[验证范围](docs/VALIDATION.md)。

## 第三方组件

官方 Codex CLI 保持原二进制，版本与许可位于 `third-party/codex`。便携包另附 .NET 运行时的许可及第三方通知。第三方组件按各自许可提供。

