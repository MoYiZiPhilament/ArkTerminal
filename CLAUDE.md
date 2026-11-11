# Claude 开发入口

ArkTerminal 是 Windows 原生 WPF Shell / Agent CLI 工作台，采用 .NET 10、ConPTY 和 DirectWrite / Direct3D。

项目由 **GPT-6.1 Sol** 和 **Kimi K3.1 Pro** 完成。[Prompt/](Prompt/) 保存分段完成提示词；应用图标由 **GPT Image 2.5 Sunburst** 绘制。署名记录项目创作来源，不表示应用内置这些模型服务。

## 开始工作

先阅读 [AGENTS.md](AGENTS.md)，以其中的开发约束、代码入口与产品契约为准；当前功能见 [README.md](README.md)，状态与待验项见 [PROGRESS.md](PROGRESS.md)，原始证据见 [历史交接](docs/IMPLEMENTATION-HISTORY.md)。Prompt 是实施需求，不能当作全部完成的证据。

1. 检查 `git status --short --branch`，保护已有工作。
2. 明确假设、范围和检查标准，只改动本次任务需要的代码。
3. 构建并按行为变化验证，区分自动检查和真实环境验收；除非明确要求，不提交、推送或发布。

```powershell
dotnet build ArkTerminal.sln -c Debug -p:RestoreLockedMode=true
dotnet build ArkTerminal.sln -c Release -p:RestoreLockedMode=true
dotnet run --project ArkTerminal.csproj -c Release --no-build -- --portable
```

当前版本 `1.2.4a`（构建版本 `1.2.4-a`，数字版本 `1.2.4`）；依赖锁定，运行时固定 `10.0.11`。打包使用现有 [Packaging/](Packaging/) 脚本，不新增测试工程或仓库报告脚手架。

版本数字使用 `x.x.x`；`a` 为主要版本，`b` 为测试版本（替代原 `preview`），`c` 为特殊处理版本。显示、标签与包名保留后缀，底层工具链固定格式的元数据不作为显示版本。MSIX 系统清单允许按 Windows 要求使用四段数字。

## 核心边界

- 首次启动与恢复不自动启动 Shell / Agent；每个 Session 独立，UI 不执行阻塞 I/O。
- 保持唯一 DirectWrite / Direct3D 和 WPF / TSF 路径，组合更新不提交，失焦取消组合。
- 三种数据模式隔离，配置原子保存，schema 1 兼容；设置和 Profile 分开保存。
- 历史 / 转录默认关闭，诊断不收正文；不改 Agent 全局配置，不遥测或自动上传。
- 上下文有界并经用户检查；交接采用复制后手动粘贴，执行命令须另行确认并使用新会话。
- MCP 只读、单会话、逐字段授权，不增加写入工具；UAC 沿用受限辅助器与身份核验。
- `1.2.4a` 是主要版本。MSIX 的安装 / MCP / UAC、MSI 安装生命周期、Windows 10、跨显示器、物理输入、长期资源与真实模型任务等验收缺口仍保留。
- 不自动扩展后续功能；自动更新与云同步不做。UI 文案精简不得移除权限、范围和执行确认所需信息。
