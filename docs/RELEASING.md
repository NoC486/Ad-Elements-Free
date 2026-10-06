# 发布 Windows 安装包

需要 Windows、.NET 8 SDK、Inno Setup 6.7 或更高版本的编译器。仅从官方来源获取编译工具。

1. 修改 csproj 中的 Version、AssemblyVersion、FileVersion，并编译/测试。
2. 使用干净的 `artifacts/publish/版本号/win-x64` 目录，运行：

```powershell
./installer/Build-Release.ps1 -Compiler 'C:\Path\To\Inno Setup 6\ISCC.exe'
```

3. 安装包和便携 ZIP 生成于 `artifacts/release/版本号`，它们包含 .NET 运行时和项目许可证。
4. 安装验证可用 `/DTestBuild` 编译隔离版本，再用 `/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /NOICONS /DIR="测试目录"` 安装。TestBuild 使用不同的 AppId 和互斥锁，只用于测试，绝不作为发布附件。
5. 验证测试目录的 EXE、许可证、版本和卸载。保留原有用户配置，不终止用户正在运行的客户端。
6. 提交构建脚本和全部源码，再创建与应用版本一致的 `vX.Y.Z` 标签。通过 `git archive --format=zip --output=... vX.Y.Z` 生成对应源码包。
7. 给安装包、便携包及源码包生成 SHA-256 清单，作为同一 GitHub Release 的附件发布；Release 标签必须指向本次构建源码。

默认全用户安装，仅安装器申请管理员权限，以写入受保护的 Program Files。首次安装优先 D 盘，其次 E–Z 固定磁盘，无非 C 盘固定磁盘时使用系统 Program Files。用户可选择当前用户安装，不提权，默认使用用户 Programs 目录。两种模式均保留目录选择页。升级应沿用旧版安装模式和目录，避免产生两份安装。主程序 manifest 仍为 asInvoker；提权安装结束后不自动启动主程序，用户从快捷方式正常启动。安装不添加服务、驱动或开机启动，不调整目录 ACL。卸载前关闭所有客户端开关，保留用户设置和恢复记录。

测试时以绝对路径给 ISCC 的 `/O` 参数指定输出目录，避免相对路径落到项目外。使用 `Test-Installer.ps1` 验证当前用户模式，加 `-AllUsers` 验证 `D:\Program Files\Ad Elements Free Installer Test`；后者会弹出 Windows 权限请求，并仅安装/卸载独立测试产品。

项目未配置代码签名证书。发布说明须如实说明安装包未签名，不声称已获微软或反作弊厂商认证。
