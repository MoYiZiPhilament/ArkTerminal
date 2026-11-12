# ArkTerminal 项目进度

更新日期：2026-11-12（Asia/Shanghai）。本表依据当前源码、`docs/IMPLEMENTATION-HISTORY.md` 的历史验收记录及本次仓库检查整理。

**当前结论：阶段 1–9 已有实现；阶段 10 已实施本机修复、测量和预览打包，最终发行验收未通过。** 当前主要版本为 `1.2.6a`。MSIX 安装 / 包身份下 MCP 与 UAC、干净 Windows 环境、物理输入和长期资源验收仍阻塞完整交付。

## 1.2.6a 输入与渲染修复

- 修复直接在 Shell 中启动 Agent 时的特殊键输入：win32-input-mode（`?9001`）激活时，此前仅裸 Ctrl+A-Z 编码为 win32-input 记录，Esc、Enter、Backspace、Tab、方向、Home/End/Insert/Delete/PageUp/PageDown、F1–F12 及 Ctrl/Alt/Shift 组合仍发纯 VT，被 conhost 误判而失效。现对所有特殊键与修饰组合完整编码为 `ESC[Vk;Sc;Uc;Kd;Cs;Rc_` 记录（含扩展键标志与修饰键按下/抬起记录）；单 Ctrl 输出与旧实现逐字节一致，命令面板 terminal.interrupt 不变。普通文本与非 win32 路径（Profile 启动、普通 Shell）零变化。
- 修复 `OnPreviewKeyDown` 未解包 `Key.System`：Alt+字母、Alt+方向与单独 F10 此前在所有模式下都不触发，现与命令路由对齐。非 win32 模式下这些键由「无反应」变为按既有 EncodeKey / ESC 前缀语义工作（激活既有意图代码，属行为变化）。
- 渲染热路径：完成的 GPU 帧不再因新输出取代内容而整帧丢弃（保留资源版本与前缓冲检查，下一帧增量补画待处理变更）；GPU 等待由 `Thread.Sleep(1)`（实际约 15.6ms）改为 `SpinWait`，D3DImage 持锁降到亚毫秒；光标动画不再每 vsync 强制全屏重绘，改走既有光标行带增量路径；输出路径渲染与快照应用提升到 Render 优先级。同步 / 取消 / 资源释放契约、零等待 TryLock 失败分支配对、前缓冲不可用保留绑定、半透明背景全量重绘均不变。
- 显示版本 `1.2.6a`，NuGet 构建版本 `1.2.6-a`，数字版本 / MSI `1.2.6`，MSIX 清单 `1.2.6.0`。
- 本轮验证：Debug / Release 锁定构建均 0 警告 / 0 错误。仓库外探针 56 项通过：win32 Ctrl+C/A 字节回归、特殊键全表（Esc/Enter/Back/Tab/四方向/Home/End/Ins/Del/PgUp/PgDn/F1–F12）VK/Sc/Uc/Cs、Ctrl+方向 Cs=264、Ctrl+Enter Uc=10、Ctrl+Backspace Uc=127、Alt+字母 Uc=0、Ctrl+Shift+字母 Cs=24、Shift+Tab、Ctrl+Space，非 win32 EncodeKey/EncodeControlCharacter golden 不变（Ctrl+C→ETX 等），以及 win32/DECCKM 模式位跨备用屏持久、仅 RIS 复位。隔离便携实例（`portable.flag`，数据根独立）启动并正确渲染新建页，exe 产品版本 `1.2.6a` / 文件版本 `1.2.6`。
- 已知限制：AltGr 字符输入在两种模式下都被 Ctrl+字母分支拦截（既有问题，本轮不扩 scope）。真实 Shell 直开 Agent 的按键验收、输入法组合手感、光标残影与帧率属运行时主观项，本轮以算法探针与结构改动验证，建议人工在真实会话复核；既有发行验收缺口不变。

## 1.2.4a Shell 修复（历史）

- 新建页滚动视口改用与设置页一致的圆角裁切遮罩（共享 `RoundedScrollViewer` 模板），内容填满视口宽度，滚动到中部时卡片在视口边界按圆角裁切而非直角；Agent 编辑器的路径覆盖与配方区块改为圆角子卡片；预览并启动 / JSON 启用的确认对话框改用与重命名对话框一致的圆角无边框样式。
- 块状光标移动改为帧率无关的指数平滑；动画帧提升到 Render 优先级；不透明背景下按已呈现光标行带做局部重绘；动画期渲染重试缩短到 16 ms，减少卡顿与残影。
- 快捷键可靠性：字体预览不再创建可聚焦输入桥；标签导航（含单标签）后恢复终端焦点；抑制隐藏输入框的复制 / 剪切 / 粘贴 / 全选编辑命令，避免组合期污染剪贴板；窗口重新激活时恢复卡死的输入法组合；命令路由解包 `Key.ImeProcessed`。纯 Ctrl+C/V/A 的终端控制字符语义不变。
- 显示版本 `1.2.4a`，NuGet 构建版本 `1.2.4-a`，数字版本 / MSI `1.2.4`，MSIX 清单 `1.2.4.0`。
- 本轮验证：Debug / Release 锁定构建均 0 警告 / 0 错误。隔离便携实例（`portable.flag`，数据根独立于正式 `%APPDATA%`）实测新建页滚动视口圆角裁切、圆角子卡片与圆角确认对话框均生效，设置页沿用同一模板渲染不变。仓库外探针 14 项通过：光标指数平滑的首帧吸附、小位移起动画、收敛停止、移动目标单调追踪，以及大跳变 / 跨行 / 隐藏 / 禁用 / 重置 / 回滚视图切换吸附，并确认 Ctrl+C→ETX、Ctrl+V→SYN、Ctrl+A→SOH 控制字符语义不变。
- 光标残影矩阵与真实 Shell 中快捷键可靠性、输入法组合手感属运行时主观项，本轮以算法探针与结构改动验证，建议人工在真实会话中复核；既有发行验收缺口不变。

## 1.2.2a 本地化与英文文档（历史）

- 新增统一 `ILocalizationService` 与中英文 `.resx` 资源目录，界面、动态控件及 Core / Terminal 用户可见错误均使用稳定资源键；英语为回退语言。
- schema 1 新增可选 `regional.language` 与 `regional.timeZoneId`。首次创建配置时选择系统 UI 语言（中文为 `zh-CN`，其他为 `en-US`）及 Windows 本地时区；旧配置缺字段时使用相同默认值，不自动改写原文档。
- 设置草稿支持语言和时区；保存后刷新现有窗口和动态控件。用户可见时间使用配置时区，记录、恢复与诊断继续使用 UTC。
- 根 `README.md` 改为英文并居中图标，中文全文移至 `README.zh-CN.md`，两种语言互相链接。
- 显示版本 `1.2.2a`，NuGet 构建版本 `1.2.2-a`，数字版本 / MSI `1.2.2`，MSIX 清单 `1.2.2.0`。
- 最终 Debug / Release 锁定构建均为 0 警告 / 0 错误。仓库外生产程序集探针 14 项通过：系统默认值、旧 schema 1 缺字段兼容、双语 857 条资源、动态翻译、时区 / 夏令时转换及无效值拒绝。
- 实际 Release 便携实例验证中文首次启动、英文保存即时刷新、英文命令面板 / Agent 控件 / 命令审阅 / MCP 窗口，以及重启后保留英文。正式 ZIP 首次配置为 `zh-CN` / `China Standard Time`，新建页子进程 0，CLR / WPF 原生模块来自包内，正常退出无异常标记。
- 既有脚本生成自包含 ZIP、开发签名 MSIX 与 MSI；包内中英文资源、MCP、版本和空便携数据目录检查通过，3 个附件的 SHA-256 清单核对通过，MSI 既有 ICE 校验通过。未安装 MSIX / MSI，既有发行验收缺口不变。
- 本轮临时界面与探针证据位于 Kimi 会话 `session_b2e84a6c-c19e-4ecf-868d-12c1dbc48e88` 及 `C:\Users\1\AppData\Local\Temp\ark-i18n-probe`、`ark-i18n-ui-fixed-S4EKaX`、`ark-1.2.2a-package-rfoBlu`；这些不是仓库测试工程或最终平台验收。

## 0.1.2a UI 与文档整理（历史）

版本约定：数字部分为 `x.x.x`，`a` 为主要版本、`b` 为测试版本（替代原 `preview`）、`c` 为特殊处理版本。历史发布名称保持原样；仅 MSIX 系统清单按 Windows 要求使用四段数字。

- 精简新建页、设置、Agent 配方及 Profile 编辑中的重复说明和长标签；保留权限、记录、披露范围与执行确认提示。终端、输入和会话行为不变。
- README 改为下载 / 使用、功能、隐私与构建入口；AGENTS 保留当前开发约束，原有 466 行交接原文归档至 `docs/IMPLEMENTATION-HISTORY.md`，新增 `CLAUDE.md`。
- 项目署名为 GPT-6.1 Sol 与 Kimi K3.1 Pro，`Prompt/` 为分段完成提示词，图标署名为 GPT Image 2.5 Sunburst。
- 显示版本 `0.1.2a`，构建版本 `0.1.2-a`，数字版本 `0.1.2` / MSI `0.1.2`；沿用 ZIP / MSI / 开发签名 MSIX 分发与既有安装、平台验收边界。
- 本轮按最终源码重新执行 Debug / Release 锁定构建，均为 0 警告 / 0 错误，自包含 publish 通过；诊断预览改为显示完整产品版本 `0.1.2a`。
- 17 项生产 WPF 检查通过，覆盖四个设置分类、最小窗口保存入口、默认不记录及诊断版本；保存了 9 张 WPF 渲染图。正式 ZIP 另有 8 项实际 exe 检查通过，包括中文 / 空格路径、无效 DOTNET_ROOT、随包 CLR / WPF、新建页无子进程与正常退出。
- ZIP / MSIX 的 19 项检查通过，MSIX 627 文件 / 3,920 个块摘要与开发签名密码学验证通过；未导入开发证书或安装 MSIX。MSI 的 ICE（仅排除预期 ICE91）及 25 项检查通过，行政解包后 621 个原有文件逐一匹配最终 ZIP；没有实际安装、修复、卸载或升级。
- 本轮临时证据位于 `C:\Users\1\.codex\visualizations\2026\11\04\01a1a4cf-83f9-79d2-a65b-7acd83f4e244`，以最终 `ui-smoke.json`、`portable-result.json`、`package-inspection.json` 和 `checks-result.json` 为准；历史结果与中途重打包产物不计入本轮最终结果。
- 发布入口：[v0.1.2a](https://github.com/MoYiZiPhilament/ArkTerminal/releases/tag/v0.1.2a)。

## 0.1.1a-preview 预发布（历史）

- 按用户要求采用最后选定的 Camellia 俯视 PNG，原图字节保留于 `Assets/AppIcon.png`，蓝色背景与透明圆角保留；ICO 提供 16–256 像素的 9 档尺寸，exe、WPF 窗口与 MSIX 图标统一使用该图。
- 显示版本 / Release 为 `0.1.1a-preview`，NuGet 兼容版本为 `0.1.1-a-preview`，程序集 / 文件 / MSIX 数字版本为 `0.1.1.0`。
- 原阶段 10 工作先保存为 `75ed23d`，图标与版本另行提交；沿用 `codex/stage10-validation` 开发分支，以 fast-forward 合入并同步 `main`，发行标签为 `v0.1.1a-preview`。
- 本轮 Debug / Release 锁定构建与自包含 publish 通过，0 警告 / 0 错误；WPF 生产窗口的图标资源 / 版本 / 新建页检查通过。实际 ZIP 在中文 / 空格目录及无效 `DOTNET_ROOT` 下正常启动、正常退出，CLR / WPF 原生模块来自包内，新建页没有子进程，退出清理 marker。
- 两包 19 项结构 / 许可 / 隔离 / MSIX 签名检查通过，MSIX 全部 627 个文件 / 3,920 个 SHA-256 块核验通过；开发签名只验证密码学与发布者匹配，未建立信任或安装。该轮临时证据位于 `C:\Users\1\.codex\visualizations\2026\11\04\01a1a470-aaff-7c53-8126-182588548178`。
- [GitHub 预发布](https://github.com/MoYiZiPhilament/ArkTerminal/releases/tag/v0.1.1a-preview)提供自包含便携 ZIP、开发签名 MSIX、公开开发证书、分发元数据与 SHA-256 清单。推荐 ZIP 试用，MSIX 仅供隔离开发验证。发布预览不改变下文已有的验收缺口。

### 同版本追加 MSI

同版本的 MSI 追加：`Packaging/Build-Msi.ps1` / `Installer.wxs` 直接封装已发布
ZIP 的应用二进制，新增 `ArkTerminal-0.1.1a-preview-win-x64.msi` 与独立
`SHA256SUMS-msi.txt`，不替换原有附件或改写标签。显示版本不变，MSI 数字产品版本为
`0.1.1`；按当前用户安装到 `%LOCALAPPDATA%\Programs\ArkTerminal`，配置使用标准
APPDATA 数据根。WiX CLI / UI 扩展固定 4.0.6，不新增应用 NuGet 依赖。
ICE 校验（排除纯当前用户布局的预期 ICE91 提示）和 25 项外部检查通过，行政解包后
621 个原有文件与发布 ZIP 的 SHA-256 一致；其余差异为说明、MSI 元数据和 UI 组件许可 / 源码。
解包 exe 使用 `--portable` 隔离测试时正常启动 / 退出、无新建页子进程、加载本地运行时；
没有运行日常系统的真实安装 / 修复 / 卸载，未改正式用户数据。完整安装与升级验收仍待补。
MSI 未签名，SHA-256 为 `4707a80e30a71d153d1330e4bbeb71a66ad3758f9b1f61b0b9d459902db6a170`。
证据在本轮临时目录的 `msi` 子目录。

## 阶段总览

| 阶段 | 当前实现 | 验证状态与主要缺口 |
| --- | --- | --- |
| 01 架构、配置与数据 | 独立数据根、原子存储、Profile、草稿与恢复基础 | 基础实现存在；本次构建通过，未重新执行该阶段完整验收 |
| 02 ConPTY 与 VT | 独立进程 / I/O、VT 解析、屏幕缓冲与基础输入 | 已有真实 Shell 的历史验证；本次未重新执行完整运行时验收 |
| 03 渲染、Unicode 与 IME | DirectWrite / Direct3D、字簇、TSF、复制、搜索与回滚 | 本机专项及微软拼音有历史验证；其他 IME、跨显示器与真实 GPU 移除待补 |
| 04 标签与会话 | 三位置标签、独立会话、重复 / 重启、热键、新建页与恢复占位 | 04-AC01–08 有本机 / 用户验收记录；跨平台与特定设备场景待补 |
| 05 视觉与动效 | 主题、材质及降级、背景图、极光、性能模式 | 本机材质、PNG 导入与 100% / 150% / 200% DPI 有记录；Windows 10 与跨显示器待补 |
| 06 设置与 Profile | 分类设置、独立草稿、字体、快捷键、性能与诊断 | 本机服务 / WPF / ConPTY 检查有记录；物理按键完整回归、部分原生对话框与磁盘不足待补 |
| 07 Shell 集成与提权 | 可靠命令边界、链接、失焦通知、有界记录、独立 UAC 辅助器 | 普通四 Shell、管理员 CMD、取消与回收有记录；人工链接全链路、物理磁盘满、其他管理员 Shell 与 MSIX 待补 |
| 08 Agent 检测与启动 | 七类声明、有限路径检测、按需版本、缓存、审核 JSON、配方与启动预览 | 历史 258 项本机检查；真实 CLI 登录后 TUI / 模型任务及未安装产品待验 |
| 09 上下文与只读 MCP | 有界采集、可编辑预览、复制、Markdown 快照、命令审阅与独立 stdio 桥 | 历史 196 项生产检查、14 项发布目录检查及用户另存核对；MSIX、真实客户端 / Agent 任务与跨平台待验 |
| 10 性能与发行验收 | 修复自然退出状态及线程池占用；回滚存储预算、行复用；RID / 自包含 ZIP / 开发签名 MSIX 与维护说明 | 本机 Debug / Release、生产探针、正式 ZIP 启停及旧 schema 1 替换兼容通过；MSIX 实装 / MCP / UAC、干净机及长期 / 物理输入验收未完成 |

逐项编号、操作证据和限制见 [历史交接](docs/IMPLEMENTATION-HISTORY.md)。该文件按工作轮次保留历史；早期“下一阶段未实现 / 未提交”的叙述应结合日期与最新总览阅读。

## 阶段 10 本轮结果

- 阶段 10 最初测量版本为 `0.1.0-preview.1` / 包版本 `0.1.0.0`，x64，自包含 .NET / WindowsDesktop `10.0.11`。具体生成和维护方式见 [Packaging/README.md](Packaging/README.md)。清单的 17763 是技术下限候选，尚无该版本实机验收。
- Windows 11 `26300.9711`、SDK `10.0.400`。本轮 Debug 与 Release 的锁定构建均为退出码 0、0 警告 / 0 错误；本轮结果与早期记录分开。
- 重跑 Unicode / VT / 搜索复制 5,422 项；配置 / 诊断 47 项；Shell / 链接 / 记录 / 通知策略 461 项；Agent 216 + 17 项；上下文 / MCP 196 项，解压目录再跑 196 项；故障与预算 15 项；四 Shell 8 组交互 / resize / 退出；20 标签与 100 次会话循环的 19 项检查通过。独立 GPU 探针完成 301 次绘制 / reset / cancel / 恢复，受控前缓冲恢复通过。它们不替代物理 IME、UAC、硬件移除或包身份验收。
- Release 的 20 个 CMD 会话创建到回显均值约 102 ms；60 次三位置切换到 Render 优先级均值 43 ms、P95 52 ms。两会话各 20,000 行输出时，极光开 / 关 / 性能模式约 1.89 / 2.54 / 2.82 MiB/s，样本约 3.30 / 2.49 / 2.27 秒。条件为 150% DPI、Cascadia Mono 14 DIP、1180×800 DIP 窗口，默认硬件渲染；未清空文件缓存，非物理按键延迟测量。
- 正式 ZIP 在中文 / 空格路径、无效 `DOTNET_ROOT` 下从自身目录加载 CLR / WPF 原生 DLL；新建页桌面检查、启动子进程 0、正常退出码 0 与 marker 清理通过。阶段 9 旧程序集生成的 schema 1 配置复制到新目录后，字体、左侧标签与中文 Profile 保留，配置字节不变且没有自动执行保存命令。
- MSIX 清单及 627 个文件 / 3,912 个块的摘要通过，开发签名密码学验证通过；系统签名信任验证准确返回不受信任。没有导入证书或安装到日常工作系统。MSIX 实装行为仍未验证，不能宣称可直接向所有用户分发。
- 首轮 100 次循环的私有内存保留促使追加 500 次循环（整轮外部探针约 121 秒，含其他前置测量），19 项检查全部通过。追加样本第 100–500 次的私有内存约 348–390 MiB，第 500 次 363 MiB；句柄 725–739，托管约 9.9–17.2 MiB，未持续增长。这证明该本机样本中保留可回收，不是跨设备或长时无泄漏保证。持续输出的 20 ms DispatcherTimer 最大间隔约 707 ms，只是调度代理指标，不能报成物理输入延迟通过。

本轮临时证据集中于 `C:\Users\1\.codex\visualizations\2026\11\04\01a1a419-9612-7e82-9abb-6dfe55d84ab6\stage10`，没有新增仓库测试工程或报告脚手架。完整逐项状态见历史交接的阶段 10 记录。

## 仓库归档时的核验（阶段 10 之前）

| 检查 | 本次结果 | 范围 |
| --- | --- | --- |
| 锁定依赖的 Release 构建 | 通过，0 警告 / 0 错误 | `dotnet build ArkTerminal.sln -c Release -p:RestoreLockedMode=true -v:minimal`，SDK 10.0.400 |
| MCP 伴随输出 | 已检查文件存在 | 主输出中包含 `Mcp/ArkTerminal.Mcp.exe`、运行时配置、SDK 与依赖 DLL；文件存在不替代协议验收 |
| Git 空白错误检查 | 通过 | 提交前运行 `git diff --cached --check` |
| 上传内容检查 | 已检查源码清单与常见凭据模式 | 源码与既有提交历史未命中所扫描模式；构建产物、IDE 缓存和本地运行数据不入库；不宣称识别任意秘密 |
| 阶段 9 历史记录 | 已复核记录文件 | `checks-result.json` 为 complete=true / 196 通过 / 0 失败，`publish-result.json` 为 14 / 0；不是本次重新运行这些用例 |

上述归档轮次没有新增测试工程、测试脚手架或仓库报告目录，也没有重跑桌面、UAC、IME、硬件或真实模型任务验收。阶段 10 的实际重跑见上一节，不能将不同轮次的数量相加为一次完整验收。

## 仍需处理的事项

1. **发行阻塞**：已有实际 MSIX 构建及开发签名，隔离安装、包数据 / 卸载 / 升级与入口仍未验收；MSIX 的 MCP 配置和提权保持不可用，禁用不算功能验收通过。
2. **平台兼容**：Windows 10、跨显示器 / DPI、其他输入法、真实 GPU 移除及特定前缓冲变化仍需对应环境验证。
3. **输入与界面**：物理键盘及不同输入焦点的完整回归、Snap 布局、链接从预览到浏览器 / Explorer 的人工全链路仍待补齐。
4. **故障与权限**：物理磁盘满、非 CMD 管理员 Shell / Agent、其他用户身份及最终包模型需补测。
5. **Agent 与 MCP**：真实产品登录后任务、手动粘贴、新辅助会话及真实 MCP 客户端接入需分别验收。版本探测、受控 CLI 和桥探针不能替代真实模型任务。
6. **资源与输入测量**：自然退出状态问题已在本轮修复，四 Shell 的 8 组检查均转为 Exited；长期私有内存、输出压力下物理输入延迟与持续压力仍需补验。

上下文交接当前采用确认后复制 / 新辅助 Agent 后复制；Agent 的忙碌、等待和任务完成状态在没有可靠事件时保持未知。阶段 10 需求与尚未通过的验收仍不能当作已完成能力。

## Git 管理

目标仓库：[MoYiZiPhilament/ArkTerminal](https://github.com/MoYiZiPhilament/ArkTerminal)。远端名为 `origin`，本地主分支统一为 `main`，与 GitHub 默认分支一致。

本地先前的阶段 4、阶段 5 提交予以保留；阶段 6–9 与后续 UI / 输入 / 渲染修复在工作树中相互关联，本次按真实现状归为一份源码快照，不重建未经逐阶段验证的历史。GitHub 原有的许可证提交通过合并保留。

| 提交 | 内容 |
| --- | --- |
| `b5d0cd5` | 已有阶段 4 多标签会话实现 |
| `fed345e` | 已有阶段 5 视觉与 DWM 后景修复 |
| `95ef3fe` | 归档阶段 6–9 与积累的终端 / UI 修复 |
| `afaa85d` | GitHub 原有 Apache-2.0 许可证初始化 |
| `72e14f4` | 合并许可证历史，保留两个来源的提交 |

进度文档和忽略规则作为单独的文档提交保存。同步采用普通 fast-forward 推送，不强制覆盖远端，不改写既有提交；上传源码不等于创建 Release 或完成发行验收。

后续改动使用短期功能 / 修复分支，按实际改动提交，合入前运行相应检查。提交应包含必要源码与锁文件，排除 `bin` / `obj`、IDE 缓存、便携数据、个人环境文件和签名私钥。日常同步可检查：

```powershell
git status --short --branch
git fetch origin
git log --oneline origin/main..HEAD
git diff --check
```

涉及会话、输入、渲染、提权或 MCP 行为时，仍需按实际变更补相应生产 / 环境验证；构建通过只证明构建成功。
