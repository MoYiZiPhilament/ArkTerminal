# ArkTerminal 开发指南

项目由 **GPT-6.1 Sol** 和 **Kimi K3.1 Pro** 完成；[Prompt/](Prompt/) 为分段完成提示词，图标由 **GPT Image 2.5 Sunburst** 绘制。

本文只保留当前开发约束与架构入口。功能与使用见 [README.md](README.md)，阶段状态见 [PROGRESS.md](PROGRESS.md)，截至 `0.1.1a-preview` 的完整历史原文见 [实施交接归档](docs/IMPLEMENTATION-HISTORY.md)。历史中的“未实现 / 未提交 / 下一阶段”仅描述当时状态，不覆盖当前源码和最新要求。

## 工作方式

1. **先明确再实现**：说明假设和取舍；存在影响实现的歧义时先问。多步任务先给出简短计划及各步检查标准。
2. **保持简单**：只实现当前要求；不增加推测性的功能、配置或单次使用的抽象。
3. **局部修改**：遵循既有风格，不顺手重构或清理无关代码；只移除本次改动产生的无用代码。
4. **以验证为准**：针对实际改动执行检查，区分构建、生产探针与真实环境 / 人工验收。发现失败先修复，不用历史结果代替本轮重跑。
5. **保护工作区**：修改前检查 Git 状态，不 reset、清理或覆盖已有工作。不自动提交、推送或发布，除非用户明确要求；获得授权后使用普通快进同步，不改写历史。

## 版本约定

- 数字部分始终为 `x.x.x`，后缀 `a` 表示主要版本，`b` 表示测试版本（替代原 `preview`），`c` 表示特殊处理版本。
- 显示版本、发行标签及包文件名使用完整版本，如 `1.2.4a`；NuGet 构建版本使用兼容写法 `1.2.4-a`。项目中的数字版本写三段；编译器 / Windows 固定宽度的底层元数据由工具链规范化，不作为产品显示版本。MSIX 系统清单按 Windows 要求使用四段数字，这是已确认的格式例外。

## 当前范围与构建

Windows 10/11 x64 原生 WPF Shell / Agent 工作台，目标框架 `net10.0-windows`。当前显示版本为 `1.2.4a`，NuGet 版本为 `1.2.4-a`，项目数字版本为 `1.2.4`，MSI 产品版本为 `1.2.4`。

阶段 1–9 已有实现，阶段 10 已落地本机修复、测量与预览打包，**最终发行验收尚未完成**。本轮 UI 文案与文档整理不扩大功能或验收结论。

```powershell
dotnet build ArkTerminal.sln -c Debug -p:RestoreLockedMode=true
dotnet build ArkTerminal.sln -c Release -p:RestoreLockedMode=true
dotnet run --project ArkTerminal.csproj -c Release --no-build -- --portable
```

- SDK 当前使用 `10.0.400`；运行时固定 `10.0.11`，图形依赖 Vortice 固定 `3.8.3`，MCP SDK 固定 `ModelContextProtocol.Core 2.3.1`。版本以项目与锁文件为准，不无意升级依赖。
- 仓库没有测试工程。不要擅自添加测试项目、报告目录或脚手架；按改动需要在仓库外运行生产程序集探针，并保留证据。
- 使用独立便携目录验证，避免修改正式用户数据、Agent 全局配置或系统信任存储。
- 打包复用 `Packaging/Build-Packages.ps1` 和 `Build-Msi.ps1`；工具链与许可见 [Packaging/README.md](Packaging/README.md)。不启用 trimming、AOT、单文件或自动更新。

## 代码入口

| 路径 | 职责 |
| --- | --- |
| `AppHost*`、`MainWindow*`、`Controls/` | 应用编排、窗口、设置和交互 |
| `ArkTerminal.Core/` | 平台无关契约、数据根、原子存储、配置、Profile、恢复、记录与上下文 |
| `ArkTerminal.Terminal/` | 独立 ConPTY 会话、解码、VT、屏幕 / 回滚、Shell / Agent 与 IPC |
| `ArkTerminal.Mcp/` | 独立 stdio 只读桥；SDK 依赖只放在该项目 |
| `Rendering/`、`Input/` | 唯一正式图形路径与 WPF / TSF 输入桥 |
| `Selection/`、`Search/`、`Sessions/`、`Visuals/` | 逻辑文本操作、标签和视觉策略 |

## 必须保持的契约

### 会话、启动与数据

- 每个 Session 独立拥有进程、I/O、缓冲和取消源；UI 线程不得解析输出、扫描进程或执行阻塞 I/O。
- 启动停留在新建页；恢复只创建未启动占位，用户主动选择后才创建 Shell / Agent。检测到程序不代表获得启动授权。
- 沿用 `ISession` / `ISessionManager` / `ITerminalBufferReader` 的输入、resize、生命周期与读取契约，不另建终端内核。
- 标准、便携、MSIX 数据根默认隔离，文件操作经统一数据根服务。settings / profiles / recovery 保持 schema 1，原子写入，不静默迁移或降级覆盖高版本配置。
- 设置与 Profile 草稿分别保存；临时字体缩放、性能模式不自动持久化。schema 1 的区域语言 / 时区在首次创建配置时取自系统，之后尊重已保存选择；旧配置缺字段使用系统默认值。回滚默认 5,000 行，同时受默认 64 MiB 存储估算预算约束；这不是进程内存硬上限。
- 用户可见文案统一经 `ArkTerminal.Core/Localization/` 的 `ILocalizationService` 与中英文资源目录，英语为回退；UTC 存储与诊断格式不变，仅显示时间转换到配置时区。

### 图形、输入与逻辑文本

- 唯一图形链为 Direct2D / DirectWrite → D3D11 shared texture → D3D9Ex surface → WPF D3DImage。禁止直接把 D3D11 指针交给 `SetBackBuffer`，不引入 WebView2 / xterm.js 或第二套渲染后端。
- GPU 查询在后台等待，D3DImage 零等待 `TryLock` 的失败分支也需配对 `Unlock`；取消与资源释放遵守既有同步。前缓冲不可用时保留绑定，避免切断恢复通知。
- 保持预乘 alpha、半透明全视口重绘、DPI / 设备恢复、隐藏暂停和文本可读性。动效只操作透明度 / 位移，不动画终端网格或 D3D 表面。
- `Input/WpfTextInputBridge.cs` 是唯一 TSF 输入路径。组合更新不写 ConPTY，commit 只写一次；焦点离开或窗口失活时取消且不提交，候选导航不得穿透 Shell。
- 不以 UTF-16 字符数代替字簇列宽。选区依靠稳定 `LineId` 和列边界，宽字符续格不能独立起选；旧历史行复用时须分配新 ID 并清除元数据。
- 复制保留显式空格，软换行不加换行，硬行使用 CRLF，不复制 ANSI。括号粘贴仅在 Session 声明 2004 模式时启用；搜索和复制快照处理放在后台，并丢弃取消 / 过期结果。

### Shell、Agent、记录与提权

- Shell 集成仅影响当前进程，不改永久 profile / bashrc。OSC、标题、cwd 和输出均不可信，不能用来授权执行或扩大读取范围。
- 无可靠事件时，Agent 内部忙碌 / 等待 / 任务完成状态及耗时保持未知；版本探测不等于真实模型任务验收。
- 路径检测有界，版本进程仅在显式刷新后运行。自定义 JSON 需审核，启用绑定原始字节 SHA-256；改变后撤销，启动前重验。参数使用结构化 argv，不拼接用户值为脚本源码。
- 应用命令历史每次运行默认关闭，转录每个会话默认关闭；旧偏好、恢复、草稿不构成同意。队列或写盘失败停止记录，终端继续运行。不收集遥测或自动上传正文。
- UAC 通过同一 exe 的 `--elevated-host` 角色与受限本机 IPC；核验内核 PID、映像、创建时间、SID 和提升令牌，不信任自报身份。仅支持同一用户提升；MSIX 提权保持禁用。

### 上下文、命令与 MCP

- 仅采集用户所选会话的主动选区或实时尾部，最终正文最多 200 行 / 64 KiB UTF-8，完整编辑预览最多 128 KiB。不读取项目文件、环境值、私有历史、已保存转录或其他会话。
- 脱敏只尽力处理；每次交接确认最终正文、来源、目标与长度。编辑 / 换目标不继承确认，来源关闭后预览失效。现有 Agent 使用复制后手动粘贴，不向运行中程序灌入正文。
- Markdown 快照须用户确认最终文本并主动另存。命令审阅只处理手动贴入的命令，另行确认后在新 PowerShell ConPTY 提交，原会话不接收输入；“已提交”不代表执行成功。
- MCP 默认无授权，每个连接绑定一个会话，cwd / 输出 / 选区分别授权；当前及后续主动选区的披露须明确说明。撤销与读取结束须重检，重启 / 新标签不继承。
- 只暴露 `list_profiles`、`list_sessions`、`get_session_metadata`、`get_output_tail`、`get_selection`，拒绝写入及未知工具。沿用 SDK 协商 / 取消及既有 IPC、队列和消息上限；stdout 只含 MCP JSON-RPC，诊断不收正文、命令或敏感路径。
- 桥仅使用应用同目录 `Mcp` 子目录中的程序，绑定当前 UI 内核身份与寿命。MSIX MCP 配置仍不可用，不生成伪配置。协议、字段和限制见 [Examples/readonly-mcp.md](Examples/readonly-mcp.md)。

## 验收与发布边界

- Windows 10、跨显示器 / DPI、其他 IME、物理长按与输入焦点、真实 GPU 移除 / 磁盘满、长期资源和真实 CLI 登录后模型任务仍有待验项。
- MSIX 开发签名不受默认系统信任；未完成隔离安装、包数据、MCP / UAC、卸载及升级验收。MSI 的 ICE / 行政解包不等于实际安装、修复、卸载或升级通过。
- 不把本机生产探针、版本刷新、复制或历史记录扩大成最终发布验收。证据、日期和具体限制按 [PROGRESS.md](PROGRESS.md) 与历史归档区分。
- 未明确要求时，不扩展到工作区、分屏、多窗口 / Quake、MCP 写入、本地扩展、导入导出或 Skill 编辑器。云同步和自动更新不做。
- UI 优先使用简洁标签，移除不影响理解的说明；保留权限、记录、读取范围、输入语义、失败状态与执行确认等必要提示。
