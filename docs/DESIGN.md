# 设计与安全边界

## 上游审查

OKKO 将定位可执行文件、自动启动、远程下载 CSS、页面判断和 CDP 发送写在单个 Python 脚本内。复用 CSS、4 秒缓冲及当前页面/新文档双重应用思路。重写快捷方式持久化恢复、生命周期、连接管理、响应确认及身份核验。移除运行时规则下载、自动启动 KOOK 和 wildcard Origin。

WPF 比 WinUI 3 少一层 Windows App SDK 部署依赖，适合简单窗口和长期托盘场景。BCL 提供 HTTP、WebSocket、JSON；托盘使用系统 WinForms 组件，快捷方式使用 WScript.Shell COM。没有 Windows Service、驱动、WebView 或第三方运行库。

## 生命周期

开关变更由信号量串行处理，UI 在操作时暂时禁用开关。设置原子写入成功后应用开关；关闭先取消并等待监控结束，再恢复快捷方式。启用时默认每 5 秒读取指定名称的进程和本机 CDP targets；异常退避 10/20/40/60 秒。所有网络操作有超时和取消令牌。

CDP 会话键包含监听 PID、启动时间及 WebSocket target URL，避免 PID 重用及多个子进程造成重复。每个已处理 target 保留一个连接和一个新文档脚本，轮询时不重复执行 Runtime.evaluate。重载时脚本自行处理 DOMContentLoaded，style ID 保证幂等。连接断开或新 target 后重新识别。完全退出 KOOK 会清理旧会话，新实例重新应用。

## 身份与网络

1. `GetProcessesByName("KOOK")` 使用系统进程信息查找名称；只对匹配的 PID 查询路径/启动时间。不周期性打开所有进程句柄。
2. `GetExtendedTcpTable` 读取 IPv4/IPv6 TCP LISTEN 元数据，不打开监听者的进程。非 loopback 绑定即拒绝。
3. 监听 PID 必须来自 KOOK 名称列表；文件名为 KOOK.exe，产品元数据为 KOOK/Kaiheila/开黑啦。其他占用者不打开其进程，直接提示冲突。
4. HTTP 仅访问固定 `127.0.0.1:9222/json`，禁用代理及重定向，有响应大小限制。
5. 页面必须通过 Provider URL 白名单。WebSocket 必须是 `ws://127.0.0.1:9222/devtools/page/...`，不接收用户名、查询串或其他主机。
6. 4 秒等待后重新核对 PID/启动时间，连接后获取 Page.getFrameTree，再核对当前 URL 和端口归属，才执行规则。脚本本身再次检查顶层页面及固定 origin/path，降低导航竞态风险。
7. .NET ClientWebSocket 不发送 Origin，未添加任何 Origin 放行参数。真实 KOOK Chromium 版本仍需实机验证。

身份策略用于避免误操作其他正常 Chromium 应用，并非抵御已控制本机的恶意攻击者：文件产品信息可伪造，端口核验与连接也不是原子操作。不声称密码学认证。未知版本宁可不处理。

## 快捷方式恢复

Windows 引号和反斜杠规则解析出 token，同时保留原始字符范围。原参数有相同端口时不取得所有权；冲突/重复/未闭合引号时不改写。只追加一次指定参数。

记录在修改之前 flush 并原子替换。恢复时先比较 Target；一致且 Arguments 未变则恢复原字符串；用户新增其他参数时，仅移除本程序拥有的确切端口 token。Target 变化、重复 token 或端口改为其他值则保留记录并提示人工检查。不恢复旧 Target，不覆盖用户新设置。用户删除后又自行添加完全相同 token 的意图无法可靠区分，这是最小差异恢复的固有限制。

## 反作弊约束审查

不含 DLL 注入、线程注入、内存读写、全局 hook、图形 hook、调试器附加、驱动/服务、进程隐藏或游戏文件操作。唯一显式 OpenProcess 使用 QUERY_LIMITED_INFORMATION（0x1000），仅限已按名称找到的 KOOK PID，以取得映像路径。不获取 VM_WRITE/VM_READ/ALL_ACCESS，不操作游戏或反作弊服务。

CDP 是 KOOK 自己开启的本机接口；它具有调试能力，所以不应将其描述为零风险。程序不打开监听端口，不放行防火墙，不修改系统安全设置。无法约束用户自行启动 KOOK 时给 Chromium 加入的不安全绑定；检测到非 loopback 监听会拒绝使用并报告。

正常运行 manifest 为 asInvoker，无自动提权。日志仅写状态与异常类型，限制大小。不会记录 CDP 页面正文、账户内容或 URL。
