# ArkTerminal 实施交接

## 当前状态

- 产品目标：Windows 10/11 x64 原生 WPF Shell/Agent 工作台，目标框架 `net10.0-windows`。
- 阶段 1 的数据目录、配置、恢复和 Profile 已存在；阶段 2 的真实 ConPTY、VT 解析、屏幕模型和基础输入已存在。
- 阶段 3（DirectWrite、Unicode、IME、选择/搜索）已完成实现，状态是 **实现完成、相关平台验证待补**，不是最终发布验收通过。
- 阶段 4 已实现单窗口多标签、三位置标签栏、可调宽左侧栏、新建页、最近项目、有限项目识别、命令面板、全局热键、专注模式和未启动布局恢复；本机功能验收已通过，平台兼容与特定场景验证待补，不是最终发布验收通过。
- 阶段 5 已实现主题资源、视觉配置、背景图、活动极光、动效、性能模式和 DWM 能力降级，状态是 **实现完成、相关平台验证待补**，不是最终发布验收通过。Windows 11 build 26300 历史复现的移动后黑屏已在本轮修复并通过正式 exe 回归，材质/真实后景已重新启用；Windows 10 与跨显示器仍未验证。
- 不要自动开始阶段 6 或后续功能。阶段 3/4 的跨环境验证，以及阶段 5 的 Windows 10、跨显示器和其他 IME 验证仍需补齐。
- 不要 reset、清理或覆盖用户现有工作；不要自动 commit、push 或发布，除非用户明确要求。

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
- UI 字体与终端字体独立。Profile 的终端字体/字号覆盖当前 View；完整字体编辑界面属于阶段 6。

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
