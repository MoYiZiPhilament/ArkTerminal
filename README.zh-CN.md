# ArkTerminal

**[English](README.md)**

<p align="center">
  <img src="Assets/AppIcon.png" alt="ArkTerminal Camellia 图标" width="96" height="96" />
</p>

Windows 原生终端与 Agent CLI 工作台。基于 WPF、ConPTY 和 DirectWrite / Direct3D，提供真实 Shell 会话、多标签和可定制界面。

项目由 **GPT-6.1 Sol** 和 **Kimi K3.1 Pro** 完成，使用 [Prompt/](Prompt/) 中的分段完成提示词推进实施。应用图标由 **GPT Image 2.5 Sunburst** 绘制。

版本使用 `x.x.x` 加字母后缀：`a` 为主要版本，`b` 为测试版本（替代原 `preview`），`c` 为特殊处理版本。

## 下载与使用

[下载 1.2.2a 主要版本](https://github.com/MoYiZiPhilament/ArkTerminal/releases/tag/v1.2.2a)。面向 Windows x64，自带 .NET 运行时。

| 格式 | 使用方式 | 数据目录 |
| --- | --- | --- |
| 便携 ZIP（推荐） | 完整解压到可写目录，运行 `ArkTerminal.exe` | 程序目录下的 `data` |
| MSI | 为当前用户安装，提供开始菜单和卸载入口 | `%APPDATA%\ArkTerminal` |
| 开发签名 MSIX | 仅供隔离开发验证，默认不受系统信任 | 包私有 `LocalState\ArkTerminal` |

首次启动显示新建页，选择 Shell / Profile 或预览 Agent 配方后启动。恢复布局只还原未启动的标签。应用手动更新，不自动迁移不同模式的数据；升级前请退出并备份数据目录。安装、备份及构建细节见 [分发与维护说明](Packaging/README.md)。

首次启动还会按 Windows 系统 UI 语言选择界面语言（中文系统使用简体中文，其他系统使用英语），并保存当前 Windows 时区。可在“设置 → 配置与 Profile”中修改。后续启动使用已保存选择，不持续跟随系统变化。用户可见时间使用配置时区；存储记录和诊断仍保留 UTC。

`1.2.2a` 为主要版本，增加中英文本地化并以英文 README 为默认展示。Windows 10、跨显示器、物理输入和长期资源验证尚未完成；MSI 的实际安装 / 修复 / 卸载 / 升级仍待验收。MSIX 尚未完成隔离安装验证，包身份下 MCP 和管理员会话目前不可用。完整范围见 [项目进度](PROGRESS.md)。

## 功能

- **终端与会话**：CMD、Windows PowerShell、PowerShell 7、Git Bash；多标签、三位置标签栏、独立会话、重启与布局恢复。
- **输入与阅读**：Unicode 字簇、中文输入、选择复制、搜索、回滚、字体缩放、行号与时间戳。
- **外观与设置**：深浅主题、Mica / Acrylic 能力降级、背景图、活动极光、性能模式、独立设置和 Profile 草稿。
- **Shell 工作流**：命令状态、链接预览、失焦通知、有界历史和转录、普通 / 便携模式下的 UAC 管理员会话。
- **Agent CLI**：有限路径检测、按需版本探测、启动配方与预览、自定义 JSON 适配器审核。
- **上下文与 MCP**：可编辑上下文、复制交接、Markdown 快照、独立命令审阅和五个只读 MCP 工具。

Agent 的检测和启动支持范围取决于适配器声明，版本探测成功不代表登录后模型任务已验收。DeepSeek Harness 默认适配器不提供未经验证的交互 TUI 配方。

## 上下文与隐私

通过应用菜单、命令面板或 `Ctrl+Shift+F9` 打开上下文工作流，检查并编辑最终正文后复制，手动粘贴到目标 CLI。复制不代表提交或执行；凭据脱敏是尽力处理，分享前仍需检查。

MCP 配置从运行中的应用复制，桥位于完整程序目录的 `Mcp` 子目录。每个连接默认无授权，只能读取明确授权的单个会话及字段，不能输入或执行命令；配置与授权不跨应用重启继承。见 [只读 MCP 使用说明](Examples/readonly-mcp.md)。

应用命令历史每次运行默认关闭，转录每个新会话默认关闭；不修改 Agent 全局配置，不收集遥测或自动上传终端正文。

## 从源码构建

需要 Windows、.NET 10 SDK；当前构建使用 SDK `10.0.400`，运行时固定为 `10.0.11`。依赖由 `packages.lock.json` 固定。

```powershell
git clone https://github.com/MoYiZiPhilament/ArkTerminal.git
cd ArkTerminal
dotnet build ArkTerminal.sln -c Release -p:RestoreLockedMode=true
dotnet run --project ArkTerminal.csproj -c Release --no-build
```

隔离试用可在运行命令末尾追加 `-- --portable`。标准模式的数据根为 `%APPDATA%\ArkTerminal`，便携模式也可由程序旁的 `portable.flag` 启用。

```powershell
pwsh -File Packaging/Build-Packages.ps1 -WindowsSdkBin '<Windows SDK x64 工具目录>' -DevelopmentSign
pwsh -File Packaging/Build-Msi.ps1
```

打包还需 Windows SDK 和 WiX 工具链，具体版本及准备步骤见 [打包说明](Packaging/README.md)。默认输出 `bin/Packages/1.2.2a/`，包含 ZIP、MSIX、MSI 及 SHA-256 清单。exe 与 MSI 未做 Authenticode 签名；MSIX 使用临时开发证书。

## 源码与文档

| 路径 | 内容 |
| --- | --- |
| `ArkTerminal.csproj` | WPF 窗口与应用编排 |
| `ArkTerminal.Core/` | 配置、本地化、Profile、恢复、存储、上下文与只读契约 |
| `ArkTerminal.Terminal/` | ConPTY、VT、缓冲、Shell / Agent、提权和只读 IPC |
| `ArkTerminal.Mcp/` | 独立 stdio MCP 桥 |
| `Rendering/`、`Input/` | DirectWrite / Direct3D 渲染及 WPF / TSF 输入 |
| `Selection/`、`Search/`、`Sessions/`、`Visuals/` | 选择、搜索、标签和视觉效果 |
| [AGENTS.md](AGENTS.md) / [CLAUDE.md](CLAUDE.md) | 开发约束与 Claude 开发入口 |
| [PROGRESS.md](PROGRESS.md) | 当前进度、验证范围和待办 |
| [历史交接](docs/IMPLEMENTATION-HISTORY.md) | 各阶段原始实施与验收记录 |
| [Prompt/](Prompt/) | 分段完成提示词；需求不等于完成记录 |
| [Examples/](Examples/) | 配置、Profile、适配器与 MCP 示例 |
| [Packaging/](Packaging/) | 打包脚本、许可与维护说明 |

## 许可证

项目采用 [Apache License 2.0](LICENSE)。第三方依赖保留各自许可证；发行包携带 [第三方声明](Packaging/THIRD-PARTY-NOTICES.md)、运行时和 Unicode 许可及依赖清单。MSI 的 WiX 组件许可与对应源码见 [MSI 声明](Packaging/MSI-NOTICES.md)。
