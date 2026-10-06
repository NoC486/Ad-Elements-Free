# Ad Elements Free

<p align="center"><img src="src/AdElementsFree/Assets/icon.png" width="180" alt="Ad Elements Free 图标" /></p>

轻量 Windows 托盘应用，用于管理受支持桌面客户端中的广告元素显示。技术栈：C#、.NET 8、WPF，以及 Windows 自带的 WinForms NotifyIcon / WScript.Shell。无第三方 NuGet 运行依赖。

各客户端采用独立 Provider 和规则目录，便于持续扩展支持范围。当前可用客户端以软件内列表为准，并非所有客户端均已适配。程序通过受支持客户端自身的本机 CDP 接口添加页面样式，不修改客户端安装文件。

## 使用

1. 从 [Releases](https://github.com/NoC486/Ad-Elements-Free/releases/latest) 下载 Windows x64 `setup.exe` 安装包，或下载 `portable.zip` 解压运行。两者均自带 .NET 8，无需另装运行时。
2. 默认只显示系统托盘图标；单击、双击或右键菜单打开窗口。首次默认关闭，避免未经用户操作修改快捷方式。
3. 打开需要管理的客户端开关。程序检查当前用户/公共桌面、当前用户/公共开始菜单中的 `.lnk`。
4. 若目标客户端已在运行，请根据状态提示完全退出，再从已配置的快捷方式启动。程序不会强行重启客户端。
5. 目标页面通过身份检查并完成加载后，程序应用对应的内置规则。
6. 关闭开关会停止监控、尽力移除当前 CSS 和重载脚本，并恢复本程序添加的快捷方式参数。正在运行的调试端口需完全退出对应客户端才关闭。

### 设置与更新

- 主窗口右上角或托盘菜单的 **设置** 可开启/关闭 **开机自启**，默认关闭。开启后在当前用户登录 Windows 时静默驻留托盘，不需要管理员权限。
- 自启只针对当前程序路径；便携版移动位置后需在新位置重新开启。若在 Windows 的“启动应用”中禁用过，还需在那里重新启用。
- 点击 **检查更新** 才会请求本仓库的 GitHub 最新正式版信息。发现新版本后提供发布页链接，由你下载并安装；不会后台自动检查、下载或执行更新。网络失败、超时和请求限额会显示提示，可稍后重试。
- 主窗口底部显示当前版本、作者 **NoC486** 和 GitHub 仓库超链接。

自启设置保存在当前用户注册表 `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` 的 `AdElementsFree` 值，仅在操作开关时写入。卸载前请先关闭开机自启；卸载器仅清理执行卸载的用户中、路径与本安装完全匹配的自启项，其他 Windows 用户需各自关闭。

退出托盘程序保留开关设置及快捷方式参数，便于下次继续；如需卸载，请**先关闭所有客户端开关和开机自启**，确认恢复提示，再退出并卸载程序。

没有直启快捷方式时，请为目标客户端的真实可执行文件创建快捷方式，再按软件状态提示配置。不同客户端所需参数与端口由各自 Provider 决定，不应直接套用其他客户端的配置。用户自己添加的参数不会由本程序删除。不要加入 `--remote-allow-origins=*`。

## 编译与验证

需要 Windows 和 .NET 8 SDK。仓库旁 `.tools/dotnet` 为开发时下载的本地 SDK，不属于源码或发布包。

当前工作目录可运行 `./build.ps1 -Test -Publish`，脚本优先使用项目旁的本地 SDK。

```powershell
dotnet build AdElementsFree.sln -c Release
dotnet run --project tests/AdElementsFree.Tests -c Release
dotnet run --project tests/AdElementsFree.WindowsTests -c Release
dotnet publish src/AdElementsFree -c Release -r win-x64 --self-contained true -o artifacts/win-x64
```

WindowsTests 会短暂显示测试窗口和托盘，仅修改自身输出目录的测试快捷方式，不修改用户桌面快捷方式。基础测试会创建随机本机端口以验证归属检查及模拟 CDP。

制作安装包见 [发布指南](docs/RELEASING.md)。64 位安装包默认“为所有用户安装”，仅安装器申请管理员权限，首次安装优先 `D:\Program Files\Ad Elements Free`，没有 D 盘时选择其他非 C 盘固定磁盘；均不存在时使用系统 Program Files。也可选择“仅为当前用户安装”，无需提权，默认使用当前用户目录。两种模式均可修改目录。主程序仍使用普通权限；卸载前先关闭所有客户端开关并从托盘退出。

## 目录

```text
src/AdElementsFree/
  App.xaml(.cs)          应用生命周期及 Provider 注册
  Core/                 Provider 接口
  UI/                   通用 Provider 列表和开关
  Tray/                 托盘菜单
  Settings/             原子 JSON 持久化和当前用户自启
  Updates/              手动 GitHub 正式版检查
  Logging/              有界本地日志
  Monitoring/           目标进程与 TCP 端口归属
  Shortcuts/            Windows 参数解析、快捷方式修改及恢复
  Cdp/                  WebSocket 请求/响应与超时
  Providers/            按客户端分别存放身份、生命周期、脚本与规则
tests/                  无测试框架依赖的基础及 Windows 集成测试
docs/                   设计、安全边界、实机验证清单
```

新增应用时实现 `IAdProvider`，把特定逻辑放在 `Providers/客户端名称/`，然后在 `App` 的组合入口注册实例。UI、托盘和设置以 Provider ID 工作。每个 Provider 独立管理自己的端口与规则；静态规则放在对应的 `Rules/` 目录。

## 数据与恢复

数据位于 `%LOCALAPPDATA%/AdElementsFree`：

- `settings.json`：Provider 开关。
- 各客户端的快捷方式恢复文件（`*-shortcuts.json`）：快捷方式路径、原 Target、原 Arguments、本程序写入后的 Arguments。先写记录，再修改快捷方式。
- `Logs/app.log` 和 `app.log.1`：每份约 512 KiB，最多两份。不记录聊天、Token、Cookie、页面响应或完整异常消息。

记录损坏时停止相关恢复操作，不覆盖原文件。不要在关闭开关并完成恢复前删除恢复记录。公共快捷方式无权限时仅报告，不请求管理员权限。

## 当前限制

- 支持范围以软件内列表和各 Provider 的适配情况为准，已有实机验证不代表所有版本或所有环境均兼容。
- 只自动修改直接指向目标客户端、且产品元数据通过身份核验的快捷方式。更新器包装的快捷方式不自动修改；请建立直启快捷方式，不猜测嵌套参数。
- 启用/程序启动时重新发现快捷方式；客户端更新替换路径后，可关闭再开启以重新检查。用户修改 Target 的旧记录保留，提示手工检查。
- 页面地址白名单由各 Provider 独立维护，仅允许经审查的目标页面；不同版本的新地址可能被拒绝，需要审查后适配。
- “已应用规则”表示 CDP 返回成功且样式存在，不代表每个选择器在新版本中仍匹配。CSS 保留上游较脆弱的 `nth-child` 规则，升级后需验收。
- 若关闭期间连接已断开，CSS 可能留到目标页面重载或客户端完全重启。程序退出后不再主动维持规则。
- 未测试或取得任何反作弊厂商认证，不作绝对兼容保证；详见安全设计。

第三方规则与素材的来源、授权及版权声明见 [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)。

## 参与开发

新增客户端或调整规则前请阅读 [贡献指南](CONTRIBUTING.md) 和 [安全设计](docs/DESIGN.md)。提交问题时请注明 Windows / 客户端版本、重现步骤和预期结果；不要上传聊天内容、Token、Cookie 或未经检查的个人配置。

## 许可证

Copyright (C) 2026 NoC486。

本项目采用 **GNU General Public License version 3 only（GPL-3.0-only）**，完整条款见 [LICENSE](LICENSE)。

你可以使用、修改和再分发本项目，也可以商用或收费。向他人分发修改版本时，必须按 GPLv3 提供对应源码、保留版权与许可证声明，并标明修改。仅自行修改使用，无需向公众发布源码。软件按现状提供，不附带担保。

发布可执行版本时，应同时提供对应版本的完整源码及构建脚本；在 GitHub Releases 发布时请使用对应源码标签，不要只上传 EXE。独立第三方组件遵循各自许可证。
