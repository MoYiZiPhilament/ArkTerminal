# ArkTerminal

面向 Windows 的原生 WPF Shell / Agent 工作台，使用 ConPTY 驱动真实终端进程，以 DirectWrite 和 Direct3D 共享表面绘制终端内容。

当前已实现阶段 1–9 的功能，处于开发与补充验收阶段。Windows 10、跨显示器、其他输入法、真实 Agent 模型任务及 MSIX 等场景仍需验证；当前源码快照不代表最终发行验收通过。完整状态见 [项目进度](PROGRESS.md)。

## 现有功能

- 独立终端会话、多标签、顶部 / 底部 / 左侧标签栏、会话重启与未启动布局恢复。
- CMD、Windows PowerShell、PowerShell 7 和 Git Bash；Shell 集成能力按实际后端提供。
- Unicode 字簇、中文输入、选择复制、搜索、回滚、临时字体缩放与辅助显示。
- 深浅主题、Mica / Acrylic 能力降级、背景图、极光、轻动效与性能模式。
- 分类设置、独立设置 / Profile 草稿、快捷键、性能面板与脱敏诊断。
- 可靠 Shell 命令状态、失焦通知、需显式开启的有界命令历史与会话转录、普通运行模式下的 UAC 管理员会话。
- Agent CLI 的有限路径检测、按需版本探测、配方、启动预览及经审核的 JSON 适配器。
- 有界上下文预览、复制交接、手动 Markdown 快照、独立命令审阅与五个只读 MCP 工具。

Agent 适配器的声明、版本检测和受控启动已实现；具体 CLI 的登录、TUI 与模型任务需分别验收。DeepSeek Harness 的默认适配器明确不提供未经验证的内置交互 TUI 配方。

## 构建与运行

使用 Windows 和 .NET 10 SDK；项目目标为 Windows 10/11 x64，当前验证环境为 Windows 11。源码依赖由 `packages.lock.json` 固定。

```powershell
git clone https://github.com/MoYiZiPhilament/ArkTerminal.git
cd ArkTerminal
dotnet build ArkTerminal.sln -c Release -p:RestoreLockedMode=true
dotnet run --project ArkTerminal.csproj -c Release --no-build
```

便携模式：

```powershell
dotnet run --project ArkTerminal.csproj -c Release --no-build -- --portable
```

首次启动停留在新建页，选择 Profile 后才启动 Shell / Agent。恢复布局也只创建未启动的占位标签。

标准模式的数据根为 `%APPDATA%\ArkTerminal`；便携模式的数据根为可执行文件目录下的 `data`，也可通过同目录的 `portable.flag` 启用。两种模式的数据相互隔离。真实 MSIX 包的安装与权限验收仍待完成，包身份下管理员启动保持禁用。

## 上下文与 MCP

应用菜单、命令面板或 `Ctrl+Shift+F9` 打开上下文工作流。交接前可检查并编辑最终正文；当前对 Agent 的交接采用用户手动粘贴，复制不代表执行。

MCP 桥位于构建输出的 `Mcp` 子目录。请保留完整输出目录，并从运行中的应用复制当前实例的客户端配置。MCP 默认没有授权，只允许显式选择的单个会话和字段，不能向终端输入或执行命令。配置与授权不跨应用重启继承。完整说明见 [上下文与只读 MCP](Examples/readonly-mcp.md)。

应用历史每次运行默认关闭，转录每个新会话默认关闭。上下文的凭据替换是尽力处理，分享前仍需检查最终预览。

## 源码与文档

| 路径 | 职责 |
| --- | --- |
| `ArkTerminal.csproj` | WPF 窗口、工作台与应用编排 |
| `ArkTerminal.Core/` | 配置、Profile、恢复、存储、记录、上下文与只读契约 |
| `ArkTerminal.Terminal/` | ConPTY、VT、缓冲、Shell / Agent、提权与只读 IPC |
| `ArkTerminal.Mcp/` | 独立 stdio MCP 桥及依赖锁文件 |
| `Rendering/`、`Input/` | DirectWrite / Direct3D 渲染及 WPF / TSF 输入 |
| `Selection/`、`Search/`、`Sessions/`、`Visuals/` | 选择、搜索、标签会话与视觉效果 |
| [PROGRESS.md](PROGRESS.md) | 阶段状态、验证范围、待办与 Git 管理约定 |
| [AGENTS.md](AGENTS.md) | 开发约束、架构细节与历史验收记录 |
| [Prompt/](Prompt/) | 十个阶段的实施需求；需求不等同于完成记录 |
| [Examples/](Examples/) | 设置、Profile、Agent 适配器及 MCP 使用示例 |

## 许可证

项目采用 [Apache License 2.0](LICENSE)。第三方依赖保留各自许可证，版本见项目文件及依赖锁文件；最终发行包的依赖许可随包检查仍列入阶段 10。
