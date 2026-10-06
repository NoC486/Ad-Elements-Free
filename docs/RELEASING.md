# 发布 Windows 安装包

需要 Windows、.NET 8 SDK、Inno Setup 6.7 或更高版本的编译器。仅从官方来源获取编译工具。

1. 修改 csproj 中的 Version、AssemblyVersion、FileVersion，并编译/测试。
2. 使用干净的 `artifacts/publish/win-x64` 目录，运行：

```powershell
./installer/Build-Release.ps1 -Compiler 'C:\Path\To\Inno Setup 6\ISCC.exe'
```

3. 安装包和便携 ZIP 生成于 `artifacts/release`，它们包含 .NET 运行时和项目许可证。
4. 安装验证可用 `/DTestBuild` 编译隔离版本，再用 `/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /NOICONS /DIR="测试目录"` 安装。TestBuild 使用不同的 AppId 和互斥锁，只用于测试，绝不作为发布附件。
5. 验证测试目录的 EXE、许可证、版本和卸载。保留原有用户配置，不终止用户正在运行的客户端。
6. 提交构建脚本和全部源码，再创建与应用版本一致的 `vX.Y.Z` 标签。通过 `git archive --format=zip --output=... vX.Y.Z` 生成对应源码包。
7. 给安装包、便携包及源码包生成 SHA-256 清单，作为同一 GitHub Release 的附件发布；Release 标签必须指向本次构建源码。

安装仅针对当前用户，不提权、不设置开机启动，不安装服务或驱动。首次安装默认优先 `D:\Program Files\Ad Elements Free`，其次按盘符顺序选择 E–Z 中的固定磁盘；没有非 C 盘固定磁盘时回退到当前用户的 Programs 目录。可移动磁盘和网络盘不自动选择。升级保留先前安装路径。安装和升级均显示目录选择页，允许改为当前用户可写的位置。卸载提示先关闭 Provider，保留 `%LOCALAPPDATA%/AdElementsFree` 中的设置和恢复记录。

项目未配置代码签名证书。发布说明须如实说明安装包未签名，不声称已获微软或反作弊厂商认证。
