# 贡献指南

## 开发环境

Windows、.NET 8 SDK。克隆后在仓库根目录运行：

```powershell
dotnet build AdElementsFree.sln -c Release -m:1
dotnet run --project tests/AdElementsFree.Tests -c Release
dotnet run --project tests/AdElementsFree.WindowsTests -c Release
```

Windows 集成测试会短暂创建窗口和托盘，测试快捷方式仅位于测试输出目录。不要使用真实用户快捷方式作为自动测试夹具。

## 新增客户端

1. 在 `src/AdElementsFree/Providers/客户端名称/` 新增实现，遵循 `Core/IAdProvider.cs`。
2. 将 CSS 放在 `src/AdElementsFree/Rules/客户端名称/style.css`，并在 `RuleFiles.Clients` 注册同名目录；写明来源及授权。
3. 由 Provider 决定进程识别、页面白名单、端口、规则和启停行为。
4. 在 `App.xaml.cs` 的注册入口添加实例；不要将特定客户端逻辑加入通用 UI、托盘或设置代码。
5. 补充身份拒绝、恢复和取消相关测试，并记录真实客户端版本及验证结果。

各客户端的 CSS 分别位于 `src/AdElementsFree/Rules/客户端名称/style.css`，构建后按相同目录结构复制到程序旁。只更新现有客户端规则时，将 CSS 提交到 `main` 即可，用户通过设置中的“同步最新规则”获取，不必改变软件版本。新增客户端 Provider 则需要发布新软件版本。

规则应使用标准 CSS；浏览器过滤器格式中的域名和 `##` 前缀不属于 CSS，应转换为选择器及样式声明。即使只改变选择器，也需检查实际页面，尤其是依赖 DOM 顺序的规则。同步时先解析一个提交 SHA，再从该提交下载所有已注册客户端规则，避免混用不同提交的文件。

## 必须遵守的边界

- 不操作游戏进程或反作弊组件；不加入注入、内存读写、Hook、驱动或调试器附加。
- 不修改客户端安装文件；正常使用无需管理员权限。
- 仅连接经身份核验的本机端口；未知身份拒绝执行。不放宽到通配 Origin。
- 保留用户已有参数和设置；修改快捷方式前写恢复记录。
- 不收集聊天、账户、Token、Cookie；日志只记录必要状态。

## 提交

说明问题、变更行为、验证方式和已知限制。不要提交 `bin/`、`obj/`、`artifacts/`、本地 SDK、日志或个人配置。发布包应独立于源码提交。

本项目采用 GPL-3.0-only。贡献代码、规则或素材前，请确保你有权按此许可证提供它们；第三方内容需保留来源及兼容许可证。不要将无明确授权的规则直接复制进项目。
