# NCM-Mini 契约 v1（所有子代理必须严格遵守此文件）

## 目标
把 Folia 的 Remote Control Window（602x184 无边框透明小窗 / 594x176 卡片）1:1 复刻到网易云音乐 3.1.42.205594 上。

## 进程与通道
- 小窗进程 = `D:\dsh工作区\netease-mini\overlay\MiniPlayer.exe`（WPF, code-only, csc.exe 编译）
- 注入脚本 = `D:\dsh工作区\netease-mini\bridge\mini.js`，运行在网易云 CEF 页面 `orpheus://orpheus/pub/app.html` 内
- 两条通道，同一数据模型：
  1. **HTTP 通道**（主）：小窗在 `127.0.0.1:46210` 起 HTTP 服务（端口被占则 46211..46219）。
     页面 → `POST /state`，body = State JSON；响应 = Command JSON 或 `{}`。轮询间隔 250ms（播放中）/ 1500ms（暂停）。
     必须回 CORS 头：`Access-Control-Allow-Origin: *`、`Access-Control-Allow-Methods: POST, GET, OPTIONS`、`Access-Control-Allow-Headers: Content-Type`，并响应 OPTIONS 预检（204）。
  2. **CDP 通道**（兜底/开发）：小窗连 `http://127.0.0.1:9222/json` 找 `type=="page"` 的目标，用 WebSocket 发
     `Runtime.evaluate`，expression = `window.__NCM_MINI__ ? JSON.stringify(window.__NCM_MINI__.state()) : null`（读），
     expression = `window.__NCM_MINI__.cmd("<name>", <arg>)`（写）。

## 页面侧全局对象（mini.js 必须挂这个，名字不可改）
```js
window.__NCM_MINI__ = {
  version: "1",
  state(): State,            // 纯读，无副作用，必须 <20ms
  cmd(name, arg): {ok:boolean, err?:string},   // 执行控制
  dom: { toggle():void },    // 小窗按钮自身
}
```

## State v1（字段与类型固定，缺字段用 null，不许改语义）
```json
{ "v":1, "src":"ncm",
  "title":"String", "artist":"String", "album":"String",
  "cover":"http(s) 或 data: URL 或 null",
  "playing":true, "pos":18.3, "dur":238.0, "timeText":"0:18 / 3:58",
  "liked":false, "loop":"off|list|one|shuffle", "vol":60,
  "lyric":{"index":12,"line":"Na na na na na na","next":"...","lines":["..."]},
  "queue":[{"title":"...","artist":"...","current":true}],
  "bar":{"prev":true,"play":true,"next":true,"like":true,"loop":true,"queue":true,"vol":true,"seek":true},
  "ok":true, "err":null }
```
`bar.*` = 该操作在页面上是否找到了可点元素（能力探测结果，小窗据此置灰）。

## Command v1（小窗 → 页面）
```json
{ "cmd":"toggle|next|prev|seek|like|loop|vol|openQueue|toggleMain|showMain", "arg": <number|boolean|string|null>, "id": 1 }
```
- `seek`: arg = 秒（number，绝对位置）
- `vol`: arg = 0..100
- `loop`: arg = "off|list|one|shuffle"
- `like`: arg = true/false（目标状态，不是切换）

## 小窗几何（照抄 Folia，数值来自 app.asar:/electron/remoteControlWindowGeometry.cjs）
窗口 602x184（固定，不可 resize），卡片 594x176 居中，左侧 174px 为 idle 大封面 sleeve；
圆角 18px；卡片底 `linear-gradient(#18181c 0%, #0f0f12 100%)`；
描边 `inset 0 0 0 1px rgba(ink,.08)` + `inset 0 1px 0 #ffffff0d` + 外阴影 `0 1px 3px #00000073`；
浅色主题卡片底 `linear-gradient(#fbfaf8 0%, #f3f2ee 100%)`，描边 `#11111617`，阴影 `#0000002e`；
`transparent` 背景模式卡片底 `#0d0d11cc`；`cover` 背景模式 `#0d0d10`。
队列展开：卡片高度按队列高度向下或向上扩展，展开时队列宽 420px，队列行高 32px，搜索框高 40px。
窗口行为：无边框、透明、可置顶开关、跳过任务栏开关、拖动、左侧 strip 区域点击穿透（Folia 用 isPointOverRemoteControlStrip 判定）、位置持久化。

## 落地路径（只允许写这些）
- D:\dsh工作区\netease-mini\{bridge,overlay,install,docs,runtime}\
- D:\dsh工作区\folia-re\（Folia 逆向产物）
- D:\dsh工作区\ncm-re\（网易云逆向产物）
- D:\CloudMusic\package\（仅 install 阶段，必须先备份原文件到 netease-mini\install\backup\）
- HKCU:\Software\Classes\ncmmini（协议注册，用户级）
禁止：C:\Windows、Program Files、HKLM、任何系统文件。

## 环境事实（已核实，别重复验证）
- 无 dotnet/node/npm/git SDK；`C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe` 可用（实测能编译能跑）
- PowerShell 执行策略禁止直接跑 .ps1，必须 `powershell -Command` 里用 `(Get-Content -Raw x.ps1) | Invoke-Expression`
- 网易云 3.1.42.205594，CEF Chrome/91，React + styled-components（哈希类名），设计系统类前缀 `cmd-button/cmd-icon`
- **页面里没有 `<audio>` 元素**，音频走原生 → 进度/时长必须从 DOM 或内部 store 取
- 全局可见：`NE_DAWN, MNB, __mnb_globals__, APP_CONF, corona, WM, MusicAPM, Rx, React, ReactDOM, frontendEncrypt, cefDebugMode`
- 播放条已知类名：`play-btn, StyledRoundPlayButton_*, lyric-icon, cmd-icon-player-mode, cmd-space btns, center-btn-container, area lyric-mode, lyric`
- CDP 实测：`Start-Process D:\CloudMusic\cloudmusic.exe -ArgumentList '--remote-debugging-port=9222'` 后 `http://127.0.0.1:9222/json` 返回 1 个 page 目标
- 前端包：`D:\CloudMusic\package\orpheus.ntpk` = 100 字节头（36 位 hex id + 64 字节不透明数据）+ 明文 ZIP（1411 项），已解到 `D:\dsh工作区\ncm-re\orpheus_body.zip`；页面入口 `pub/app.html`(12306B)
- CDP 求值助手：`D:\dsh工作区\ncm-re\cdp-eval.ps1`（用变量 `$Expr` 传入 JS 表达式，返回 returnByValue 的 JSON）

---

# 契约增补 v1.1（协调者在 A 验收后裁定，覆盖上文冲突处）

1. **`vol` 定性**：Folia remote 快照与控件**不含音量**（已验证：remote.css 0 命中、main.cjs remote×volume 同行 0 命中）。因此 State.vol / Command.vol 是**超出 Folia 的新增项**（用户 Q11 明确要求音量），实现方式定为：由 bridge 拖动网易云自身音量滑条（DOM 合成指针事件），`bar.vol` 表示该滑条是否被找到；找不到就置灰按钮。禁止为此引入 Core Audio COM。
2. **队列写操作不在范围内**：Folia 的 `queueCapabilities{append,insertNext,remove,move,select,clear}` 与 `queueReadOnly` 本次不移植。队列只做「展开显示 + 点击某行切歌」（= `select`，走点击真实行元素）。契约 v1 的 `bar.*` 保持 control 级，不扩位。
3. **字体**：不分发 Inter。WPF 用 `Segoe UI`（`FontFamily="Segoe UI"`，标题 700 字重）；`letter-spacing -.012em/.02em` 与 `-webkit-line-clamp`、`background-clip:text` 无等价 → 按 SPEC §5.2 的近似方案：字距忽略、歌词大字用两行截断（`MaxLines=2` + 尾部省略号）、不做逐字渐变。
4. **数值权威来源**：WPF 侧一切尺寸/颜色以 `netease-mini\docs\SPEC-folia-remote.md` 为准；本文件与 SPEC 冲突时以 SPEC 为准，SPEC 与 remote.css 原文冲突时以 remote.css 原文为准。
5. **跨版本注意**：SPEC 里的 `js@偏移` 只对 Folia 0.7.24-adf8621 有效，维护时改按符号名/字符串检索。
# 契约增补 v1.2（C run1 复核后追加）

0. **DPI 铁律（最重要）**：Folia 是 Electron，602 CSS px = 602 **DIP**。WPF 同样以 DIP 为单位。因此 `Window.Width=602`、`Window.Height=184`，内容也一律用同一套 DIP 坐标，**禁止任何 0.8 / scale 换算**。本机 125% 缩放下 `GetWindowRect` 必须读到物理 **753×230（±3）**。自检必须用 `CopyFromScreen` 抓真实窗口矩形（不是 `RenderTargetBitmap` 离屏渲染——它会掩盖裁剪），并且抓出来的图里**必须能看到最右侧的 ⋯ 图标完整存在**。
1. **新增 Command `playIndex`**：`{cmd:"playIndex", arg:<0基整数>}`，语义=播放队列里第 arg 首（bridge 用 `playingList/jump2Track` 或直接合成点击队列第 arg 行）。State 的 `queue[i]` 必须带 `index` 字段（0基，与 arg 同基数）与 `id`（网易云 songId，可为 null）。小窗队列每一行点击都下发 playIndex。
2. **`__NCM_MINI__` 成员类型钉死**：`state()`、`cmd()`、`dom.toggle()`、`connect()`、`info()`、`selfStart()`、`rehost()`、`last()`、`stop()` 是函数；**`stats` 是对象**（`{cmd,err,post,kick,online,last,lastStateMs,toggle,lastErr,lastRej}`），不要当函数调。
3. **`connect()` 契约**：调用后立即开始端口扫描，返回 `{ok:boolean, port:number}`（同步即可），且首轮扫描不得晚于 1500ms。
4. **端口发现**：小窗把实际端口写 `netease-mini\runtime\port.json`（`{port,pid,backend,prefix,urlaclNeeded,at}`）；bridge 必须优先读这个文件确定端口，读不到再退回 46210..46219 扫描。
5. **身份可证**：bridge 的 `API.build` = `'ncmmini-bridge-<mini.js的sha256前8位>-<字节数>'`；小窗在 `/info` 里回 `bridgeBuild` 字段（从 state 里读不到就填 null）。
6. **HttpListener 无需 urlacl**（C 实测本机可绑 46210，甚至 80）；TcpListener 兜底仅在 HttpListenerException code=5 时启用。
7. **CDP 取值路径**：必须读 `result.result.value`（C 实测修前 CDP 通道静默无数据）。

# 契约增补 v1.3（B run2 复核后裁定，覆盖 v1.1/v1.2 冲突处）

1. **cmd() 返回 Promise（裁定：接受）**：overlay 侧所有 Runtime.evaluate 下发命令**必须带 awaitPromise:true**，否则拿到空对象；同步场景用 cmdSync(name,arg)（返回 {ok:false,err:'pending'}，真结果 300ms 后落到 stats.lastAck）。overlay 不得因 cmd 异步而自行重试造成双发。
2. **端口交接（裁定：接受，写进 overlay 启动序列）**：页面读不到本地文件（file:/// 与 orpheus://file 均 Failed to fetch，worker-src 未声明）。因此 overlay 每次启动/换端口必须两步：先 Runtime.evaluate 设 window.__NCM_MINI_PORT_FILE__ = JSON.stringify(port.json 内容)，再调 window.__NCM_MINI__.connect()（返回 Promise）。设置前先读 info().port，若已是自己的端口就跳过，避免与并发代理抢。
3. **seek 语义（裁定：按真实行为，不故意报错）**：ok:false/err:'not-applied' 只在确实没落地时返回。实测暂停态 seek 也能落地（33.12→45.12），所以「暂停一律失败」作废。
4. **契约字段补齐**：queue[i] 含 index(0基) 与 id(songId 或 null)；新增 Command playIndex(arg=0基序号) 与 closeQueue；CFG.ack 默认关（overlay 的 Json.cs 确认能吃未知字段之前不许开）。
5. **身份口径（裁定：接受 BODY 不动点方案）**：build = 'ncmmini-bridge-' + sha256(BODY) 前8位 + '-' + BODY字节数，BODY=两标记之间字节；整文件 sha256 由安装阶段写入旁证文件 bridge/mini.js.sha256，install/uninstall 与 API.selfHash('./hybrid/mini.js') 三方对齐即为可证。
6. **已知瑕疵（记入 v2.1，不阻塞交付）**：next/prev 的 applied.title 可能提前判命中（settledMs 8-17ms）；like(false) 走服务端取消需 1500ms；stats.dedupe 计数器与行为不对齐。
7. **install 阶段建议**：若走 CDP 兜底，CEF 启动参数追加 --disable-background-timer-throttling --disable-backgrounding-occluded-windows（后台定时器限流是 24.5s 首包的根因）。

6. **环境陷阱（所有后续代理必须遵守）**：PowerShell 5.1 的 `Get-Content` 对 `main.cjs` 这类文件有行数分裂怪癖（Count=7966 vs 真实 7990，按下标取行偏 17-23 行）。引用行号证据一律用 `Select-String` / rg，禁止用 `Get-Content` 数组下标。

# 契约增补 v1.4（输入路由与 W4 裁决）

1. W4 裁决：未 lit 时点击图标位置若落到进度条热区并产生 seek，判为符合 Folia，不算缺陷（Folia 空闲态 deck 不存在，该屏幕位置本就是 y≈163 的 bar 热区）。验收口径改为：未 lit 时不得出现任何 click key= 记录且 hitRaw=false；不要求零 cmd enqueue。idle 态 bar 热区保持 376x16 @ y=155.4，不得为这条去缩热区。
2. topmost 是功能前提，不是偏好：小窗非 topmost 时真实鼠标点击会被其它 topmost 窗口（StartMenuExperienceHost 全屏 topmost、游戏、网易云自身）在 OS 层截走，表现为「属性全对但点了没反应」。config.topmost 默认必须 true；测试脚本不得把 pin 点成 false 就收场（v23.ps1 每跑一次翻一次 topmost，收尾必须 /ctl/top 复位或点两下 pin）。
3. hidden 态的 dbg 数值不可用于输入结论：IsWindowVisible=false 时 GetWindowRect 仍返回旧矩形、dbg-layout 仍报 hit=True，但收不到任何鼠标消息。判定「点击是否生效」前必须先断言 windowVisible=true 且 rectSource=hwnd。
4. 日志格式：click key="prev"（带引号）。外部 grep 必须按带引号形式匹配，否则假阴性——本项目已因此误判过一次「八键全灭」。
5. 自动化点击期间：不得有并发人手鼠标（背景点击触发 win.MouseLeftButtonDown → DragMove() 把窗拖走；SetCursorPos 目标与实际 wmDown 落点不一致即为该现象）；不得发全局 ESC（小窗 PreviewKeyDown 会吃掉并 Hide，伪装成点了没反应）。
6. 坐标换算：dbg-layout 的 physPerDip 被 F(double) 的 "0.0" 打成 1.3（真值 1.2508）；换算一律用 rectPhysW/602，不要用那个字段。
7. 遗留缺口（必须补）：overlay 未实现 GET /mini.js 自分发（现 404），注入仍依赖外部临时 HTTP 服务，自动拉起链路因此不闭环。
8. 环境陷阱补充：PS5.1 读 .ps1 默认 GBK，UTF-8 无 BOM 脚本里的中文路径会静默失败；交付脚本要么全 ASCII + [char]0x5DE5 拼路径，要么用 (Get-Content -Raw -Encoding UTF8 x.ps1) | Invoke-Expression。
