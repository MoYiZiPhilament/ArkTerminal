# ArkTerminal 本地分发与手动维护

版本 `1.2.4a`，包版本 `1.2.4`。当前为主要版本，未通过最终发行验收。
显示版本和发行标签使用 `1.2.4a`，NuGet / .NET 构建版本使用兼容的
`1.2.4-a`，项目数字版本为 `1.2.4`。

版本规则：数字部分为 `x.x.x`；`a` 为主要版本，`b` 为测试版本（替代原 `preview`），`c` 为特殊处理版本。产品显示与包文件名保留字母后缀，项目数字版本写三段。仅 MSIX 系统清单使用 Windows 强制要求的四段数字；二进制的固定宽度底层元数据由编译器规范化。

## 工具链与生成

需要 Windows x64、PowerShell 7.4+、.NET SDK 10.0.400 和 Windows SDK 的 x64
`makeappx.exe` / `signtool.exe`。应用依赖由 NuGet 锁文件固定；运行时由
`Directory.Build.props` 固定为 .NET / WindowsDesktop 10.0.11。ZIP 与 MSIX
都携带运行时，无需另外安装 .NET；构建仍需 SDK。没有 trimming、AOT 或单文件。

```powershell
dotnet build ArkTerminal.sln -c Debug -p:RestoreLockedMode=true
dotnet build ArkTerminal.sln -c Release -p:RestoreLockedMode=true
pwsh -File Packaging/Build-Packages.ps1 -WindowsSdkBin 'C:\Program Files (x86)\Windows Kits\10\bin\10.0.28000.0\x64' -DevelopmentSign
```

默认输出 `bin/Packages/1.2.4a/`；已有同名产物时停止，可另传
`-OutputDirectory`。脚本从干净暂存目录 publish，再生成 ZIP、MSIX、
`distribution.json` 与 `SHA256SUMS.txt`，不包含本机 `data`、配置、PDB 之外的
临时探针或签名私钥。MCP 位于 `Mcp` 子目录，提权辅助器使用同一
`ArkTerminal.exe --elevated-host` 入口，须保留完整目录。

本轮使用微软 NuGet 包 `Microsoft.Windows.SDK.BuildTools 10.0.28000.2957`
中的 x64 工具，包 SHA-256 为
`b7163fc6f636172726d0985963e4f7f11c41afe1c7fdfe78f8051877360ed696`。
它是构建工具，不是应用依赖，未装入发行包；受
[Windows SDK 许可](https://aka.ms/WinSDKLicenseURL)约束。可由已安装的相同版本
Windows SDK 提供工具，也可从微软官方 NuGet 包解压后把 `bin/10.0.28000.0/x64`
传给脚本。生成包可复现，时间戳、开发证书和签名每次不同，文件散列不保证相同。

## MSI 安装包

同一 `1.2.4a` Release 另提供 `ArkTerminal-1.2.4a-win-x64.msi`
和独立的 `SHA256SUMS-msi.txt`。MSI 直接封装同版本
便携 ZIP 的程序文件，不重新编译应用；去掉 `portable.flag` 和空 `data`，
补充 MSI 元数据、维护说明及安装界面组件的许可 / 对应源码。

双击 MSI 后按安装界面操作。它仅支持当前用户，固定安装到
`%LOCALAPPDATA%\Programs\ArkTerminal`，提供开始菜单入口和系统卸载入口，
不自动启动应用。普通运行使用 `%APPDATA%\ArkTerminal`，没有 MSIX 包身份。
它与已有标准 exe 共享该数据根，与 ZIP 的便携数据隔离；不自动迁移数据。
升级、修复或卸载前先退出应用并备份数据；卸载只清理 MSI 管理的程序、快捷方式
和安装登记，不删除 APPDATA 下的配置 / 快照 / 记录。

产品名称和文件名使用完整 `1.2.4a`，应用仍显示该版本。
Windows Installer 的 `ProductVersion` 按其三段数字规则使用 `1.2.4`；
后续 MSI 自动大版本替换要求前三段至少一段增加，同数字版本不视作升级。
MSI 没有 Authenticode 签名，不使用 MSIX 的开发证书，也不导入证书或安装运行时。
当前验证覆盖 ICE 校验、行政解包、逐文件比对及解包后的程序启动；
干净环境的实际安装、修复、卸载与升级仍待验收。

构建需要固定的 [WiX CLI 4.0.6](https://www.nuget.org/packages/wix/4.0.6)
和 UI 扩展 4.0.6，均为构建工具，不增加应用 NuGet 依赖：

```powershell
dotnet tool install wix --version 4.0.6 --tool-path obj/Tools/wix4 --allow-roll-forward
obj/Tools/wix4/wix.exe extension add WixToolset.UI.wixext/4.0.6
pwsh -File Packaging/Build-Msi.ps1
```

在仓库根目录执行上述命令，先生成或放入同版本便携 ZIP。已有同名 MSI 时脚本停止。
默认输出仍是 `bin/Packages/1.2.4a/`，可用 `-PortableZip` 指定输入 ZIP，
`-WixPath` 指定同版本工具。脚本执行标准 ICE 校验，只排除纯当前用户布局的
预期 ICE91 提示，不排除其他校验项。安装界面包含未修改的 WiX UI 打印许可辅助器，
其 MS-RL 许可、对应提交的完整源码归档及散列随 MSI 携带，见
[MSI-NOTICES.md](MSI-NOTICES.md)。源码首次下载到 `obj/Tools` 缓存，后续构建核对 SHA-256。

## 系统范围与签名

包清单的技术下限为 Windows 10 build 17763（ConPTY 引入版本）；.NET 10
的 Windows 支持还受系统版本、版本类型及维护周期限制，见
[.NET 10 支持系统](https://github.com/dotnet/core/blob/main/release-notes/10.0/supported-os.md)。
这里只在 Windows 11 build 26300.9711 测试过；17763 不是已经通过验收的最低版本。
普通 Windows 10 已结束维护，不能由清单中的 MinVersion 推导其仍受 .NET 官方支持。
Mica / Acrylic 等效果继续按系统能力降级；Windows 10、跨显示器及其他 IME 待实机验证。

MSIX 为 full-trust 桌面包，只声明 `runFullTrust`，不声明 AppContainer 网络或
设备权限。包身份下普通 Shell 的完整运行行为仍待安装验证；MCP 配置入口和
管理员会话沿用明确的未验收限制，**这两项目前不可用，阻塞完整 MSIX 发行验收**。
不得用 ZIP 成功或包清单校验通过替代它们。

省略 `-DevelopmentSign` 产生 `.unsigned.msix`，不能正常安装。
指定后产生 `.development.msix` 和公开的 `ArkTerminal-Development.cer`；
证书明确标识 `ArkTerminal Local Development`，有效期 30 天，不带时间戳。
每次构建生成临时密钥，签名后删除临时 PFX，不使用用户已有签名身份，
不改证书信任存储。这不是生产签名，目标机默认不信任它。
仅在明确准备的隔离测试 Windows 环境，由环境所有者按其策略建立开发信任后
安装验证；不应把开发证书导入日常工作机。脚本没有自动安装、导入证书、上传或商店发布。
生产分发须换成真实发布者身份、匹配的清单和有效签名，另做升级身份与权限验收。

## 数据、备份和手动升级

| 运行模式 | 数据根 |
| --- | --- |
| 标准 exe（无便携标记） | `%APPDATA%\ArkTerminal` |
| 便携 ZIP（含 `portable.flag`） | `ArkTerminal.exe` 同目录的 `data` |
| 安装后的 MSIX | `%LOCALAPPDATA%\Packages\<PackageFamilyName>\LocalState\ArkTerminal` |

三种模式默认隔离，不自动迁移。配置位于 `Config`，恢复/草稿位于 `Recovery`，
背景副本位于 `Backgrounds`；手动快照可另存至用户明确选择的位置。
历史每次运行默认关闭，转录每个新会话默认关闭，MCP 授权不跨重启继承。
所有自动正文记录保持关闭时，不应把目录存在误认为已记录正文。

ZIP：完整解压至可写目录，支持中文和空格。不要从压缩包内直接启动。
只读目录或磁盘不足时显示错误，不偷偷回退到 APPDATA。
升级前关闭全部会话，退出应用并确认辅助进程结束，备份整个 `data`；
把新包解压到新目录，再把备份的 `data` 复制进去后运行。
不要覆盖正在运行的二进制，不只替换 exe，也不要漏掉 `Mcp` 或原生 DLL。
当前配置 schema 为 1；发现更高版本时保留文件并拒绝降级覆盖。
回退应使用旧程序和升级前备份副本，不能假设未来 schema 可逆。

MSIX：备份上述该包数据根，再由用户手动安装同一 Name / Publisher 下更高版本的包。
Windows 会管理包替换与回退；本应用没有后台检查、下载或更新器，不更新 Agent CLI。
未提供 `.appinstaller` 或自动检查策略。当前仅生成一个版本的预览，真实 MSIX
就地升级、卸载数据清理和文件重定向仍待隔离环境验证。

卸载 ZIP：先退出、备份需要保留的数据，再删除解压目录；另存到其他位置的快照
由用户自行保留或清理。标准 exe 的 APPDATA 数据独立保留。
卸载 MSIX：先备份，再从系统“已安装的应用”卸载；Windows 通常清理包私有数据，
不要依赖卸载后仍能恢复。外部另存文件和其他模式数据不会因此自动迁移。
当前不提供产品内的导入导出；上述复制备份是用户手动文件操作。

## 许可与核验

`LICENSE` 为项目及 Apache-2.0 文本；`THIRD-PARTY-NOTICES.md`、
`Licenses/packages.json`、运行时与 Unicode 许可随两种包分发。
自包含运行时的分发遵循微软运行时包中的许可与第三方声明。
升级运行时需要明确更新固定版本、重新构建和回归，不依赖系统 .NET 自动补丁。
可用 `Get-FileHash -Algorithm SHA256` 与旁边的 `SHA256SUMS.txt` 核对产物。
开发 MSIX 的签名信任状态和 ZIP 的 Authenticode 状态必须分别判断。
当前 ZIP 内 exe 没有 Authenticode 签名；`distribution.json` 用
`msixSignature` 与 `portableExecutableSignature` 分别记录两种状态。
