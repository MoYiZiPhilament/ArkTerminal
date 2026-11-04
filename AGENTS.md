# ArkTerminal 实施交接

## 进度与版本管理（2026-11-04）

- 面向仓库读者的功能 / 构建入口见 `README.md`，阶段总览、验证边界、待办与提交关系见 `PROGRESS.md`。下文保留各工作轮次的历史证据，早期“下一阶段未实现 / 未提交”的状态按当时日期理解，当前范围以本节与“当前状态”为准。
- 本轮用户明确要求整理进度、做好 Git 管理并上传至 `https://github.com/MoYiZiPhilament/ArkTerminal.git`。已有源码按阶段 6–9 的当前快照归档，保留本地阶段 4 / 5 及远端许可证历史；本地主分支统一为 `main`，远端为 `origin`。该授权不代表开展阶段 10 或发布最终发行版。
- 本次锁定依赖的 Release 构建通过（0 警告 / 0 错误）；本轮检查与历史运行时 / 人工验收分别记录，不把历史结果当成本次重跑。

## 当前状态

- 产品目标：Windows 10/11 x64 原生 WPF Shell/Agent 工作台，目标框架 `net10.0-windows`。
- 阶段 1 的数据目录、配置、恢复和 Profile 已存在；阶段 2 的真实 ConPTY、VT 解析、屏幕模型和基础输入已存在。
- 阶段 3（DirectWrite、Unicode、IME、选择/搜索）已完成实现，状态是 **实现完成、相关平台验证待补**，不是最终发布验收通过。
- 阶段 4 已实现单窗口多标签、三位置标签栏、可调宽左侧栏、新建页、最近项目、有限项目识别、命令面板、全局热键、专注模式和未启动布局恢复；本机功能验收已通过，平台兼容与特定场景验证待补，不是最终发布验收通过。
- 阶段 5 已实现主题资源、视觉配置、背景图、活动极光、动效、性能模式和 DWM 能力降级，状态是 **实现完成、相关平台验证待补**，不是最终发布验收通过。Windows 11 build 26300 历史复现的移动后黑屏已在本轮修复并通过正式 exe 回归，材质/真实后景已重新启用；Windows 10 与跨显示器仍未验证。
- 阶段 6 已实现分类设置、独立 Profile 草稿、字体/辅助显示、统一快捷键、性能面板及脱敏诊断。状态为 **实现完成、相关验证待补**：本机生产程序集探针已通过，物理键盘与不同输入焦点的完整回归、磁盘不足及跨平台验证尚未完成。
- 阶段 7 已按用户明确要求实现 Shell 集成、链接、失焦通知、有界记录与真实 UAC 辅助进程，状态为 **实现完成、相关验证待补**。本机生产 Session / IPC 探针已通过真实提权、取消、辅助器退出、UI 崩溃回收、握手超时与 ZIP 中文 / 空格路径检查；用户已确认长任务通知可见且无声音。MSIX 提权保持禁用，完整链接打开人工验收、物理磁盘满和跨平台验证待补。
- 阶段 8 已按用户明确要求实现 Agent CLI 的有限检测、按需版本探测、缓存、JSON 审核启用、路径覆盖、配方与同内核启动。状态为 **实现完成、相关验证待补**：本机版本 / 受控 CLI / WPF 检查通过，真实 CLI 的登录后模型任务与 Windows 10 等场景未验收，不是最终发布验收通过。
- 阶段 9 已按用户本轮要求实现内置上下文工作流、可编辑预览、复制 / 辅助 Agent 交接、手动 Markdown 错误快照、命令辅助与独立只读 MCP。状态为 **实现完成、相关验证待补**：本机生产程序集 / 真实 stdio / ConPTY 检查通过，原生另存步骤经用户操作及逐字比对；MSIX、真实 CLI 模型任务与跨平台验收仍待补，不是最终发布验收通过。
- 不要自动开始阶段 10 或后续功能。阶段 3/4 的跨环境验证，以及阶段 5/6/7/8/9 的 Windows 10、跨显示器和其他 IME 验证仍需补齐。
- 不要 reset、清理或覆盖用户现有工作；不要自动 commit、push 或发布，除非用户明确要求。

## 阶段 9：内置上下文、命令辅助与只读 MCP（2026-11-04）

- 应用菜单、既有命令面板的 `ark-terminal-context` / `context.snapshot` / `context.explain` / `context.repair` / `context.review-command` / `mcp.manage` 提供入口；复用原 `GlobalHotkeyService` 注册固定 `Ctrl+Shift+F9`，被占用时显示降级，菜单 / 面板仍可用。呼出上下文会恢复最小化窗口。没有第二套面板 / 热键服务，不将该工作流声称为七个 CLI 通用的原生 Skill 文件，不自动写入任何 CLI 配置。
- `ArkTerminal.Core/Context/` 统一正文上限、控制序列清理、常见凭据脱敏、Markdown 和快照导出；`Sessions/TerminalContextCollector.cs` 实现原有 `IContextCollector`，经 UI 只取得来源引用 / 安全元数据 / 选区范围，再后台读取。`TerminalScreen.CaptureBoundedText` / `ConPtySession.CaptureBoundedText` 是增量入口，最多复制 200 个物理行 / 65536 个 UTF-16 单元，不复制整份回滚；最终正文按 .NET 字形簇限制为 200 行 / 64 KiB UTF-8，软换行可能使实际逻辑行更少，带截断与替换计数。完整可编辑预览最多 128 KiB UTF-8。普通启动进程名通过已有内核进程句柄查询，查询不可用 / 管理员远端未取得时显示未知，不从标题或 stdout 猜进程 / Agent 状态。
- 采集仅限所选来源的当前主动选区或有限实时尾部；不读项目文件、环境值、启动脚本 / 敏感参数、私有历史、保存的转录或其他会话。cwd 标明启动快照或不可信 Shell 元数据。授权数据仍可能包含未识别秘密，脱敏只尽力处理；Bearer / Basic、JSON 引用键、含空格 / 转义引号及不完整引号的已列模式另有完整值检查，不宣称万能识别。
- `Controls/ContextWorkflowWindow.cs` 每次操作重新展示最终正文、来源、目标与长度，编辑 / 换目标不继承确认；来源关闭后预览失效。复制只报告已复制，不报告被 CLI 接收 / 提交。现有 Agent 输入状态未验证，全部采用复制降级；显式新建辅助 Agent 复用阶段 8 的配方 / 启动预览与原有 UAC，再复制供用户手动粘贴，不向正在运行的程序灌入正文。解释 / 修复入口准备给所选 CLI 的请求，未自动取得建议，不伪造模型输出。
- 手动快照先确认最终 Markdown，再使用原生另存对话框；默认目录经新增的 `IAppDataPaths.SnapshotsDirectory` 位于当前独立数据根 `Snapshots`，也可由用户明确另选导出位置。目录准备和写盘在后台，保存期间冻结预览，使用同目录临时文件替换；取消不留下持久正文文件，Markdown 代码不执行。备注直接编辑于最终预览。
- `Controls/CommandReviewWindow.cs` 为手动贴入的实际命令提供规则提醒、PowerShell Profile / cwd 和独立执行确认。确认后在新的同内核 ConPTY 通过 `-NoProfile -NoExit -EncodedCommand` 提交显示原文；原会话没有输入副作用，管理员仍走原确认和 UAC，CMD / Bash 或未验证输入模式提供复制降级。规则未命中不表示安全，“已提交”不表示成功执行。诊断只记录准备、复制、保存、取消 / 失败、命令提交、MCP 允许 / 拒绝与授权变化等枚举事件 / 计数，不收正文、备注、命令、cwd、会话 ID 或导出路径。
- 新增必要的 `ArkTerminal.Mcp` stdio 可执行项目，固定官方 **ModelContextProtocol.Core 2.3.1，Apache-2.0**，带 `packages.lock.json`；其传递依赖为 Microsoft.Extensions.AI.Abstractions 10.11.0、Logging.Abstractions / DependencyInjection.Abstractions 10.0.13（MIT），已检查安装包元数据。UI / Core / Terminal 不引入 SDK 依赖。使用 SDK 的 JSON-RPC 类型 / 序列化 / 服务端 / 协商 / 取消 / 分派，自有传输只做换行帧及资源限制，不是仅识别 tools/call 的替代协议。核实源和使用说明见 `Examples/readonly-mcp.md`。
- `ProtocolVersion=null` 保留 SDK 支持：2024-11-05 / 2025-03-26 / 2025-06-18 / 2025-11-25 用 initialize / initialized，2026-07-28 用逐请求元数据及 server/discover。本机五版均实际列工具，2026 另验证工具调用仍拒绝未授权，未知未来协议沿用 SDK 的协商 / 错误。stdout 只有 UTF-8 单行 MCP JSON-RPC，诊断只到 stderr 且不收正文，无 LSP Content-Length。
- `Core/Mcp/` 和 `Terminal/Mcp/` 提供版本 1 的内部只读 IPC。复用阶段 7 的当前用户 SID DACL、内核远程拒绝、首实例保护和内核 PID / 映像 / 创建时间 / SID 核验；新增可选多实例参数保持提权默认行为。UI 仅接受自身目录 `Mcp/ArkTerminal.Mcp.exe`，桥验证所绑定 UI 内核对象，握手再核对随机实例 ID；客户端自报名仅显示且不可信。桥不直接读保存文件，UI 经当前 SessionManager / View 提供字段白名单。
- 每个连接默认无授权，UI 单独选一个固定 SessionId 和字段。Profile 列表只给该会话关联 Profile 的 ID / 名称 / 类型；会话列表只给该 SessionId / 状态 / Profile 引用；元数据只给安全字段，cwd 额外勾选；输出与选区各有独立权限，选区 checkbox 明确披露当前及后续主动选择的内容。撤销和读取结束重检阻止捕获过程中的旧授权结果；新标签、重启、恢复占位与项目共享不继承范围。关闭 / 缺失、无选区、无授权和字段拒绝状态区分。
- 只注册 `list_profiles`、`list_sessions`、`get_session_metadata`、`get_output_tail`、`get_selection`；`send_text` / `send_input` / 未知工具稳定拒绝。只读工具不能打开上下文工作流、触发粘贴、创建会话或执行命令。工具结果为 MCP 文本内的 `{status,data}` JSON，正文附 `truncated` / `redactions` / `utf8Bytes` / 上限 / `untrusted=true`，schema 与字段见使用文档和运行时 tools/list。
- 限制集中于 `ContextLimits` / `McpLimits`：入站 stdio 128 KiB，出站序列化消息 1 MiB（处理 64 KiB 正文的双重 JSON 转义，正文权限上限未提高）；传输队列 8，最多 4 个在途请求，工具并发 2，单桥 IPC 一次一个，UI 最多 4 个连接。IPC 请求 4 KiB / 响应 256 KiB / JSON 深度 8，握手及收到首字节后的剩余帧 / 请求读取 5 秒。串行请求取消后断开该桥以防迟到响应串线，其他桥 / 会话保留；UI 退出取消并有最后 2 秒进程退出期限，处理 Windows 重定向 stdio 不响应取消的情况。
- 主项目构建 / 本地发布携带 `Mcp` 子目录及其依赖；配置界面验证主 exe / 桥文件存在后生成含完整路径和结构化 args 的通用 mcpServers JSON，路径空格 / 中文无需 Shell 引号。客户端须按自己的格式手工对应 command / args，不自动改全局配置。配置绑定当前 UI 实例 / PID / 创建时间 / 映像，重启须重新复制，授权不持久化。MSIX 启动入口没有实装 / 清单验收，明确不提供伪配置，须阶段 10 复验；禁用复制不算 MSIX 通过。
- `ISession` / `ISessionManager` / `ITerminalBufferReader` 的既有签名保持；上下文请求 / 快照只做增量字段，新增安全读方法和路径属性，settings / profiles / recovery 保持 schema 1。没有测试工程、仓库报告目录或脚手架；没有 commit / push / 对外发布，没有改正式用户配置。只完成阶段 9。

### 阶段 9 验证与交接边界

- 环境：本机 Windows build 26300.9711、.NET SDK 10.0.400、.NET / WindowsDesktop runtime 10.0.11。最后 `dotnet build ArkTerminal.sln -v:minimal` 退出码 0、0 警告 / 0 错误；`git diff --check` 通过。本地 `dotnet publish ArkTerminal.csproj -c Release -o <隔离中文 / 空格目录>` 成功，不是对外发布。
- 外部 Roslyn / WPF / 真实 ConPTY / 独立桥探针直接加载最终生产程序集，最后 `checks-result.json` complete=true、196 项通过 / 0 失败 / 退出码 0。另存对话框由用户点击保存，先前同版本 UI 的导出文件与确认预览逐字相等，该单项保留于 `native-save-result.json`；之后完整值脱敏测试已补强。发布目录与正式 exe 启停另有 `publish-result.json`，14 项通过 / 0 失败。证据集中于 `C:\Users\1\.codex\visualizations\2026\11\03\01a1a21d-61bc-75a2-9e09-dcfca895f6ff\stage9-checks`，不是产品自动记录正文。早期桥退出缺陷、测试请求漏带新协议元数据、外部编译参考程序集选择和发布相对路径冲突的失败已修复，不计成功。
- 生产 WPF 的正常 / 最小尺寸布局有上下文及授权面板截图检查。Computer Use 读取 dotnet 测试窗口被自动审批拒绝，未以别的桌面工具绕过；原生另存由用户操作并核对文件，不能称为自动点击验收。正式发布 exe 在隔离便携目录实际停留新建页，直接子进程为 0，正常关闭退出码 0 并清理 running.marker。中文 / 空格目录中的实际桥完成 initialize / 五工具 / 内核进程退出绑定。

| 编号 | 状态 | 实际证据与限制 |
| --- | --- | --- |
| 09-AC01 | 通过（本机支持范围） | 200 行 / 64 KiB、超长正文、无会话 / 选区、字形簇边界、ANSI / OSC / 方向字符、完整合成凭据替换及来源隔离；不宣称全秘密 / 全 Unicode 一致性。 |
| 09-AC02 | 通过（本机生产预览） | 取消保全剪贴板、最终编辑文本逐字复制、改变目标后重看本次目标、来源关闭后拒绝传递；预览 / 取消无持久正文。 |
| 09-AC03 | 通过（本机复制降级） | A 来源预览指向 B，复制没有增加 B 的实际输入字节；输入状态未知时只复制，辅助 Agent 复用既有确认启动，不自动提交正文。七类真实 CLI 的手动粘贴、登录后模型任务 / 新辅助任务未验。 |
| 09-AC04 | 通过（本机，用户协助原生对话框） | 手动另存带元数据 / 范围 / 截断说明 / 用户备注的 Markdown，文件逐字等于最终展示文本；没有执行其中代码。 |
| 09-AC05 | 通过（本机普通 PowerShell） | 取消后无新会话 / 标记；单独确认后真实新 ConPTY 执行合成标记命令，状态只报告提交；解释 / 修复请求不构成执行同意。管理员、CMD / Bash 自动执行与真实模型回复未验，后两 Shell 使用复制降级。 |
| 09-AC06 | 通过（真实本机 stdio） | 四版 initialize 协商、2026 discovery / 列工具 / 未授权调用、合法 null-id 解析错误与未知方法错误；stdout 每行都是 JSON-RPC，stderr 不含合成正文。 |
| 09-AC07 | 通过（本机 UI / 服务与 IPC） | 默认拒绝、单会话列表、跨会话拒绝、cwd 不附带、选区另授、撤销后拒绝及捕获中撤销抑制正文；客户端名称不授权，错误 UI 创建时间拒绝；已关闭稳定状态。未做真实其他用户 / MSIX 身份验收。 |
| 09-AC08 | 通过（本机） | 五工具列表无写入口，send_text / send_input / execute / 未知工具拒绝；实际会话输入字节不增加。 |
| 09-AC09 | 通过（本机支持范围） | 长消息、64 KiB 极端转义正文、12 并发请求、4 桥 / 第 5 拒绝、SDK 取消、无 UI、单桥退出保留其他会话、主进程退出后桥 4 秒内消失；UI 退出 / 正常关闭资源清理。未替代跨系统压力验收。 |

- 平台与产品缺口：MSIX 的实装启动入口 / 清单 / 包权限仍阻塞相应发行验收；Windows 10、跨显示器、其他 IME、实际物理快捷键与此前 GPU / 磁盘满缺口沿用各阶段边界。真实 CLI 的登录后交接 / 模型任务没有运行，不把受控进程、复制或准备请求当作七类产品任务成功；当前普通 / 便携本机实现可用，仍不是最终发布验收通过。
- 阶段 10 可复用 `IContextCollector` / `ContextSnapshot`、`ContextText` / `ContextDocument` / `ContextSnapshotExporter`、`ReadOnlyMcpHost` / `McpBridgeClient`、`McpReadGrant` / `McpReadFields` / 五工具输入 schema，以及 `Examples/readonly-mcp.md` 的客户端入口。必须在最终 ZIP / RID / 自包含 / MSIX 中重验伴随桥、固定依赖、许可、实例配置、当前用户 / 内核身份、权限撤销、取消 / 退出和日志无正文；保留历史 / 转录默认关闭与恢复不启动。不得扩大 MCP 为写接口，不自动开始阶段 10。

## 阶段 8：Agent CLI 检测与快速启动（2026-11-02）

- 新建页的 `Agent CLI` 区提供检查路径 / 重载 JSON、刷新版本、取消、路径覆盖、模型 / CLI 权限 / 参数 / Shell 后端编辑、实际命令预览、快速启动与创建 Profile 草稿。命令面板提供 `agent.detect` 与 `agent.recipe.<id>` 入口。快速启动的配方只在本次会话内保留，可重复 / 重启；未保存配方不写入无法恢复的 Profile 引用。需跨运行复用时创建并保存 Profile，恢复仍只创建未启动占位，不恢复任务正文。
- `ArkTerminal.Core/Agents/BuiltInAgents.cs` 保存核实依据和七类候选，候选顺序为 `.exe`、`.cmd`、`.ps1`。实际优先级是用户路径覆盖 → JSON 的 overridePath → PATH（最多 128 个目录，不查 cwd / 显式 UNC）→ 9 个固定常见目录；不递归或扫描全盘。下表依据已实际读取，核实日期为 2026-11-02。产品版本随安装变化，表中只记录本机最终探测结果。

| ID / 显示名 | 命令 / 版本探测 | 本机最终状态 | 核实依据与能力边界 |
| --- | --- | --- | --- |
| codex / Codex | `codex --version` | 路径 / 版本已验证：0.156.1 | https://developers.openai.com/codex/cli/reference/ ，版本参数另见 https://developers.openai.com/cookbook/examples/codex/using_goals_in_codex 。交互入口、--model、--sandbox、--no-alt-screen、--add-dir 已按官方声明映射。 |
| deepseek-harness / DeepSeek Harness | `dsh --version` | 不支持内置交互配方，本机未发现文件 | https://github.com/deepseek-ai/deepseek-harness/blob/master/apps/cli/README.md 。官方 dsh 的随附模式是 Web / headless / SDK / ACP，TUI 依赖另装 Profile；不运行 npx、Web 或安装流程充当终端 Agent。已安装专用 TUI 的用户可审核启用自己的 JSON。 |
| claude-code / Claude Code | `claude --version` | 路径 / 版本已验证：2.1.248 | https://code.claude.com/docs/en/cli-reference 。交互入口、--model、--permission-mode、--continue、--add-dir。 |
| kimi-code / Kimi Code | `kimi --version` | 路径 / 版本已验证：2.1.1 | https://www.kimi.com/code/docs/en/kimi-code-cli/reference/kimi-command.html 。交互入口、--model、--plan、--continue、--add-dir；不把其他 CLI 的权限开关套用到 Kimi。 |
| opencode / OpenCode | `opencode --version` | 路径 / 版本已验证：v2.0.32（本机为 .cmd 包装器） | https://opencode.ai/docs/cli/ 。默认 TUI、--model、--continue；未声明统一的权限模式参数。 |
| antigravity / Antigravity CLI | `agy --version` | 未安装 / 未发现候选 | https://antigravity.google/docs/getting-started?tab=cli 与 https://antigravity.google/docs/cli/reference/ 。交互命令是 agy，支持 --model、--mode；不将 IDE 的 antigravity 命令当 TUI。 |
| codebuddy / CodeBuddy Code | `codebuddy --version` | 未安装 / 未发现候选 | https://www.codebuddy.ai/docs/cli/quickstart 与 https://www.codebuddy.ai/docs/cli/cli-reference 。交互 REPL、--model、--permission-mode、--continue、--add-dir。 |

- `AgentAdapterRegistry` 只读取统一数据根下 `Adapters` 的第一层 JSON；总计最多 32 个适配器、每文件 64 KiB、JSON 深度 8。拒绝损坏 / 超限 / 重复字段 / 重复 ID / 未知字段 / 未知变量 / 内嵌或递归模板 / 凭据开关 / 状态钩子，UTF-8 BOM 可读。JSON 文件出现、校验通过及路径覆盖均不构成执行同意。审核对话框显示版本目标 / 参数与完整启动声明，启用绑定文件原始字节的 SHA-256，文件变化后撤销；每次主动启动重新读取与核验，不依赖检测缓存授权。
- schema 与合法示例为 `Examples/agent-adapter.schema.json`、`Examples/agent-adapter.example.json`。schemaVersion=1；定义表达 id / displayName / executableCandidates / overridePath / versionProbe / launchTemplate / variables / fields / capabilities / statusSignals / evidence。变量仅允许完整参数位置的 model / workingDirectory，model 只用于 fields.model；附加参数须在字段白名单中。没有脚本插件钩子、外部代码求值或程序化交接协议，声明里的目标程序本身仍是用户明确授权运行的第三方代码。
- `AgentDetector` 的默认限制集中于 `AgentDetectionOptions` / `AgentLimits`：并发 2、单探测 3 秒、合并 stdout / stderr 最多 64 KiB；JSON 可收窄探测限制。版本进程在挂起状态加入 kill-on-close Job 后才恢复，继承句柄仅限探测管道；取消、超时、输出超限、正常结束都会关闭进程组。只保留有界版本号与固定原因 / 错误码，不保存原始输出、命令正文或环境秘密。
- 启动只做路径 / 元数据与缓存检查，不运行版本命令或 Agent 任务。版本探测只由显式刷新触发。缓存记录来源、路径、文件大小 / 修改时间、定义指纹与探测时间，版本有效期 7 天，WinGet 符号链接核对目标元数据；PATH 改变、文件修改 / 删除或覆盖路径改变会失效。高版本 / 无效偏好或缓存不被降级覆盖，缓存写入失败仍可使用本次结果并显示原因。
- 数据通过 `IAppDataPaths.AgentPreferencesFile` / `AgentDetectionCacheFile`，分别落在各模式独立根的 `Config/agent-adapters.json` / `agent-detection.json`；复用 `JsonDocumentStore` 原子写入与 revision 冲突保护。settings / profiles / recovery 保持 schema 1，未静默改写旧 Agent 配方；启动始终按当前有效适配器校验，不能退化成普通 Shell。未改变 ISession / ISessionManager 的输入、resize 与生命周期签名。
- `AgentLauncher.PrepareAsync` 返回实际 `SessionLaunchRequest`、Shell 后端 / 回退说明、目标路径、结构化参数与能力声明；`IAgentLauncher.LaunchAsync` 已有真实实现。优先 PATH 中的 pwsh.exe，没有才回退 Windows PowerShell；也可显式选 PS5。预览、确认与运行状态显示后端，失败不循环重试。CLI 权限模式只映射已声明参数，不绕过 CLI 授权或 Windows UAC；管理员 Profile 仍走阶段 7 的确认和 UAC，环境覆盖值不再进入其确认正文。
- 参数通过有界编码数据与固定启动脚本传递，不把用户值当 PowerShell 源码。原生 .exe 直接按 Windows argv 规则启动并继承同一 ConPTY；.ps1 通过参数数组调用；.cmd 包装器用成对引号保持 cmd 引号状态并保留原生 argv 中的字面引号。中文、空格、空参数、单双引号、反引号、分号、$、尾反斜杠与括号的所测范围已验证。`.cmd` 不支持 `% ! ^ & | < >` 或换行，遇到这些字符明确拒绝并提示使用 .exe / .ps1；不声称任意自行重新解析参数的批处理脚本均可保真。
- 配方仍复用独立 Profile 草稿 / 正式保存。模型为有界标识符，权限模式与附加开关须在适配器白名单内；最多 64 项、每项 2048 字符、展开总计 8192 字符，编码后另受 Windows 命令行上限约束。无令牌字段，已识别的凭据开关 / 赋值、Bearer / sk- / JWT 形式在表单及原始草稿写入前拒绝；这不声称可识别任意被用户伪装的秘密。认证由 CLI 自身或进程环境负责，不枚举继承环境到界面 / 诊断。
- Agent 会话的标题使用配方名称，仍使用同一生产 SessionManager、ConPTY、缓冲、输入与 DirectWrite View。当前能力仅为交互文本输入、部分已映射的原生 continue 参数和进程生命周期；programmaticHandoff=false，statusSignals 仅 process。运行中的 Shell / 包装器不等于 Agent 忙碌，执行 / 等待 / 任务失败 / 耗时无可靠事件时保持未知；不从任意输出、回车、静默时间或 Shell 提示符推断 Agent 内部状态，也不据此开启记录或通知。

### 阶段 8 验证与交接边界

- 环境：Windows 11 build 26300.9711、.NET SDK 10.0.400 / runtime 10.0.11，PowerShell 7 与 Windows PowerShell 5；实际 UI 另观察到 Store PowerShell 入口。最后 `dotnet build ArkTerminal.sln` 退出码 0、0 警告、0 错误，`git diff --check` 通过。没有新增 NuGet 依赖、测试工程、脚手架或仓库报告目录，没有 commit / push / 发布，没有改写正式用户配置。
- 临时证据集中于 `%TEMP%\ark-stage8-d952dbc7`，外部 Roslyn 探针直接加载最终生产程序集。最终 `checks-result.json` 的 216 项通过，`extra-result.json` 的 16 项通过，`ui-checks-result.json` 的 26 项通过，共 258 项；以最终 complete=true / 退出码 0 为准。早期夹具的 PATH 分号目录、重复测试数据及参考程序集选择错误不计验收成功。
- 参数检查实际运行了 PS7 / PS5 × .exe / .ps1 / 普通 .cmd / 带 IF 块 .cmd 共 8 组 ConPTY 会话，并逐项比较目标接收的 argv / cwd / 输入；非脚本注入替代。受控超时进程及其真实子进程在结束后不存在；并发 trace 实测最大值为 2。真实安装 CLI 仅运行已确认版本参数，未登录、安装、更新或发送模型任务。
- 正式便携 exe 的桌面检查覆盖新建页、七类状态下拉框、命令面板入口、JSON 未启用 / 审核 / 启用、实际命令预览与确认、受控 Agent 输出、创建 Profile 草稿及保存。首次启动与 JSON 启用后直接子进程均为 0；点击启动后观察到独立 pwsh / 目标进程和 ConHost，目标 READY_FOR_INPUT 进入正式 View。正常确认关闭后 UI PID 20048、pwsh 36020、目标 24040、ConHost 40508 全部消失，退出 marker 清理；后续仅保存 Profile 的实例也已正常退出。WPF 探针另覆盖默认记录关闭、草稿不改正式文件、秘密不进草稿、诊断排除、重复 / 重启与正常关闭。

| 编号 | 状态 | 实际证据与限制 |
| --- | --- | --- |
| 08-AC01 | 通过（本机检测与受控故障） | 七类均有状态 / 原因 / 来源；四个真实 CLI 版本已验证，agy / codebuddy 未安装，dsh 明确不支持默认 TUI。另以受控程序验证超时、不能运行、非零退出及输出超限。版本成功不等于实际 CLI 模型任务通过。 |
| 08-AC02 | 通过（本机） | 正式 exe 异步轻量检测、新建页与命令面板可用，首次直接子进程为 0；JSON 校验及启用均不启动任务。 |
| 08-AC03 | 通过（本机） | 元数据变化、缓存目标删除、路径覆盖 / PATH 变化、手动刷新、JSON 内容变化、缓存 / 偏好高版本保护均已验证；取消保留上次完整结果。 |
| 08-AC04 | 通过（本机） | 有 pwsh 时选 PS7；移除实际 PATH 中的 pwsh 后生成明确 PS5 回退，并用真实 ConPTY 收到目标输出；非循环回退。 |
| 08-AC05 | 通过（本机支持范围） | 上述 8 组真实 argv / cwd / 输入往返通过，包括双引号与反斜杠组合。cmd 解释字符的拒绝是明确能力限制，不算这些字符的完整支持；需要时选择 .exe / .ps1。 |
| 08-AC06 | 通过（本机） | 超限 / 损坏 / null / 重复键 / 重复 ID / 未知变量 / 钩子 / 禁用 / 变更撤销 / 陈旧审核拒绝均有生产服务检查，未运行禁用声明；UTF-8 BOM 与合法示例通过。 |
| 08-AC07 | 通过（所测合成秘密） | 无令牌字段；已识别凭据在表单与原始草稿写盘前拒绝；正式文件逐字节保全，探测缓存不含原始输出，默认诊断预览不含配方路径 / 参数 / 合成秘密。未使用真实凭据，不声称万能脱敏。 |
| 08-AC08 | 通过（本机生产会话与界面） | 受控 Agent 已运行并等待输入，生命周期显示运行中，Agent 执行 / 等待状态及耗时仍未知；未注入伪 Agent 事件或从普通输出猜测。 |

- 未验证 / 限制：四个已安装产品的登录后 TUI / 模型任务、真实 CLI 各版本差异、agy / codebuddy 实际安装环境、用户另装的 dsh TUI、非 CMD 管理员 Agent、Windows 10 / MSIX / 跨显示器 / 其他 IME / 物理键盘与真实 GPU 移除仍待对应环境验收。不得把受控 CLI 或版本探测当作这些产品任务的端到端验收。沿用阶段 7 的 MSIX 提权门禁，禁用不算管理员功能验收。
- 阶段 9 可复用 `AppHost.AgentAdapters` / `AgentDetection` / `Agents`、`AgentLaunchPlan`、`AgentDefinition.Capabilities` 与 `ISession.WriteInputAsync` / `ITerminalBufferReader`。当前没有任何经过实现 / 验证的程序化 Agent 协议，交接明确采用用户确认的复制或新会话降级；可靠 Shell 元数据和记录仍不能自动扩大上下文。不要自动实施阶段 9。

## 阶段 7：Shell 集成与管理员启动（2026-11-01）

- `ArkTerminal.Terminal/Integration/` 提供 `IShellIntegrationSource` / `ITerminalTextSource`、不可变 `ShellStatus` 与 `ShellIntegrationChanged`。开始、完成、提示符及 cwd 来自启动钩子，按会话随机标记关联；所有 OSC / Shell 元数据均不可信，只用于显示或用户授权的有界记录，不能授权执行、读取文件或扩大 MCP 上下文。`ISession` / `ISessionManager` 的输入、resize 和生命周期签名未改；Agent 细状态仍留阶段 8。
- PowerShell 5/7 用进程内 `PSConsoleHostReadLine` / `prompt` 包装与 `-EncodedCommand` 注入，保留已有函数，不重写永久 profile。成功布尔值可靠；仅单一直接原生命令提供原生退出码，cmdlet / 复杂语句的整数退出码明确未知，避免复用陈旧 `LASTEXITCODE`。没有可用 ReadLine 时主动收窄能力。Git Bash 4.4+ 用 PS0 / PROMPT_COMMAND；只在 Shell 自己的新历史条目可用时提供命令文本，忽略历史 / 禁用历史时不猜命令。适配器按原参数加载 login / rc 脚本，但内层不是 login shell，不保留 `login_shell` 标志；尊重 `--noprofile` / `--norc`，中间启动器不执行 BASH_ENV，进入交互 Shell 后还原该环境。CMD 只注入临时 PROMPT 目录信号，标题沿用原生 OSC，命令边界 / 退出码 / 等待 / 历史 / 耗时均未知。自带命令或未适配启动参数明确降级，不覆盖参数。
- 两份脚本作为 Terminal 程序集资源，启动内容通过参数 / 进程描述符注入，不落永久脚本，不改 bashrc、profile 或注册表。四种 Shell 的 `PostLaunchInput` 仍按原契约原样发送一次、不补 Enter。集成开关对下一次主动启动生效；禁用的新进程没有注入信号。
- 修复了 OSC 的 ESC-backslash 终止符此前提前清空载荷的问题，并正确跳过超限字符串；标题、集成与 OSC 8 链接按逐字节分块验证。链接只从有界可见快照识别；Ctrl+单击先显示完整实际目标，OSC 8 的目标不依赖显示标签。支持引号、空格、中文、相对 cwd 与软换行；拒绝控制 / 隐蔽格式字符、自定义协议、显式 UNC 网络 / 设备路径及 ADS，无可靠 cwd 不解析相对路径，不按错误码造跳转。网页仅允许 http/https；本地文件只在 Explorer 中定位或打开目录，不直接执行文件。外部打开在后台调用，失败显示实际错误。
- `ShellNotificationPolicy` 按命令 ID 去重，会话与应用均至少间隔 5 秒，结束的会话移除缓存。`ShellNotificationService` 使用无声音系统气泡和任务栏提示；只在应用进程没有前台窗口时发送，拥有的历史窗口 / 原生对话框也算应用内焦点。完成、长任务、提示符等待可分别关闭；默认只启用长任务，门槛 10 秒，`shellNotifications` 可设 1—86400 秒。没有可靠信号不通知，也不推测程序内部 Read-Host / 密码输入等等待。
- `ArkTerminal.Core/Recording/` 提供独立历史和转录服务，路径只用 `IAppDataPaths.HistoryDirectory` / `SessionSavesDirectory`。每次运行历史关闭，每个新会话转录关闭；旧 `Enabled` 偏好、草稿和布局恢复均不构成记录同意。开启入口确认正文 / 命令可能含秘密，不宣称万能脱敏。历史只接完整可靠边界、只保存本次同意之后开始的命令；默认 1000 条 / 1 MiB（含 JSON），30 天，原子替换，最多读取 16 MiB；关闭或清除使旧授权版本失效，不导入 / 删除 Shell 自身历史。有限本地查看 / 搜索入口最多显示 100 个匹配项。
- 转录接 VT 解析后的文本，不保存按键、OSC 或 CSI，默认每会话 10 MiB、总量 100 MiB、7 天；两类记录队列均为 64 项，转录单项最多 32768 字符。每段最多半个会话预算，轮换 / 截断标记可见，所有片段有范围说明；NTFS 枚举对打开文件可能报告旧大小，因此活动文件使用写入器实际字节计数计入配额。活动文件不被其他会话无声删除，配额不足 / 队列满 / 写盘或最终 flush 失败时停止记录，终端继续运行。停止有写入屏障，不再写之后的输出。启动和每小时清理精确匹配的自有过期 / 超量转录，不递归、不跟随重解析点；清除只处理本服务命名文件。保留动态覆盖 / 备用屏不能逐帧重放的限制。
- `AppHost.Shell.cs` 连接可靠事件与记录同意，收束并等待所有停止任务；`MainWindow.Shell.cs` 提供记录、历史查看 / 清除、链接预览、通知和每次管理员启动详情确认。Profile 的管理员开关已可启用；阶段 8 Agent 配方仍拒绝启动，不能退化为普通 Shell。按本次 Prompt 的通用约束，专注模式现在保留顶部 / 底部 / 左侧标签栏，只隐藏辅助入口，替代先前 UI 轮次的隐藏标签行为。
- `Sessions/Elevation/` 用同一 exe 的独立 `--elevated-host` 进程角色，经 `runas` / UAC 提权；辅助器不初始化 AppHost、配置、普通窗口或单实例锁，独立拥有提权 ConPTY 及 kill-on-close Job。UI 端保持低权限，通过受限本机命名管道通信；DACL 只允许当前用户，first-instance 和 `PIPE_REJECT_REMOTE_CLIENTS` 在内核拒绝复用 / 远程客户端。核验内核对端 PID、映像、创建时间、用户 SID 与真实提升令牌，用受限查询句柄，不按客户端自报 PID / 名称授权。仅支持同一用户的管理员提升，不支持替代管理员账号。
- 提权协议版本 1，启动绑定 SessionId、完整解析后的程序 / 参数 / cwd / 环境 / 运行模式及 SHA-256；请求只走 IPC，命令行不携带命令 / 环境正文。最大消息 256 KiB，I/O 每帧 16 KiB，握手 30 秒、心跳 3 秒 / 失活期限 15 秒；连接只允许一次启动，之后只有该会话的输入、resize、心跳与关闭。辅助器输出队列 64 帧，满时在后台读取线程背压，不丢 VT 字节或因暂时慢消费结束任务。取消、超时、管道断连、UI 内核句柄退出与辅助器退出都有收束；关闭高权限会话结束其进程树，不提供隐式脱离。
- 保持 settings / profiles / recovery schema 1。增加 `history.maxSizeBytes` 与 `shellNotifications` 的默认字段，既有字段保留兼容；未增加 NuGet 依赖、测试项目或仓库报告目录。现有回滚默认 5000 行与原上限保留，未因本阶段提高内存用量；这比 Prompt 中建议的 10000 行更保守，不宣称新增了独立回滚内存预算。

| Shell | cwd | 命令开始 / 结束 | 退出结果 | 标题 | 启动后文本 |
| --- | --- | --- | --- | --- | --- |
| Windows PowerShell 5 | FileSystem Provider | ReadLine / prompt | 成功布尔值；简单原生命令整数码 | 原生 OSC | 原样一次，不补 Enter |
| PowerShell 7 | FileSystem Provider | ReadLine / prompt | 同上 | 原生 OSC | 同上 |
| Git Bash 4.4+ | cygpath 转 Windows 路径 | PS0 / PROMPT_COMMAND | 顶层交互命令单元退出码 | 原生 / 用户提示符 | 同上 |
| CMD | PROMPT 的 $P | 不支持，未知 | 未知 | 原生 OSC | 同上 |

### 阶段 7 验证与交接边界

- 本机环境：Windows 11 build 26300.9711、.NET SDK 10.0.400 / runtime 10.0.11、Windows PowerShell 5、PowerShell 7.6.5、Git Bash、CMD。最后 `dotnet build ArkTerminal.sln` 通过，0 警告、0 错误。临时证据集中在 `%TEMP%\ark-stage7-6d16445a`，没有向仓库增加报告目录。早期夹具 / UAC 被取消的其他用例不算成功，采用各项最终 result。
- 外部 Roslyn 探针直接加载生产程序集，无测试项目：完整 Shell / Unicode OSC 分块 / 链接 / 通知策略 / 存储检查最终 461 项通过；`ui-notification-final/ui-result.json` 的 36 项生产 WPF / 真实 ConPTY 检查通过并退出码 0，覆盖默认新建页、旧偏好不记录、DirectWrite 发布帧、权限与可靠历史、转录启停、最小窗口保存栏、草稿文件保持、专注三位置标签、重复会话隔离及正常退出 marker 清理。实际隐藏窗口后运行 11 秒 PowerShell 任务，系统前台 PID 确认失焦、生产事件提交原生通知并被 Windows 接受，用户确认看到了且无声音。修正后的夹具显式收束资源和 Dispatcher；不把夹具的历史收尾错误算作应用验收结果。
- 既有临时检查重新运行：5422 项 Unicode / 搜索 / 复制断言通过，301 次真实 GPU 绘制 / 重置 / 取消 / 恢复通过，布局 / 画刷上限仍为 512 / 274；不等同真实 GPU 移除。WPF 自身 `settings-wpf.png` 检查了新入口布局；桌面截图工具曾返回不匹配画面，该结果已弃用，不计原生视觉验收。
- 用户操作真实 UAC：管理员 CMD 输出高完整性 SID `S-1-16-12288`，cwd / 环境 / resize 正确并回收；明确选择“否”后无注册会话；辅助器结束后普通会话仍可执行；UI PID 50124 异常退出前记录了辅助器 40852、CMD 37276、ConHost 8896，之后四个进程全部消失。握手未发送请求时辅助器没有 Shell 子进程，约 33 秒退出。普通身份核验 / 非法 PID、映像、创建时间与畸形 / 超限消息探针 11 项通过。
- ZIP 做了压缩 / 解压哈希核对；在中文 / 空格解压目录内用生产 Session / IPC 代码进行真实 UAC 探针。最终慢消费者压力探针 `ZIP 中文 空格/elevation-fixture/uac-stress-result.json` 的 11 项通过：输入 24000 行输出任务，按字节速率限流，超过 1 MiB 后仍运行，结束标记进入正式缓冲，正常关闭清除辅助器。先前逐包固定延时的压力夹具超时未算通过。这里是提权与路径探测，不是阶段 10 最终发布包验收。

| 编号 | 状态 | 证据与限制 |
| --- | --- | --- |
| 07-AC01 | 通过（本机所测适配能力） | 四种 Shell 开 / 关真实启动；成功 / 失败、原生整数码、中文 / 空格 cwd、标题验证，CMD 明确降级。禁用无注入；BASH_ENV / --noprofile 与启动文件逐字节保持检查通过。复杂 PS 退出码和被忽略 Bash 历史文本保持未知。 |
| 07-AC02 | 部分通过 | 分块 OSC 8、真实隐藏目标、引号 / 中文 / 空格 / 相对路径、含单引号文件名与括号 URL、非法协议 / 控制字符拒绝通过；生产 Ctrl+点击 / 预览 / 后台打开入口已接入。浏览器与 Explorer 的完整用户点击 / 失败反馈尚未人工端到端验收。 |
| 07-AC03 | 通过（本机策略 / 生产入口 / 用户观察） | 聚焦抑制、独立分类、短 / 长任务、提示符等待、去重、全局限频及无信号不通知检查通过；真实 11 秒任务在系统前台 PID 确认失焦时提交系统通知，Windows 接受，用户确认可见且无声音。其他 OS / 通知策略环境仍待验。 |
| 07-AC04 | 通过（本机生产控件 / 真实 Shell） | 启动与默认输出均无应用历史 / 转录正文；旧 Enabled 不启用，诊断预览无合成正文、命令和路径。Shell 自己的历史策略保持原样。 |
| 07-AC05 | 部分通过 | 授权时间段、停止屏障、单会话 / 总量 / 活动文件预算、轮换、过期、清除范围、队列满与实际 ACL 写盘失败停止通过；故障注入 ACL 已恢复。物理磁盘满未验证。 |
| 07-AC06 | 通过（本机） | 只保存同意后开始且收到可靠结束的命令，关闭 / 清除使旧授权失效，条数 / 字节边界与只读失败保全通过；不导入或删除 Shell 历史。 |
| 07-AC07 | 通过（本机生产 Session / 辅助器探针） | 用户同意真实 UAC，嵌入式会话核实高权限、cwd / 环境 / resize / 回收；ZIP 中文 / 空格路径通过。不能推广到未测试的 Windows / 包模型。 |
| 07-AC08 | 通过（本机所测场景） | UAC 取消、辅助器被结束、UI 直接退出、握手超时均收束；普通会话在辅助器故障后仍可执行，已观察测试进程树清理。 |

- MSIX：本机没有可用于本轮的 Windows 10 MSIX 打包 / 签名 / 安装工具链，未生成或安装测试包。`ElevationSupport` 对任何实际包身份直接禁用提权；普通模式保留。这是 MSIX 管理员功能的发布阻塞，不能将禁用计为实现 / 验收通过。阶段 10 必须核对 runFullTrust / allowElevation、清单和系统限制，并在真实安装包重验后才解除该门禁。
- 待补项继续保留：上表人工链接、其他系统的通知行为、物理磁盘满、Windows 10、跨显示器、其他 IME、实际物理键盘及真实 GPU 移除。管理员当前只实测 CMD；不把普通身份的四 Shell 检查说成四种管理员 Shell 的人工验收。当前不是最终发布验收通过。
- 阶段 8 可消费可靠 `ShellIntegrationChanged` / `ShellStatus`，但 Agent 细状态独立；阶段 9 继续用 `ISession.WriteInputAsync` 的显式输入入口，历史 / 转录与 Shell 元数据不得自动进入 MCP；阶段 10 复用本次 ZIP / MSIX 差异与同用户 UAC 边界。不要自动实施下一阶段。

## UI 整体轻动效（2026-11-01）

- `Visuals/MotionPolicy.cs` 统一主要 UI 的短动效：入场为 180ms 淡入与下向上 8 DIP 位移，退场为 110ms 淡出与轻微位移，标签拖放继续复用原策略。只使用 `Opacity` / `TranslateTransform`，不动画终端网格、文本布局、D3DImage 表面或窗口材质，不为输出、滚动和逐次键入播放动画。
- 显示 / 隐藏按元素版本取消旧操作，迟到的退场不会折叠已重新打开的面板；完成或取消后移除活动状态，避免自绘重命名窗口等临时元素被静态注册表长期持有。隐藏开始即停止鼠标命中，完成后再折叠；诊断文本在有效退场完成后清空，快速重开会取消旧清理。
- 新建页 / 终端标签、设置进入与返回、专注模式退出、设置分类、Profile 表单切换、命令面板、终端搜索、性能面板、诊断预览及标签重命名窗口已接入。性能面板开始退场即停止周期采样，不等动画结束；原会话附着、搜索恢复、焦点和诊断导出契约未改。
- `WindowVisualCoordinator.AnimationsEnabled` 继续同时受性能模式和系统 `ClientAreaAnimation` 控制。主窗口在视觉状态变化时同步 `MotionPolicy.SetEnabled`；关闭动画会立即取消进行中的入场 / 退场并落到最终可见性、透明度和变换状态，退出性能模式后恢复后续动效，不持久改写偏好。
- 最终 `dotnet build ArkTerminal.sln` 通过，0 警告、0 错误。临时隔离便携实例位于 `%TEMP%\ark-ui-motion-gx3CVIIA`；直接加载生产程序集的外部 WPF 探针通过 6 项状态断言，覆盖入场、快速关闭重开、退场完成、性能模式中途收束、禁用后显示及回调清理。正式 exe 实际操作覆盖设置分类与 Profile 切换、性能面板快速连切、诊断快速重开、命令面板 Esc / 重开 / Enter 启动 CMD、终端搜索、标签往返、性能模式、专注模式、最小化 / 恢复和确认退出。
- 真实 CMD 通过原生按键收到 `echo motionok` 并显示 `MOTIONOK`；标签往返前后应用直接子进程均为同一 `cmd.exe` PID 26300 与 `conhost.exe` PID 12116，正文保留，正常确认退出后验证进程与窗口均清理。未修改正式用户配置，未新增依赖、测试工程或仓库报告目录。
- 边界：本轮未切换 Windows 的全局客户端动画设置，系统分支与性能模式共用已验证的禁用路径，但不据此宣称系统设置实机验收；未重做完整 IME、标签拖放或跨 DPI 回归。Windows 10、跨显示器、其他 IME、真实 GPU 移除等既有缺口继续保留，不宣称最终发布验收通过，不自动开展阶段 7。

## 材质外轮廓、键盘空格与行号修复（2026-11-01）

- Windows 11 使用公开的 `DWMWA_WINDOW_CORNER_PREFERENCE=DWMWCP_ROUND`；WindowChrome 的非零玻璃框架清除应用 HRGN，由 DWM 统一处理内容和材质的外轮廓，窗口根框不再独立裁剪 12 DIP 圆角。保留自绘窗口按钮所需的非客户区策略、原生拖动与缩放。旧系统 / 合成不可用时继续使用原有 24 DIP 椭圆直径与 12 DIP WPF 半径；圆角 API 失败仅运行时降级，不改写材质偏好。受控合成失效与恢复分支已验证，不等于 Windows 10 实机验收。
- 原生按键复现确认：TextBox 将普通 `Space` / `Shift+Space` 作为编辑命令处理，不触发先前探针直接合成的文本事件。正式终端现在只在会话运行、终端输入焦点且非组合状态下，按现有输入方法提交一次空格并消费该按键；重复 KeyDown 每次提交一个字节。`ImeProcessed`、组合 / 候选状态及 Ctrl / Alt / Windows 修饰组合不走此分支，未新增 IME 提交路径。
- 行号 / 时间戳区域与正文复用相同的终端背景颜色和不透明度，各自区域只填充一次；取消独立灰条外观，字体、字号、字重和行高跟随当前终端及临时缩放。保存字重上限仍为 1000，行号的 WPF 显示上限适配为 999。逻辑编号、软换行、备用屏不可用标记及正文复制 / 搜索语义保持不变。
- 最终 `dotnet build ArkTerminal.sln` 通过，0 警告、0 错误。临时证据位于 `%TEMP%\ark-corners-space-223260ac`：`final-checks/result.json` 的 172 项专项与 `compat-final/result.json` 的 70 项界面 / 会话回归通过；覆盖亮暗主题、Mica / Acrylic / Off、0.5 / 1 不透明度下行号与 GPU 背景像素一致、背景图、字号 / Profile / 字重边界、软换行、回滚、备用屏、复制提取、窗口状态、性能模式及正常退出。未改写正式用户配置、未新增依赖 / 测试工程 / 仓库报告目录。
- CMD 与 PowerShell 的原生按键注入均由真实 ConPTY 观察程序收到 `[120,32,121,32,32,122,32]`，对应 `x y  z `。`ime-verified/result.json` 的 8 项真实微软拼音检查通过：组合中 `Ctrl+Shift+Space` 呼出面板、取消且不提交，返回后重新组合，候选下移与空格选词，以及随后输入普通空格；最终收到 `[23612,32]`（`尼` 与一个 ASCII 空格），没有导航键穿透或重复提交。该输入证据使用真实 TSF 和原生按键注入；ConPTY 端以阻塞 `getwch` 和原始控制台模式记录为准，不以合成文本事件或 `kbhit` 轮询结果代替。旧临时夹具的输入法准备 / 轮询 / 时序失败不代表最终结果。
- 既有 GPU 检查通过 301 次绘制 / 重建 / 取消 / 恢复及受控前缓冲检查，布局 / 画刷缓存上限仍为 512 / 274。正式 exe 的 WGC 截图已观察亮暗主题的 Mica / Acrylic 四角与三个自绘窗口按钮；标题栏实际移动及 Acrylic 最大化 / 还原后未观察到直角底色或黑屏。隔离启动停留新建页、选择 Profile 前直接子进程为 0，验证实例正常退出清除 marker。
- 边界：原生按键注入不等于实际物理长按 / 硬件时序验收；系统贴靠尝试受自动化坐标裁剪限制，未形成可靠贴靠证据，仍待手动补验。本轮未重做实际 100% / 150% / 200% DPI 切换。Windows 10、跨显示器、其他 IME、真实 GPU 移除与阶段 6 其余焦点 / 磁盘不足缺项继续保留，不宣称最终发布验收通过，不自动开展后续阶段。

## UI 裁剪与输入响应修复（2026-11-01）

- 复现了真实 CMD 已回送字符、屏幕却等待下一批输出才显示的问题：单独输入 `x`，等待 250 ms 光标仍停在旧列；再输入空格才显示前一个字。`IncrementalTextElementSegmenter` 现在保留字簇尾部状态，同时立即发布已经收到的内容，续码通过 `TerminalScreen.UpdateTextElement` 更新同一字簇；控制序列边界仍结束尾部状态。保留范围按尾部字簇长度限制，避免长读取批次丢失续码状态。支持组合标记、VS15/VS16、RI、ZWJ 及列宽变化时的续格 / 换行更新，不通过本地预测回显或改写输入法提交路径解决。
- 合并正式终端的输出通知，最多排队一次后台优先级的 UI 快照更新；终端绘制也使用后台优先级，让输入先处理。Shell 标题只在变化时更新，通过有锁轻量读取获取，不再为每批正文复制整屏快照。Session / manager 接口未改变。
- 已过期但正常完成的 GPU 帧立即补画最新内容；正常 D3DImage 锁争用使用 16 ms 延后重试，实际绘制 / 设备失败仍为 250 ms，继续只使用原有一个渲染重试定时器，隐藏 / 暂停时停止，不在 UI 线程自旋。`LastRenderWasBusy` 仅区分正常争用与失败，不改变共享纹理、GPU 查询取消及资源释放契约。
- 相同标签位置不再重建标签容器；字体样例在字体参数未变化时不重绘，也不创建 TSF 输入控件。DPI 更新由各正式绘图控件接收，字体样例仍能随 DPI 刷新。连续 60 次无关背景滑块调整，字体样例重绘由 120 次降为 0 次。
- 设置使用专用滚动模板，直接对 `ScrollContentPresenter` 做 12 DIP 圆角裁剪，滚动条与内容分开，去掉默认滚动条交汇处的白色直角。内容宽度跟随视口，正常宽度下文字换行；窄窗口保留横向滚动和固定保存栏。主窗口 / 重命名窗口的 WindowChrome 原生椭圆直径改为 24 DIP，与 WPF 的 12 DIP 外圆角匹配，避免原生裁剪范围外露方形底色。生产 MainWindow 的 WGC 截图已观察滚动后的圆角与窗口边缘；不等同于 Windows 10 或跨显示器验收。
- 最终 `dotnet build ArkTerminal.sln`：0 警告、0 错误。临时证据位于 `%TEMP%\ark-ui-fix-f14d2a29`：`complete-final/result.json` 的专项 4171 项通过（其中 4147 项字簇 / 分块输入检查）；`compat-final/result.json` 的既有 70 项 UI / 会话 / 透明度回归通过。原有 `checks.ps1` 的 5422 项 Unicode / 搜索 / 复制检查通过。适配当前 Draw 签名后的临时 `render-checks-current.ps1` 通过 301 次 GPU 绘制 / 重置 / 取消 / 恢复及受控前缓冲检查，布局 / 画刷缓存上限仍为 512 / 274；争用分类与失败后的恢复均通过。
- 本机探针中，6 次字符 / 空格输入对应的实际终端帧发布耗时约 10—36 ms；一万行输出期间输入调度 P95 约 17 ms、最大约 33 ms，120 次滚动更新正常。1000 个输出通知仅排入一次 UI 更新，空格按原顺序提交一次，正常退出清除 marker 并结束会话。上述为本机样本，不作所有设备的性能保证；真实微软拼音组合态、实际物理键盘时序、Windows 10、跨显示器及真实 GPU 移除仍待人工 / 对应环境补验。
- 未新增依赖、测试工程或仓库报告目录，未修改用户正式配置，未自动开展阶段 7。

## UI 统一与自绘标题栏（2026-11-01）

- 参考用户提供的 otty 截图和 `https://github.com/emilkowalski/skills` 的设计规范，统一深灰紫 / 浅紫灰主题、间距、图标入口、圆角组件、按下与键盘焦点反馈。新建页采用工作目录与 Profile 卡片，设置页采用左侧分类与分组卡片；保留原有配置、草稿及会话逻辑。
- 主窗口使用 `WindowStyle=None` 与 WPF `WindowChrome`，由 `MainWindow.Chrome.cs` 管理应用菜单、返回设置前页面、自绘最小化 / 最大化 / 还原 / 关闭按钮。顶部空白保留原生拖动命中，边缘保留原生缩放命中；顶部、底部、可调宽左侧标签仍可用。专注模式隐藏标签与辅助入口，窗口按钮保留。
- `Visuals/Themes/Shared.xaml` 统一按钮、文本框、下拉框、开关、列表、菜单、提示、滚动条及滑块；`Controls/RoundedBorder.cs` 对窗口、正式终端表面和字体样例执行真实圆角裁剪。终端内容位于圆角内 12 DIP 的留白，留白仅绘制边框带并复用终端背景颜色 / 透明度，避免裁掉小字号首字符或在正文下重复叠加 alpha；0.5 透明度的 GPU 正文与留白像素对比通过。正式 DirectWrite / D3DImage / TSF 路径未替换。标签重命名窗口也使用自绘标题区和圆角输入框。标签切换、命令面板与搜索等高频入口即时响应。
- 正式 exe 的 WGC 截图复现了启用 Mica 后 DWM 遮住自绘窗口按钮的问题；`WindowMaterialService` 仅对关闭 Aero 按钮命中的自绘窗口使用公开的 `DWMWA_NCRENDERING_POLICY=DWMNCRP_DISABLED`，保留 Backdrop 请求。修正后默认 Auto / Mica 的正式 exe 截图确认三个窗口按钮均显示；API 失败时运行时回退不透明，不改保存偏好。
- `dotnet build ArkTerminal.sln` 通过，0 警告、0 错误。`%TEMP%\ark-ui-450c6618\final-verification` 中直接加载最终生产程序集的外部探针通过 70 项检查：原生命中、圆角与紧凑文本模板、四类设置、预览取消及正式文件逐字节保持、真实 CMD 中英文输出、搜索 / 命令面板 / 重命名、三位置标签与会话保留、底部标签与性能面板不重叠、720×520 保存栏、浅色主题、专注、最小化 / 最大化 / 还原、Mica / Acrylic / Off 移动后的新发布帧及正常退出。`verified-exe` 内正式 exe 干净便携启动停留新建页，直接子进程为 0，正常退出移除 marker。未新增依赖、测试工程或仓库报告目录。
- 边界：本轮窗口状态和输入使用生产控件 / 原生 API 探针；正式 exe 的原生截图补验不等同于完整人工鼠标、Snap 布局或真实 IME 按键验收。Windows 10、跨显示器及既有阶段 3/5/6 平台缺项继续保留，不据此宣称最终发布验收通过，不自动开始阶段 7。

## 构建与验证

```bash
dotnet build ArkTerminal.sln
dotnet run --project ArkTerminal.csproj --no-build
```

- 最近一次 `dotnet build ArkTerminal.sln`：退出码 0，0 警告、0 错误。
- 项目没有测试基础设施。不要擅自新增测试项目、报告目录或脚手架；当前验收以真实应用、Shell、输入法和 Windows 桌面操作为主。
- 正常启动必须停留在新建页，只有用户选择 Profile 才启动进程。不要添加启动时自动运行 Shell、Agent 或恢复命令的行为。
- 本轮实测环境（2026-10-31）：Windows 11 build 26300.9711、.NET SDK 10.0.400、CMD / Windows PowerShell 5、Git vim/less、微软拼音；100%/150%/200% DPI，结束后恢复原来的 150%。只有一台活动显示器。

## 解决方案与职责

- `ArkTerminal.csproj`：WPF 应用、正式渲染、WPF/TSF 输入、选择、搜索和窗口入口。
- `ArkTerminal.Core/`：数据根、配置、Profile、恢复、校验和原子存储。保持平台无关，目标为 `net10.0`。
- `ArkTerminal.Terminal/`：ConPTY 会话、解码、VT、屏幕/回滚和渲染快照契约。
- `Rendering/`：唯一正式图形后端、字体服务、固定网格渲染和正式终端控件。
- `Input/`：WPF/TSF 文本输入桥。
- `Selection/`、`Search/`：选择复制与后台逻辑文本搜索。

## 阶段 3 已实现的架构

### 图形与字体

唯一正式路径：

```text
Direct2D/DirectWrite
  → D3D11 shared texture
  → D3D9Ex opens the shared handle
  → D3D9 surface
  → WPF D3DImage
```

- 不得把 D3D11 指针直接传给 `D3DImage.SetBackBuffer`，不得引入 WebView2/xterm.js。
- `Rendering/Direct3DSharedSurface.cs` 负责共享表面、DPI、D3D11 event query 同步、front-buffer 和设备失效恢复。GPU 完成等待在后台执行，不在 UI 线程自旋。
- D3DImage 使用零等待 `TryLock`；WPF 在超时返回 false 时仍增加锁计数，该分支也必须 `Unlock`。单帧等待期间取消和延迟释放资源，旧帧不能发布；绘制失败检查 `EndDraw`、D3D11 移除原因和 D3D9Ex `CheckDeviceState`，不使用对 D3D9Ex 无效的 `TestCooperativeLevel`。
- 失败帧重试间隔 250ms，最多一个渲染重试定时器；卸载、Detach 和暂停时停止。尺寸/设备清理不能释放仍被后台查询使用的资源。
- 前缓冲不可用时只取消帧，不清空 WPF 的表面绑定；否则恢复通知链会被切断而解锁白屏。必要的 COM 清理也保留 WPF 持有的绑定，等可用通知到达后再重建并全量绘制。
- `Rendering/TerminalFontService.cs` 负责字体测量和有界布局缓存；`DirectWriteTerminalRenderer.cs` 按字簇/连字组绘制，DirectWrite 自然 advance 不决定终端列。
- 缓存默认值：布局 512 项、truecolor 画刷 256 项、搜索结果 1000 条、搜索防抖 250ms。
- 临时 `FormattedText` 后端和 IMM32 实验文件已删除，不要恢复为第二套后端。

### Unicode 与缓冲契约

- `IncrementalTextElementSegmenter` 在普通读取末尾保留可能未完成的字簇，控制序列边界才强制冲刷。
- `UnicodeWidthData.cs` 包含 Unicode 16.0 官方 EastAsianWidth、emoji 属性和 variation sequence 的合并范围，附来源、SHA-256 和 Unicode License V3。EA Ambiguous=1，Wide/Fullwidth=2，emoji presentation、emoji ZWJ 和成对 RI=2，单个 RI=1；普通文本中的 ZWJ 不会一律触发 2 列。
- `TerminalWidthPolicy.PolicyVersion` 分别声明宽度数据版本与 `.NET StringInfo` 分簇实现；不声称运行时分簇已通过完整 Unicode 16.0 一致性验收。不同程序/字体的 emoji 宽度仍可能不同。
- 终端字簇与 DirectWrite 字形布局是不同层，不能用 UTF-16 `char` 数量定位光标或选择。
- 快照包含稳定 `LineId`、逐字簇文本、起始列、列宽和显式空白；宽字符续格不能成为独立选区起点。
- 新增 `ITerminalBufferReader`：按 scrollback offset 获取视口，并以后台调用的 `CaptureText()` 捕获有界逻辑文本。现有 `ISession` 输入、resize 和生命周期契约未改变。

### WPF/TSF 输入

- 正式输入桥是 `Input/WpfTextInputBridge.cs`，不是 IMM32。
- 透明 TextBox 仅用于获得系统 TSF 文本服务，排列在当前终端光标格；DirectWrite 负责组合串显示。
- composition/update 只更新 View，不能写入 ConPTY；commit 只写一次。
- 控制键在 `OnPreviewKeyDown` 隧道阶段先路由，组合/候选活动时导航键不得穿透到 Shell。
- `PreviewLostKeyboardFocus` 用于在 WPF 自动提交前取消组合。焦点离开策略是 **取消且不提交**，返回后新建组合。不要改回只处理 `LostKeyboardFocus`，否则拼音会被提交。
- 窗口失活不一定改变 WPF 的逻辑键盘焦点，输入桥还需在宿主 HWND 的失活/失焦消息中提前取消；不引入第二套 IME 提交路径。
- 位置、DPI 或视口改变后，输入桥刷新内部子布局，由 WPF TextBoxView 的布局更新通知 TSF；只移动固定大小的 TextBox 会使候选窗停在旧坐标。

### 选择、复制、搜索和回滚

- 选择使用 `LineId + 列边界`，复制在后台对逻辑文本快照执行。
- 软换行不插入换行；硬行使用 CRLF；保留显式/内部空格，去掉未占用尾部单元格，不复制 ANSI。
- `Ctrl+Shift+C` 只复制有效选择；`Ctrl+Shift+V` 粘贴原文。只有 Session 声明 2004 模式时才以一次输入写入 `ESC[200~ + 原文 + ESC[201~`。
- 搜索在后台执行 `OrdinalIgnoreCase`，防抖、可取消，并按 generation 丢弃迟到结果；不要在 UI 线程扫描回滚。
- `TerminalSearchService.Find` 的末尾增加可选 `CancellationToken`，扫描时检查取消；命中起点/终点分别向字簇起点/末端对齐，跨软换行的高亮片段也受 1000 条上限约束。现有 Session / buffer 接口不变。
- 历史回滚中开始输入/组合时回到底部；组合期间滚动历史会取消且不提交，然后正常回滚。
- `Ctrl+F` 搜索，`Ctrl + +/-` 和 Ctrl+滚轮缩放，`Ctrl+0` 恢复 Profile 字号。缩放只影响当前 View，不自动保存。

## 配置与依赖

- 新增 Vortice 包均固定 `3.8.3`：`Vortice.Direct2D1`、`Vortice.Direct3D11`、`Vortice.Direct3D9`，MIT。DirectWrite 已包含在 `Vortice.Direct2D1` 中。
- `packages.lock.json` 固定传递依赖；不要删除锁文件或无意升级版本。
- `app.manifest` 启用 Per-Monitor V2。
- 设置保持 `schemaVersion: 1`。新增字段有默认值，不提升版本、不做静默迁移：
  - `appearance.terminalFontWeight`
  - `appearance.terminalLineHeight`
  - `appearance.terminalCharacterSpacing`
  - `appearance.terminalLigatures`
  - `workbench.tabPosition` / `confirmCloseRunning`
  - `workbench.commandPaletteHotkeyEnabled` / `commandPaletteHotkey`
  - `appearance.visual`：主题、材质、真实后景、极光预设/自定义色、背景图相对路径及遮罩/模糊/亮度/饱和度/适配/位置、终端背景不透明度；合法范围集中在 `VisualSettingsLimits`。
- 恢复文档仍为 schema 1，新增活动 TabId、固定标题和明确 cwd；最近项目独立保存于 `Config/recent-projects.json`，最多 20 项且仅含路径/显示名。
- UI 字体与终端字体独立。阶段 6 提供基础字体编辑；Profile 显式选择继承全局字体/字号或覆盖当前 View，临时缩放不自动保存。

## 阶段 6 架构与验收（2026-11-01）

- 设置入口位于原窗口内容区，分外观、快捷键、配置与 Profile、诊断与数据；保存栏固定，内容可滚动。`MainWindow.Settings.cs` 编排设置编辑，`Controls/ProfileEditorControl.cs` 编排独立 Profile 目录编辑，仍复用现有存储、字体及视觉服务。
- 设置、Profile、恢复文档保持 schema 1。新增 `appearance.showTimestamps/showLogicalLineNumbers/showCommandDuration`（均默认关闭）、`workbench.focusModeHotkeyEnabled/focusModeHotkey`（默认 Ctrl+Shift+F11）和 `terminal.useGlobalFont`。旧 Profile 缺少继承字段时继续覆盖字体/字号；新建和新生成的默认 Profile 继承全局值。终端字重、行高、字距、连字为全局设置，UI 只编辑字体和字号。
- `AppHost.Profiles.cs` 提供独立 Profile 草稿与提交；路径为 `Recovery/profiles-draft.json`，通过 `IAppDataPaths.ProfilesDraftFile` 访问。两类草稿各持基准 revision，保存有界原始编辑文本；无效文本不替换最后有效预览，也不能正式提交。Profile、环境字典、参数及 Agent 配方完整深拷贝。
- `DocumentSaveResult.Warning` 区分“正式提交失败”与“提交成功、草稿清理失败”。提交意图标志与内容比较识别已提交的残留草稿，不恢复覆盖正式值。重载入口保留旧草稿的 revision 冲突保护；需取消冲突草稿后重新编辑，不自动合并或覆盖外部修改。
- Profile 支持新建、复制、删除及字段编辑。参数逐项保存，环境按首个等号拆分，名称去首尾空白，值保留空格/等号/空值，重复键按大小写不敏感拒绝并定位行。路径检查在后台复用正式启动解析；不可用路径允许保存，主动启动前阻止并提示；预检查不声称已验证执行权限。
- `{{home}}` 仅用于 Profile cwd 和 Agent cwd；参数、环境值及 `PostLaunchInput` 不做模板求值。启动后文本原样写一次，不自动补 Enter。新建使用正式配置；重启从最新正式 Profile 重新取配置，保留明确选定目录；重复标签继续使用旧启动快照。删除默认 Profile 前须先更换并保存默认项，删除后的重启提示重新选择。
- 正式渲染仍为原 DirectWrite/D2D→共享 D3D→D3DImage 路径。字体样例复用相同控件，只接有界合成快照，不创建进程；隐藏停止绘制并释放临时表面。显式临时缩放与配置基准分离，Ctrl+0 清除临时缩放；预览/取消不写入缩放偏好。
- 快照增加可选逻辑行号、首次输出 UTC 时间，软换行与重排保留；UI 时间显示为本机本地时间。辅助栏不参与终端列索引或复制，备用屏显示不可用；耗时始终为未知，等待阶段 7 可靠边界，不能按回车或输出间隔推算。
- 命令定义包含固定手势、作用域和可用条件。终端操作只在终端输入焦点且 IME 未占用时路由，输入框保留默认编辑行为；Ctrl+C 不被全局热键占用。命令面板和专注模式均可改键/禁用，注册冲突保留旧组合；磁盘提交失败回滚注册，窗口内回退跟随正式组合并统一去重。
- `ITerminalBufferActions` 独立提供清屏视图和清回滚，不改变 `ISession` / `ISessionManager`。清屏回到最新并清可见缓冲，保留光标/模式/回滚；清回滚只删主屏历史，保留可见内容；不写入 Shell 命令。失效或空选择不改变剪贴板。
- 性能面板默认隐藏，与“性能模式”分开。可见时每秒采样，隐藏/最小化/cloaked 停止周期采样，导出按需抓取。显示实际发布帧、重绘、读写字节、队列、缓冲及 ArkTerminal 进程指标（不含 Shell 子进程）；不调用 `CaptureText` 扫描正文。会话估算为已分配单元格槽位×64 B，排除可变文本/CLR/数组开销；共享纹理单列且只计一次。
- `ArkTerminal.Core/Diagnostics/DiagnosticService.cs` 只接固定组件、事件、错误码和匿名槽位，不接异常文本、命令、环境或正文。默认队列 256 条；日志每份 512 KiB、3 份、7 天；导出每份 1 MiB、5 份、7 天，集中于 `DiagnosticsOptions`。轮换/清理仅处理本服务时间戳+GUID 命名且非重解析点的文件，不递归扫描正文目录。写盘失败不阻塞终端，排队旧事件不会在清理后重建旧日志。
- 诊断预览仅在内存中生成，排除 Profile 名称/路径、完整命令、环境、正文和异常全文，数据路径使用固定别名。确认后只保存用户最终可见/可编辑的 UTF-8 文本，取消不创建导出，超限保留预览。日志自身写入与预览导出分开。
- 未增加 NuGet 依赖、测试工程或仓库报告目录。验收用 `%TEMP%\ark-stage6-aa3e89e2` 内的临时外部 C# 探针，直接加载生产程序集与真实 WPF/ConPTY；核心/存储/进程 48 项、WPF 控件/路由/诊断 47 项断言通过。截图已观察原生设置、终端辅助栏、性能和诊断预览；控件驱动不等于真实 IME 按键验收。旧的临时夹具失败记录不代表最终结果，最终结果以 `core-result.json` 和 `ui-result.json` 为准。
- 最后一次 `dotnet build ArkTerminal.sln`：退出码 0、0 警告、0 错误。

- 阶段 6 收尾修正了“把导入/清除背景作为首次编辑时仍要求先有草稿”的旧守卫；清除背景首次操作的专项生产控件检查通过，正式文件不变且草稿可取消。首次导入取消对话框不会创建草稿；文件对话框的实际选图流程继续复用阶段 5 原实现，本轮未人工点击该对话框。

| 编号 | 本轮状态 | 证据与边界 |
| --- | --- | --- |
| 06-AC01 | 通过（本机生产控件探针） | 原生字体发布帧、主题及真实 PNG 背景预览成功；取消后正式文件逐字节保持，原图保持，实际 UI 字号恢复，临时缩放保留。正式 exe 干净便携启动显示新建页；PID 11324 的直接子进程为 0，正常退出清除 marker，History/Sessions 均无文件。 |
| 06-AC02 | 通过（只读/冲突；磁盘不足未验证） | 设置与 Profile 文件只读时旧文件完整、草稿保留；恢复权限后重试成功。外部 revision 冲突未覆盖。Win32 热键替换后写盘失败恢复旧注册；清理失败用临时服务注入验证“已提交+警告”，不是物理磁盘不足验收。 |
| 06-AC03 | 通过（确认后的生产控件逻辑） | 恢复外观默认后分别取消和保存，Profile 文件及背景原图未删除；恢复逻辑只作用指定分类。确认对话框未人工点击，不能据此声称人工端到端通过。 |
| 06-AC04 | 通过（本机） | 名称/环境重复行定位、未知模板、无效目录、继承字体、逐参数空值/空格/引号/反斜杠、新建/复制/删除、独立 Agent 配方保存均覆盖；真实 CMD 与 Python 验证 cwd/环境/参数。无尾换行的启动后输入未执行，显式 Enter 后才执行。访问检查不替代实际 OS 执行权限验收。 |
| 06-AC05 | 通过（本机） | Profile 与设置保存后运行 PID/SessionId 不变；主动重启取得新正式环境和目录；重复会话隔离；删除 Profile 后重启提示且无新进程。 |
| 06-AC06 | 部分通过，完整按键验收待补 | 作用域路由、Ctrl+C 保留、两项原生热键禁用/冲突/回滚通过，真实 ConPTY Python 被 Ctrl+C 中断。本次补验真实微软拼音候选导航、空格选词 / 提交后空格，以及组合态 Ctrl+Shift+Space 取消且不穿透（原生按键注入）；物理键盘与不同实际输入焦点的完整回归尚未完成，不能把整项记为通过。 |
| 06-AC07 | 通过（本机） | 合成正文、环境、名称、异常和路径未进入默认预览/日志；预览取消无导出，确认后文件逐字等于最终编辑文本；大小、轮换、清理及保留同名前缀的非应用文件通过。未使用真实秘密。 |
| 06-AC08 | 通过（本机） | 多会话真实连续输出产生发布帧与正 RX 采样；隐藏、最小化停止周期采样，恢复可继续；进程整体、会话估算及共享资源口径分开。 |

### 阶段 6 交接边界

- 阶段 6 交付时的阶段 7 边界（历史记录，当前实现见上）：管理员开关不能开启；已有管理员值允许显式关闭，启动仍阻止。Shell 集成只保存结构，运行时明确未注入。历史/转录显示已保存偏好但启用入口禁用，未创建记录服务；未来不能因旧偏好或草稿恢复自动开始记录。耗时开关等待可靠命令边界。
- 阶段 8：Agent ID、模型、权限、参数、cwd 与 Shell 后端仅结构校验和保存；有配方时当前启动明确拒绝，不能退化为普通 Shell 报成功。`AgentLaunchRecipe` 的复制已隔离，未来 CLI 参数须按实际适配器验证。
- 阶段 10：复用 `AppHost.SaveFormalAsync/SaveProfilesAsync`、独立草稿服务、命令元数据、终端数字诊断、`DiagnosticService` 预览数据/导出/清理入口和面板按需采样。继续回归 D3DImage 同步、alpha、隐藏释放、辅助栏后的 IME 坐标、选择/搜索及字体/缩放恢复。
- 待补：阶段 6 物理键盘与不同实际输入焦点的完整按键回归、物理磁盘不足；Windows 10、跨显示器、其他 IME、真实 GPU 移除仍未覆盖。当前不是最终发布验收通过，不自动开展后续阶段。

## 阶段 4 架构与验收证据（2026-10-31）

- `Sessions/TabViewModel.cs` 与 `TerminalTabsViewModel.cs` 分离稳定 TabId、运行时 SessionId 和启动配置快照；重复创建新进程，重启默认清空旧缓冲，旧 Session 事件按实例/generation 丢弃。底层 `ISession` / `ISessionManager` 契约不变。
- 单个正式 DirectWrite View 只挂接活动 Session，后台会话继续接收输出但不持有渲染表面；切换保存滚动/搜索/缩放并取消 IME 组合。OSC 0/2 提供 Shell 标题，OSC 52 等其他序列仍忽略。
- 标签支持顶部/底部和左侧；左侧为顶部操作区加纵向列表，标签占满栏宽，分隔条可在 180—520 DIP 间拖动调宽。阶段 4 未实现拖出、多窗口、工作区或完整设置编辑器。
- 命令注册位于 `Commands/`，全局热键位于 `Infrastructure/GlobalHotkeyService.cs`；命令面板热键可配置，F11 使用独立 ID 注册并随退出释放。`WindowCommandKeyService` 只作窗口内回退，F11 多路径统一去重，窗口 `PreviewKeyDown` 不再重复处理 Space/F11。热键改键先释放旧注册，失败时回滚，禁用与退出释放。
- 历史刷新补齐 `CSI X`（Erase Character），解决 PSReadLine 长短历史切换尾部残留；渲染按内容版本禁止过期帧 `AddDirtyRect`，不通过频繁取消 GPU 帧造成白屏。
- 验证使用 `%TEMP%` 隔离便携目录和真实 WPF/ConPTY，没有新增测试项目或报告脚手架。最后一次 `dotnet build ArkTerminal.sln` 为退出码 0、0 警告、0 错误。

| 编号 | 实际状态 | 操作与观察证据 |
| --- | --- | --- |
| 04-AC01 | 通过 | 干净隔离配置启动只显示新建页，应用 PID 34228 的直接子进程为 0；点击 Profile 后才创建会话。 |
| 04-AC02 | 通过（本机） | 实际新建 20 个 CMD / Windows PowerShell 5 会话，加 2 个恢复占位共 23 标签；只有新会话有进程。后台 PowerShell 切走后完成 `BG_OUTPUT_1…8`，返回完整保留且活动项进入视口。正式 View 仅挂接活动 Session。 |
| 04-AC03 | 通过（本机） | 顶部、底部、左侧均实际操作；23 标签溢出可导航，活动项可见，新建/关闭入口可达。左侧栏从约 280 DIP 拖宽约 146 屏幕像素，标签和关闭按钮随宽度自适应。 |
| 04-AC04 | 通过（本机） | 实际拖动 CMD/PowerShell 标签快速换位及边缘落点，顺序正确、活动会话不重启；长拖动中按 Escape 取消，原顺序与标签数量保持。布局切换会取消当前拖拽。 |
| 04-AC05 | 通过（用户实测） | 用户确认微软拼音组合状态下 Ctrl+Shift+Space 与 Ctrl+Shift+F11 均正常，专注切换与输入行为正常。F11 使用独立全局热键，窗口层重复处理已移除并统一去重。 |
| 04-AC06 | 通过（本机） | 重启取消后原 `BG_OUTPUT` 全部保留；确认后目标标签为新缓冲，直接子进程数保持 40。正常关闭 20 会话后 ArkTerminal 和原子进程均为 0。 |
| 04-AC07 | 通过（本机） | 外部 Win32 探针验证 Space/F12 占用与释放；曾发现改键后新注册被旧注销误删并修复。改到 F12 后 Space 可注册、F12 返回 1409；从 Rider 全局呼出成功。禁用后 F12 可注册，重新启用 Space 后返回 1409，退出后二者均可注册。 |
| 04-AC08 | 通过（本机） | 带设置草稿和运行布局强制结束隔离实例，恢复后产生 2 个未启动占位和待确认草稿，应用 PID 16200 子进程为 0；只有点击明确启动才创建进程。 |

## 阶段 5 架构与验收证据（2026-10-31）

- `Visuals/` 统一窗口视觉协调、主题资源、DWM 能力、背景图、极光和动效；`WindowVisualCoordinator` 合成正式设置/草稿、系统状态与性能覆盖。主题切换只替换资源并重绘，不改变 Session。
- `appearance.visual` 保持 schema 1。遮罩 0—0.85、背景模糊 0—24 DIP、亮度 0.5—1.5、饱和度 0—2、终端背景不透明度 0.35—1。背景支持 PNG/JPEG/BMP，文件不超过 20 MiB、解码不超过 4000 万像素，静态处理不超过 400 万像素；副本使用 `Backgrounds/<文件名>` 相对路径。
- 背景图另有固定可读性遮罩（浅色 65%、深色 45%），与用户遮罩分离。亮度/饱和度/模糊在后台生成有界静态位图，不对文字或每帧终端内容施加 BlurEffect。
- 极光有 borealis/dawn/ocean/ember 和自定义色，聚焦约 30 FPS、失焦约 10 FPS，隐藏/最小化/cloaked 停止，减弱动效静态。性能模式是当前进程即时覆盖，不持久改写偏好；退出恢复最新正式设置或草稿。
- DWM 使用公开 Backdrop/框架扩展 API 并检查结果。Windows 11 build 26300 曾实测初次 Acrylic 可见、移动后黑屏；本轮修正 WPF 透明表面、框架扩展与 Backdrop 顺序，移除移动时重建和旧 blur-behind 路径后，正式 exe 的 Mica/Acrylic/真实后景移动验收通过，已解除该构建的强制不透明降级。历史记录不代表已证明系统构建自身有缺陷；其他系统版本仍按各自实测验收。
- `Rendering/TerminalVisualStyle.cs` 传入主题颜色与 premultiplied alpha；半透明默认背景整视口重绘以避免脏行 alpha 累积，不透明保留增量路径。共享 D3D 表面和 Session 契约未改。
- 验证使用 `%TEMP%` 隔离便携目录、真实 WPF/CMD 和临时外部生产程序集探针；没有新增测试项目或仓库报告脚手架。最后一次 `dotnet build ArkTerminal.sln` 为退出码 0、0 警告、0 错误。

| 编号 | 实际状态 | 操作与观察证据 |
| --- | --- | --- |
| 05-AC01 | 通过（本机） | 亮/暗草稿预览一致更新；跟随系统时真实修改并广播 `ImmersiveColorSet`，界面深浅随系统切换，结束后系统值恢复 0。应用/CMD/ConHost PID 5784/26632/4916 不变，`THEME_SESSION_RETAINED` 保留。 |
| 05-AC02 | 通过（本机；历史问题已修复） | 历史黑屏记录保留。本轮修正透明表面/API 顺序和生命周期后，Mica、Acrylic 与清空静态图后的真实后景均完成 20 次移动、5 次最大化/还原、5 次最小化/恢复，覆盖亮暗主题、性能模式及持续输出；最终像素无黑屏。未使用探针覆盖的正式 exe 复验通过，已解除 build 26300 降级。其他系统版本未据此推导。 |
| 05-AC03 | 通过（本机，用户配合文件对话框） | 用户通过真实系统对话框完成正常 PNG、坏图和 20 MiB 超限图操作：正常图显示背景和成功提示，两种错误均提示且原背景保留。保存后重启加载为 640×360 冻结位图；原图哈希不变，中文/空格便携副本独立可用，引用清理边界由生产服务断言验证。自动化焦点问题未归为应用导入缺陷。 |
| 05-AC04 | 通过（本机） | 实际调整遮罩、模糊至 0、亮度至 0.7、饱和度至 0、完整适应与左上位置，画面随参数变化且文字可读；取消后恢复深色、模糊 12、亮度 1.1、饱和度 0.8、裁剪填满和居中。 |
| 05-AC05 | 通过（本机） | 实际聚焦/失焦/最小化/恢复及 500 行连续输出正常。生产控件探针记录聚焦帧数 5，`HIDDEN_STOPPED=True`、减弱动效静态、性能模式停止；没有输出事件驱动极光。 |
| 05-AC06 | 通过（本机） | 自定义背景/极光下切换性能模式，模糊和动画立即关闭，退出恢复原偏好；会话标记、500 行输出、滚轮回滚及回到底部均保留，进程未重建。 |
| 05-AC07 | 通过（本机单显示器、微软拼音） | 实际执行 100%→150%→200%→150%，终端视口、字格与 D3DImage 像素尺寸随 DPI 重建；拖动、最大化/还原和持续输出正常。用户在三种缩放下确认候选跟随和画面正常，中文只提交一次；200% 取消样本未写入 Shell。跨显示器、Windows 10 和其他 IME 不在本项已验证范围内。 |

## 阶段 3 本轮修复与验收证据（2026-10-31）

- 修复宽度误判：U+231A、U+2630、U+16FE0、U+1B170 从 1 列改为 2 列，U+1F100 从 2 列改为 1 列；普通 `a + ZWJ` 为 1 列。
- 修复搜索 `e + 组合音标` 中的 `e` 返回零命中，以及扫描开始后取消无效的问题。
- 修复 GPU 查询与资源释放竞态、错误返回值遗漏、失败帧立即重试，以及组合中最大化后的候选旧坐标；补齐窗口失活和历史回滚时的取消处理。
- 后续人工锁屏复验复现了解锁白屏；已修复前缓冲不可用期间提前清空 WPF 表面绑定的问题，修复后在同一活动进程/会话内锁屏、解锁、持续输出和输入均实测通过。
- 压力检查还复现并修复：一行超过画刷缓存容量时，下一行沿用已淘汰的背景画刷会引发原生访问冲突。背景画刷改为每行即时获取，不跨行持有可能被 LRU 淘汰的引用。
- 验证使用临时外部脚本和生产程序集/ConPTY 管道；未新增测试项目、仓库脚手架或独立报告目录。临时证据位于本机 `%TEMP%\ark-stage3-2eukzu8v`，不是应用自动记录输出的功能。

| 编号 | 实际状态 | 本轮操作与观察证据 |
| --- | --- | --- |
| 03-AC01 | 通过（微软拼音） | 真实按键输入长组合，回删后补回并选候选；观察程序只收到一次 `你好世界中文输入验证`。Esc、应用内焦点离开以及切换到其他窗口均取消且不写入；返回后首个英文 `x` 正常收到。 |
| 03-AC02 | 通过（本机所测样本） | 5422 项临时断言覆盖官方范围边界、emoji 属性、VS15/VS16、RI、ZWJ、逐字节 UTF-8、光标/字簇、搜索和复制。真实界面显示混合字符表，并对跨软换行的 Unicode 样本选择复制逐字比较通过。 |
| 03-AC03 | 未验证（本机子项通过） | 100%/150%/200% 实际切换，组合中最大化/还原后候选跟随光标；历史回滚开始组合回到底部，组合期间回滚取消且不提交。跨显示器缺第二台活动显示器，不能将整项记为通过；原缩放 150% 已恢复。 |
| 03-AC04 | 通过（本机所测输入） | 原始观察程序收到左/右 `ESC[D` / `ESC[C`、F1 `ESC OP`、Ctrl+C `03`；生产 ConPTY 会话中的 Python 等待被 Ctrl+C 中断。实测 IME 开关、组合取消、窗口切换及返回后重新组合。 |
| 03-AC05 | 通过 | 真实界面 Ctrl+Shift+V，对观察程序关闭/启用/关闭 2004 模式：开始/结束标记计数为 `0/0 → 1/1 → 0/0`，中英、CRLF、空格、组合音标和 `👩‍💻` 原文完全一致。观察程序退出恢复控制台模式。跨软换行界面复制逐字一致，无额外换行/ANSI；硬换行 CRLF 与显式空格另经逻辑文本断言验证。 |
| 03-AC06 | 通过 | 生产 SessionManager / ConPTY 中实跑 Git less/vim，备用屏标志均 `false → true → false`，主屏标记保留，光标分别返回 `(8,0)` / `(11,0)`。真实界面也分别进入/退出 less 和 vim，主屏、光标正常；less 内建立选择后退出，高亮清除，Ctrl+Shift+C 不改变剪贴板，状态栏无错误。验收会话临时设置 `TERM=xterm-256color`，未修改全局环境；View 在屏幕切换时清除选择/搜索。 |
| 03-AC07 | 未验证（锁屏等子项通过） | 真实应用最小化/恢复正常；先复现解锁白屏，修复后用户配合锁屏/解锁，同一 PID 42204、同一会话继续输出至 `LOCK_PROBE_TICK 0084`，Ctrl+C 可中断，`echo LOCK_POST_UNLOCK_OK` 输出正常。锁屏前后句柄 660→661。修复后受控前缓冲丢失/恢复检查及 100 轮、301 次 GPU 绘制/取消/重建通过，布局 512 项、画刷 274 项；第 10/50/100 轮私有内存约 129/132/134 MB、句柄 637/642/610。其他特定 front-buffer 变化和真实 GPU 移除尚无对应触发条件，不能将整项记为通过。 |
| 03-AC08 | 通过 | 真实应用完整输出 5000 行，期间搜索、替换查询、取消和缩放保持响应，结束后搜索显示 `1/1000`；历史锚点及回到底部行为正常。另一次连续输出中最小化/恢复后关闭，应用和测试子进程均退出。进行中的大范围搜索在取消后 43ms 退出，迟到结果仍受版本/generation 校验；缓存上限另经真实 GPU 检查验证。 |

## 阶段 5 本轮修复与补验（2026-10-31）

- 材质能力将 `SystemBackdropApiAvailable` 与允许启用的能力分开；即使启用策略禁止材质，清理也实际调用公开 API。移除 Windows 8 以后不产生该模糊效果的旧 blur-behind 路径，不再依据其返回成功报告真实后景已应用。`WindowMaterialResult.Success` 仅表示 API 请求结果，不表示独立视觉验收。
- 材质应用先设置 WPF Window/HwndTarget 透明，再扩展框架并请求 Backdrop；移动不再先关闭再重建材质。DPI、窗口恢复和 `WM_DWMCOMPOSITIONCHANGED` 使用单个去抖刷新，退出移除 hook 和窗口事件。材质降级只影响运行时，不再在修改其他设置时把保存的材质/真实后景偏好写成关闭。
- GPU 实测复现了半透明背景重复叠加：设置 0.5 时行内 alpha 为 192、网格外为 128。整屏已 Clear 的行不再重复 Fill 背景，修复后连续三帧均为 128，预乘红色分量为 50；不透明增量重绘仍正确清除旧内容。
- 临时外部生产程序集探针 24 项断言通过，涵盖 GPU alpha、预乘 RGB、不透明增量路径、被禁用时清理原生 Backdrop、降级结果、偏好保留、26300 验收后恢复启用及中文/空格便携副本的独立解码、保留与清理。模拟旧系统能力只验证分支，不等同于 Windows 10 实机验收。未新增仓库测试项目或报告目录。
- 文件对话框的自动化仍复现缓存/焦点定位异常；用户已通过真实对话框完成正常 PNG、坏图和超限图操作，并确认正常图出现背景和成功提示，两种错误均提示且原背景保留。正常图已保存并重启加载为 640×360 冻结位图。原图、应用副本与中文/空格便携副本 SHA-256 均为 `00F8E9265F46BE5D239AFBD49BD0CF57F19AEEC831836093E90B502181C15740`；未因此修改导入处理代码。
- 本轮临时证据及对照程序位于 `%TEMP%\ark-stage5-fix-f6d4a90d`。历史黑屏证据保留；最终使用正式 exe 完成移动/恢复循环并捕获正常 Acrylic 像素后，才解除 26300 降级，未引入 Windows App SDK 或新依赖。
- 同一会话在 500 行输出及材质切换后保持 SessionId；搜索 `VISUAL_TICK 0500` 命中 1 项，回滚偏移 100、选择文本 `VISUAL_TICK `、回底偏移 0 均正确。`less` 备用屏 `true → false` 后主屏标记恢复且选择清除。正式便携构建启动停留新建页，选择 Profile 前直接子进程为 0。

## 待补验证与已知限制

- 跨显示器未验证：当前仅一台活动显示器。
- 锁屏之外的特定 front-buffer 变化和真实 GPU 设备移除未验证：没有专用触发环境；锁屏恢复和受控资源重建已通过，但不能替代其他场景及硬件设备移除验收。
- Windows 10 未验证：当前只有 Windows 11 build 26300.9711 桌面环境。
- 其他输入法未验证：当前只配置并实测微软拼音，不自动安装其他输入法，不宣称全部 IME 支持。
- 平台/字体兼容限制：Windows 字体可能把旗帜显示为 `CN`；Shell 程序或字体的 emoji 宽度可能与本应用策略不同。运行时 StringInfo 分簇不等于完整 Unicode 16.0 一致性认证。
- 阶段 4 的 04-AC01—04-AC08 均已通过本机/用户实测；本轮补验了 DPI、选择/搜索/回滚和备用屏，但特定 front-buffer 变化、真实 GPU 移除及跨显示器仍按各自环境验收，不能据此宣称跨平台发布验收通过。
- 阶段 5 的 05-AC02、05-AC03、05-AC07 已通过本机补验：build 26300 材质/真实后景重新启用，文件对话框由用户完成端到端操作，100%/150%/200% 与微软拼音候选定位已覆盖。Windows 10、跨显示器和其他 IME 仍是环境缺口；历史黑屏只证明当时的当前组合链路问题，不归因为已证实的系统构建缺陷。
- 后续修改透明或极光时必须回归 premultiplied alpha、D3DImage 同步、隐藏暂停和文本可读性。

## 必须保持的产品边界

- 每个 Session 独立拥有进程、I/O、缓冲和取消源；UI 线程不得做输出解析、进程扫描或阻塞 I/O。
- 标准、MSIX、便携模式的数据默认隔离，所有文件访问经统一数据根服务，配置原子写入；不静默迁移或降级覆盖高版本配置。
- 不自动运行检测到的 Agent，不修改用户 Agent 全局配置，不收集遥测或自动上传正文。
- 应用命令历史和按会话输出保存默认关闭；MCP 首版只读。
- 不自动更新、云同步、账号、插件商店或主题商店；不提前实现多窗口、工作区或完整设置编辑器。
