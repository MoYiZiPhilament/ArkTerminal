# 阶段 9：上下文与只读 MCP

`ark-terminal-context` 是 ArkTerminal 内置工作流，不是七种 CLI 通用的原生 Skill 文件。应用菜单、命令面板以及固定全局热键 **Ctrl+Shift+F9** 可打开它；热键被其他程序占用时，使用菜单或面板，设置中的快捷键状态会说明注册结果。

## 上下文、建议和快照

选定来源会话后，只读当前主动选区；没有选区时取当前会话的有限尾部。可以关闭“优先选区”后重新采集。正文最多 200 行 / 64 KiB UTF-8，底层最多复制 200 个物理行 / 65536 个 UTF-16 单元，软换行可能使实际逻辑行数更少。最终正文按 .NET 字形簇截断，去除控制序列和隐蔽方向字符，尽力替换常见凭据模式。预览注明范围、截断与替换数；这不能识别所有秘密，用户仍须检查。整个可编辑 Markdown 预览上限 128 KiB UTF-8。

来源 Profile 只采集 ID、名称与 Shell / Agent 类型；cwd 标注来自启动快照或不可信 Shell 元数据。可信启动进程名来自实际创建的普通 ConPTY 进程，不能据此判断当前前台程序或 Agent 忙碌状态；未取得该字段时显示未知。不会读取环境值、启动脚本、参数、项目文件、`.env`、私有历史、其他会话正文或保存的转录。

操作前显示最终文本、来源、目标及长度，确认只对该次操作捕获的最终版本有效。来源关闭后预览失效。取消不会复制、发送或保存正文。Markdown 中的代码仅为文本。

- **确认并复制**：仅写剪贴板，状态为“已复制”，不代表 CLI 接收、提交或执行。
- **新建辅助 Agent 并复制**：使用所选已有配方，仍经过阶段 8 的实际启动预览和原有 UAC 流程；成功启动后复制上下文，由用户手动粘贴。现有 Agent 的输入模式 / 忙碌状态没有经过验证，首版不向其自动灌入说明文字。
- **命令解释 / 修复建议**：生成给所选 CLI 的请求正文。首版没有已验证的程序化建议协议，不会自动运行模型任务，也不会显示伪造的模型建议。没有保存配方时仍可复制到用户手动选择的 CLI。
- **另存 Markdown**：先预览，再使用系统另存对话框。默认建议位置由当前数据根的 `Snapshots` 提供；用户可明确另选位置。保存最终展示版本，使用同目录临时文件替换，不自动记录后续输出。
- **审阅建议 / 确认执行命令**：将 CLI 回复中要执行的命令手动贴入独立窗口，检查规则提醒、PowerShell Profile 和 cwd。最终确认后，在新的同内核 ConPTY 中用 `-NoProfile -NoExit -EncodedCommand` 提交显示的原文；管理员 Profile 仍需原有确认 / UAC。不会向忙碌的旧会话写入。显示“已提交”不保证命令执行成功。CMD / Bash 或未验证输入模式使用复制降级。

内置七类适配器当前均无经过实现 / 验证的程序化交接协议（`programmaticHandoff=false`）。这些入口不改变各 CLI 的认证、权限或安装状态，不修改任何全局 Agent 配置。

## stdio 配置

从完整构建或 ZIP 解压目录启动 `ArkTerminal.exe`，打开 **应用菜单 → 只读 MCP：授权 / 撤销 / 配置**，复制本实例的配置，再手动加入所选 MCP 客户端。每个客户端各启动一份 `Mcp/ArkTerminal.Mcp.exe`，不启动另一份 UI。

生成的 JSON 包含 `mcpServers.ark-terminal-readonly.command` 和结构化 `args`：`--pipe`、`--instance`、`--ui-pid`、`--ui-start`、`--ui-image`。它供支持 `mcpServers` 的客户端使用；其他客户端须按其自身格式手工映射 `command` / `args`，不宣称七种 CLI 共用配置格式。`command` 是桥的完整 `.exe` 路径，路径含空格 / 中文时仍是一个 JSON 字符串，不额外包裹 Shell 引号。没有令牌或环境秘密。配置绑定本次 UI 的实例、内核 PID、创建时间和映像；UI 退出或重启后须重新复制，旧授权也不会恢复。

MSIX 尚无实装验证过的入口，界面明确显示未验证并不提供伪配置。阶段 10 需验证真实包的启动方式、清单、读写权限和桥文件完整性；禁用复制不代表 MSIX 功能通过验收。当前发布输出为可本地验证的普通 / 便携目录，不是最终发布包认证。

## 协议和依赖

仅桥项目固定引用 **ModelContextProtocol.Core 2.3.1**（Apache-2.0），使用官方 SDK 的 JSON-RPC 类型、序列化、服务端、协议协商、取消及工具分派。自行实现的传输层只负责 UTF-8 换行帧、大小和背压限制。依赖锁文件为 `ArkTerminal.Mcp/packages.lock.json`；UI / Core / Terminal 不引用 MCP SDK。传递依赖为 Microsoft.Extensions.AI.Abstractions 10.11.0，Logging.Abstractions / DependencyInjection.Abstractions 10.0.13（MIT），无账号、云模型或网络监听功能。

`ProtocolVersion` 不固定为单个版本，保留 SDK 支持范围：initialize 协商支持 2024-11-05、2025-03-26、2025-06-18、2025-11-25；2026-07-28 使用带逐请求元数据的 `server/discover`。协议处理跟随 SDK，不能把新协议强塞进旧 initialize。stdout 只有每行一条 MCP JSON-RPC 消息，无 `Content-Length`；诊断仅 stderr，内容固定，不含正文或原始异常。

限制集中于 `ContextLimits` / `McpLimits`：入站 stdio 每条 128 KiB，出站序列化消息 1 MiB（64 KiB 正文可能被 JSON 双重转义，正文上限未提高）；桥传输队列 8 项、最多 4 个在途 JSON-RPC 请求、工具处理并发 2、单桥 IPC 一次一个请求、UI 最多 4 个桥连接。请求管道帧 4 KiB，响应 256 KiB，IPC JSON 深度 8；握手 5 秒，收到帧首字节后的剩余帧 / 读取请求 5 秒。正常无请求的连接可以保持空闲。取消会使该串行桥断开，避免迟到响应串到下一请求，其他桥和终端会话继续运行。UI 内核进程退出后桥取消并有最后 2 秒退出期限，处理 Windows 重定向 stdio 不响应取消的情况。

命名管道只允许当前用户 SID，内核拒绝远程客户端，首实例占用保护。双方通过内核管道对端 PID、真实映像、创建时间和 SID 鉴别；UI 仅接受同目录 `Mcp/ArkTerminal.Mcp.exe`。IPC 再检查版本 1 与随机实例 ID。客户端自报名称只显示为“不可信”，不授予权限。桥不直接访问配置、转录或快照文件，主 UI 经 SessionManager 提供数据。

核实依据：[官方 C# SDK v2.3.1](https://github.com/modelcontextprotocol/csharp-sdk/tree/v2.3.1)、[SDK 版本说明](https://csharp.sdk.modelcontextprotocol.io/v2/versioning.html)、[NuGet 包](https://www.nuget.org/packages/ModelContextProtocol.Core/2.3.1)、[stdio 规范](https://modelcontextprotocol.io/specification/2025-11-25/basic/transports)。具体安装包元数据和固定版本已在实施时检查。

## 五个只读工具

每个连接首次出现时在 UI 选择一个会话与允许字段，默认全拒绝；可立即拒绝 / 撤销。授权只活在当前连接中，不写盘。重启生成新 SessionId，不继承旧授权。选区的复选框单独披露该会话当前及之后主动选择的内容；cwd 也有单独字段。授权一个会话不会授权另一个会话。

| 工具 | 参数 | 返回白名单 / 状态 |
| --- | --- | --- |
| `list_profiles` | 无 | 唯一授权会话所关联 Profile 的 ID、显示名、Shell / Agent 类型，不列出全目录 |
| `list_sessions` | 无 | 唯一授权 SessionId、生命周期状态、Profile 引用 |
| `get_session_metadata` | `sessionId` UUID | ID、状态、Profile 引用、类型、可信启动进程名、未知的 Agent 任务状态；只有额外允许 cwd 时包含 cwd / 来源 |
| `get_output_tail` | `sessionId`；可选正整数 `maxLines` / `maxBytes` | 有界、去控制序列、尽力脱敏的实时尾部；客户端不能扩大 200 行 / 65536 字节限制 |
| `get_selection` | `sessionId` | 同管线的当前主动选区；需要单独允许 Selection，非活动 View 不保存额外选区副本 |

统一工具结果为 MCP 文本内容内的 JSON `{ "status": "...", "data": ... }`。正文结果带 `text`、`truncated`、`redactions`、`utf8Bytes`、`maxLines`、`maxBytes` 和 `untrusted=true`；没有允许的字段不会因其他工具而附带。

稳定状态包括 `ok`、`unauthorized`、`field_denied`、`session_denied`、`no_selection`、`revoked`、`session_closed_or_missing`、`invalid_arguments`、`unknown_tool`、`busy`、`disconnected`。协议层未知方法使用 SDK JSON-RPC 错误；过长帧关闭该桥，SDK 取消由该连接收束。`send_text` / `send_input` 不注册，未知 / 写入工具固定拒绝，不能借预览、模板或诊断触发输入。

诊断只记录操作种类、结果和计数，不记录正文、会话 ID、cwd、目标、备注、命令或导出路径。复制、快照保存、提交命令与 MCP 允许 / 拒绝是不同事件；“执行成功”仍需从具体终端结果验证。
