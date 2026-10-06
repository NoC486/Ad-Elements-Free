# Ad Elements Free

<p align="center"><img src="src/AdElementsFree/Assets/icon.png" width="180" alt="Ad Elements Free 图标" /></p>

轻量 Windows 托盘应用，第一版只提供 KOOK 广告元素隐藏。技术栈：C#、.NET 8、WPF，以及 Windows 自带的 WinForms NotifyIcon / WScript.Shell。无第三方 NuGet 运行依赖。

目前支持 **KOOK**，用户已实测广告元素隐藏效果正常。各客户端采用独立 Provider，后续可扩展。它通过目标客户端自身的本机 CDP 接口添加页面样式，不修改客户端安装文件。

## 使用

1. 安装 .NET 8 Desktop Runtime，运行 `AdElementsFree.exe`；或使用自包含发布目录中的程序。
2. 默认只显示系统托盘图标；单击、双击或右键菜单打开窗口。首次默认关闭，避免未经用户操作修改快捷方式。
3. 打开 KOOK 开关。程序检查当前用户/公共桌面、当前用户/公共开始菜单中的 `.lnk`。
4. 若 KOOK 已在运行，请完全退出 KOOK，再从已配置的快捷方式启动。程序不会强行重启 KOOK。
5. KOOK 主页面被确认后等待约 4 秒，再应用内置规则。
6. 关闭开关会停止监控、尽力移除当前 CSS 和重载脚本，并恢复本程序添加的快捷方式参数。正在运行的调试端口需完全退出 KOOK 才关闭。

退出托盘程序保留开关设置及快捷方式参数，便于下次继续；如需卸载，请**先关闭 KOOK 开关**，确认恢复提示，再退出并删除程序。

没有直启快捷方式时，可为真实的 `KOOK.exe` 创建快捷方式，在参数末尾添加 `--remote-debugging-port=9222`。用户自己添加的参数不会由本程序删除。不要加入 `--remote-allow-origins=*`。

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

## 目录

```text
src/AdElementsFree/
  App.xaml(.cs)          应用生命周期及 Provider 注册
  Core/                 Provider 接口
  UI/                   通用 Provider 列表和开关
  Tray/                 托盘菜单
  Settings/             原子 JSON 持久化
  Logging/              有界本地日志
  Monitoring/           目标进程与 TCP 端口归属
  Shortcuts/            Windows 参数解析、快捷方式修改及恢复
  Cdp/                  WebSocket 请求/响应与超时
  Providers/KOOK/        KOOK 身份、生命周期、脚本及 Rules/style.css
tests/                  无测试框架依赖的基础及 Windows 集成测试
docs/                   设计、安全边界、实机验证清单
```

新增应用时实现 `IAdProvider`，把特定逻辑放在 `Providers/XXX`，然后在 `App` 的组合入口注册实例。UI、托盘和设置以 Provider ID 工作。KOOK 端口常量属于其 Provider，其他 Provider 可选择自己的端口。

## 数据与恢复

数据位于 `%LOCALAPPDATA%/AdElementsFree`：

- `settings.json`：Provider 开关。
- `kook-shortcuts.json`：快捷方式路径、原 Target、原 Arguments、本程序写入后的 Arguments。先写记录，再修改快捷方式。
- `Logs/app.log` 和 `app.log.1`：每份约 512 KiB，最多两份。不记录聊天、Token、Cookie、页面响应或完整异常消息。

记录损坏时停止相关恢复操作，不覆盖原文件。不要在关闭开关并完成恢复前删除恢复记录。公共快捷方式无权限时仅报告，不请求管理员权限。

## 当前限制

- 2026-10-06，用户反馈 KOOK 实机广告隐藏效果正常；未记录客户端版本，不代表所有版本或所有环境已验证。
- 只自动修改直接指向、且产品元数据可识别为 KOOK 的 `KOOK.exe` 快捷方式。`Update.exe --processStart` 等更新器包装快捷方式不修改；请建立直启快捷方式。安全优先，不猜测嵌套参数。
- 启用/程序启动时重新发现快捷方式；KOOK 更新替换路径后，可关闭再开启以重新检查。用户修改 Target 的旧记录保留，提示手工检查。
- URL 白名单目前覆盖 KOOK/Kaiheila 官方 HTTPS 域名、本机 5890 的 `/app/` 页面和安装目录下 `/app/` 文件页面；不同版本的新地址会被拒绝，需要审查后适配。
- “已应用规则”表示 CDP 返回成功且样式存在，不代表每个选择器在新版本中仍匹配。CSS 保留上游较脆弱的 `nth-child` 规则，升级后需验收。
- 若关闭期间连接已断开，CSS 可能留到 KOOK 页面重载或完全重启。程序退出后不再主动维持规则。
- 未测试或取得任何反作弊厂商认证，不作绝对兼容保证；详见安全设计。

参考与规则来源：[NoC486/OKKO](https://github.com/NoC486/OKKO)，见 [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)。

## 参与开发

新增客户端或调整规则前请阅读 [贡献指南](CONTRIBUTING.md) 和 [安全设计](docs/DESIGN.md)。提交问题时请注明 Windows / 客户端版本、重现步骤和预期结果；不要上传聊天内容、Token、Cookie 或未经检查的个人配置。

## 许可证

Copyright (C) 2026 NoC486。

本项目采用 **GNU General Public License version 3 only（GPL-3.0-only）**，完整条款见 [LICENSE](LICENSE)。

你可以使用、修改和再分发本项目，也可以商用或收费。向他人分发修改版本时，必须按 GPLv3 提供对应源码、保留版权与许可证声明，并标明修改。仅自行修改使用，无需向公众发布源码。软件按现状提供，不附带担保。

发布可执行版本时，应同时提供对应版本的完整源码及构建脚本；在 GitHub Releases 发布时请使用对应源码标签，不要只上传 EXE。独立第三方组件遵循各自许可证。
