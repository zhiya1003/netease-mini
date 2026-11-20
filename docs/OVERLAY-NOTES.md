# OVERLAY-NOTES — MiniPlayer.exe（WPF / code-only / csc.exe 编译）实测手册

契约：docs/CONTRACT.md（v1 + 增补 v1.1/v1.2/v1.3）　细化规格：docs/SPEC-folia-remote.md
本轮（overlay run2）核心修正：**去掉一切 scale 换算**，WPF 一律用 DIP（=Folia 的 CSS px）。

## 1. 产物

| 路径 | 说明 |
|---|---|
| `overlay\\MiniPlayer.exe` | 105,984 B，.NET Framework 4.8 WPF，x64，winexe，code-only |
| `overlay\\build.ps1` | 一键编译（注意：脚本末尾 `exit` 会结束宿主 PowerShell，验证步骤要拆两次调用） |
| `overlay\\src\\WindowUi.cs` | 视觉树（sleeve174+face420 骨架、封面三态、队列、点击穿透、坐标导出） |
| `overlay\\src\\Channels.cs` | Core 路由 + 双 HTTP 后端 + CDP 客户端（awaitPromise、端口交接、build 探测） |
| `overlay\\src\\Model.cs` | State/Command + Store；QueueRow 现带 `index`(0基)/`id` |
| `overlay\\src\\Native.cs` | Win32 + Config(expandCover/coverMode/queueDir) + Position(DIP + 陈旧判定) |
| `runtime\\winrect.json` | 同时给物理像素与 DIP：`width/height`(物理) `widthDip/heightDip` `actualWidth/actualHeight`(DIP) `dpi/physPerDip/scale=1` |
| `runtime\\screen-*.png` | **CopyFromScreen 抓 GetWindowRect 真实矩形**的验收截图（唯一视觉证据口径） |
| `runtime\\cdp-run.mjs` | 复用助手：`node cdp-run.mjs b64 <base64(js)>` / `inject <file>` |

## 2. 编译

```powershell
$env:OVERLAY_DIR='D:\dsh工作区\netease-mini\overlay'
(Get-Content -Raw "$env:OVERLAY_DIR\build.ps1") | Invoke-Expression   # 末尾 exit 会终止当前 pwsh
```
csc 关键项（实测必需）：`/nologo /noconfig /target:winexe /platform:x64 /optimize+ /codepage:65001` + GAC 绝对路径引用；
`/noconfig` 防 CS1703，`/codepage:65001` 防中文源码按 GBK 读。本轮 `csc exit code = 0`。

## 3. 通道（v1.3 口径）

- HTTP 主通道：`127.0.0.1:46210`（占用则 +1..46219），`OPTIONS→204` + 三个 CORS 头，`POST /state→200`（响应体=下一条 Command 或 `{}`），`GET /state` 回显真实 State，`/health|/info`、`/hotspots`、`/cmd`、`/click`、`/ctl/<verb>`。
- **CDP 下发必须 `awaitPromise:true`**（Channels.cs:662）：bridge 的 `cmd()`/`connect()` 返回 Promise，不带就拿空对象。
- **端口交接（契约 v1.3 #2，Channels.cs:547-575）**：连上 9222 后 → 读 `info().port` → 若已等于自己端口则 skip；否则先 `window.__NCM_MINI_PORT_FILE__ = JSON.stringify(port.json)`，再 `window.__NCM_MINI__.connect()`（表达式直接返回 Promise 链，让 CDP resolve）。每 10s 复检一次，页面被别的 mock 抢走会自动夺回。实测：`handoff page 0 -> 46210`，随后 `handoff skip page port already 46210`。
- `/info` 新增 `bridgeBuild`（CDP 读 `window.__NCM_MINI__`.build，读不到填 null）、`portHandoff`、`coverState`。
- `--nohttp` 现在也写 `port.json`（`port:0, backend:"none", reason:"nohttp"`，Program.cs:127-129/170）；CLI 动词遇到无服务实例返回 **exit=2 + 明确文案**（Program.cs:233/269）。

## 4. 本轮验收实测（DPI/尺寸）

本机 2560x1440 @125%（DIP 2048x1152）。

| 条 | 实测 |
|---|---|
| E1 物理 | 外部 DPI-aware `GetWindowRect` = **753x230** @1642,126；进程内 `GetWindowRect` 同值（winrect.json `width:753 height:230`） |
| E1 DIP | `Window.ActualWidth=602.4`、`ActualHeight=184`（`Width/Height` 设 602/184，602×1.25=752.5→753，故 DIP 回读 602.4，在 ±0.5 内） |
| E2 截图 | `runtime\\screen-final-E1E2.png`（753x230，CopyFromScreen 真实矩形）：左侧真实封面照片、歌名 绽放、歌手 郑润泽、歌词大字、进度条+游标、`1:14 / 4:00`、底行 7 图标（prev/play/next/♥/loop/≡/，⋯ 完整在屏）、右上 tools 3 颗 |
| E2 队列态 | `screen-final-queue.png` 753x655（=184+340 DIP→物理），8 行队列 + 搜索框 + 当前行高亮 |
| E3 | `/click q5` → 日志 `cmd enqueue {"cmd":"playIndex","arg":5,"id":4}` + `POST /state reply={"cmd":"playIndex","arg":5,"id":4}`；页面 `lastAck={cmd:"playIndex",arg:5,ok:true,via:"dispatch",applied:{title:"绽放",index:5},settledMs:220}`；state().title 由 身骑白马(index 8) → 绽放(index 5) |
| E5 | `OPTIONS→204` + `Access-Control-Allow-Origin: *` + `...-Methods: POST, GET, OPTIONS` + `...-Headers: Content-Type`；`POST /state→200`；`GET /state` 回显 18 键真实 State（queue 31 行带 index/id） |
| E6 | `--quit` → `200 {"ok":true,"verb":"quit"}`；`processes=0 lock=False port.json=False` |
| E7 | `build.ps1` 一条命令重建：`csc exit code = 0`，`BUILT ...\\MiniPlayer.exe 105984 bytes` |

## 5. 与 SPEC 的 9 组差值（本轮逐条落地）

1. **骨架 sleeve174+face420**：WindowUi.cs:27-45 常量（WinW602/WinH184/CardW594/CardH176/SleeveW174/FaceW420/StageH124/SongCardTop35/SongCardLeft94/SongCardRight22/LineTop118/DeckTop127/DeckH49/ActionsTop46/ActionsH32）；face 内绝对定位用 Canvas（BuildStage/BuildProgress/BuildDeck）。队列左边缘 178（QueueLeft）。
2. **strip 恒 178**：WindowUi.cs:685-686 `stripPhys = round(178 × dpi/96)`，strip 内永远穿透（不再只在 idle 生效），轮询 80ms（对齐 main:5323）。
3. **队列高**：WindowUi.cs:830-834 `46 + clamp(n,1,8)*36 + 6`，最小 160；方向按 room.up/down + `least=min(wanted,160)` + 优先保持已展开侧（QueueDir 持久化）。实测 31 行 → 340 → 窗口 524 DIP/655 物理。
4. **字号/字重**：标题 20/26 600（WindowUi.cs:299）、歌手 13/19 rgba(ink,.58)（:304）、歌词大字 20/26 600 unsung .4（:309）、时间 11/500 .78 + 总时长段 .38（Run 两段）、队列行名 12.5/500 .86（当前 700 实色）、行歌手 11 .45、序号 10.5/500 .32 宽 26 右对齐（:924/:932）、搜索 12、tabular-nums（Typography.NumeralAlignment）。
5. **颜色**：ink dark = **255,255,255**（WindowUi.cs:111）、paper dark #111114 / light #fff（:112）、second .64 / next .3 / paused 覆写 sung .62 unsung .3、transparent 附 `inset 0 0 0 1px #ffffff1a` 且隐藏 ring、cover 背景补 wash（BlurEffect 76 = CSS blur 38）+ shade 渐变。
6. **图标尺寸**：play 36 圆（svg 21 playing / 22 paused）、skip 32（svg 17）、small 28（svg 15，more 16）、transport gap 14 居中 top 24、tools 24 圆 svg 13 gap 2 top 10 right 16、loop 指示点 3×3（one 档 #ff4d5e）。
7. **封面三态 transform**：WindowUi.cs:565-600 单一 176×176 元素 + `RenderTransformOrigin=(0,0)` + Translate/Scale：sleeve(0,0)/1 r`18,0,0,18` 内描边 .5；card translate(196,35) scale(.318182) r34.57 描边1.57；line translate(196,11) scale(.125) r48 描边4；描边色 #ffffff24；占位由音符改成 **F 76/88px 700 rgba(ink,.35)**。
8. **初始位置公式**：WindowUi.cs:1344-1355 `legacyX=wa.x+floor((wa.width-428)/2); x=max(wa.x,legacyX-174); y=wa.y+floor((wa.height-184)/2)`（全 DIP）；position.json 存 DIP 并带 `dpi/phys/unit:"dip"`，不匹配即判陈旧回落公式（Native.cs:212-231）。实测日志 `initial pos formula x=636 y=460`。
9. **动画**：KeySpline(.32,.72,0,1) settle / (.2,.8,.2,1) snap（:583）；封面进 sleeve .56s+60ms delay、其余 .3s snap；队列高 .22s settle + 换高期间整窗 opacity 0/90ms→1/140ms（:883）；idle 进度条下移 34px .44s delay .12s、非 idle .24s snap（:1119）。

## 6. 已知缺陷 / 待办（本轮实测发现，未修）

1. **Command.id 每进程从 1 重新计数**（Model.cs:155 `seq`）。bridge 按 id 去重，导致**新实例的第一条命令可能被页面当重复吞掉**（实测第一次 `/click q5` id=1 无效果，先补发一条其它命令让 id 前进后立刻生效）。修法：`seq` 初值取 `pid*1000` 或 `DateTime.UtcNow.Ticks/10000 % 1e6`。
2. **hotspots 的 q0..q7 曾同坐标**：ScrollViewer 内的行未过布局就 PointToScreen。已加 `surface.UpdateLayout()`（实测修复：y 396/441/486/…/711，间距 45 物理 = 36 DIP）。
3. **真实鼠标自动化在本机不可靠**：有并发操作者抢占鼠标（SetCursorPos 1029,369 → GetCursorPos 实测 1369,439），且窗口位置被手动拖动（1642,126 / 1700,90 等）。E3 证据改用 `/click q<idx>`（与行点击同一代码路径 WindowUi.cs:743/:952）。
4. **handoff 首帧返回 null**：`connect()` 的 Promise 在 CDP 侧 5s 内没 resolve 完时 Evaluate 返回 null（页面实际已切到 46210，10s 后复检即 skip）。可改为失败重试一次。
5. 音量仍无卡片滑条（Folia 也没有）；滚轮改音量未做进 UI 证据。
6. `--quit` 走 HTTP；`--nohttp` 实例只能 taskkill（契约 v1.2 #4 的口径，已给明确提示文案）。

## 7. 复验命令

```powershell
$RT='D:\dsh工作区\netease-mini\runtime'; $O='D:\dsh工作区\netease-mini\overlay'
$p = Start-Process -FilePath "$O\MiniPlayer.exe" -PassThru; Start-Sleep 13
Get-Content "$RT\winrect.json" -Raw                       # 期望 width:753 height:230 actualWidth:602.4 actualHeight:184
# 外部 DPI-aware 量矩形 + CopyFromScreen（Add-Type 里先 SetProcessDpiAwareness(2)）
(Invoke-WebRequest "http://127.0.0.1:46210/info").Content  # bridgeBuild / portHandoff / coverState
curl.exe -s -i -X OPTIONS http://127.0.0.1:46210/state     # 204 + 三个 CORS 头
(Invoke-WebRequest -Method POST -Body '{"which":"q5"}' -ContentType 'application/json' http://127.0.0.1:46210/click).Content
node "$RT\cdp-run.mjs" b64 <base64("JSON.stringify(window.__NCM_MINI__.lastAck())")>
& "$O\MiniPlayer.exe" --quit                              # 进程消失 + lock/port.json 删除
```
截图口径：**只认 CopyFromScreen(GetWindowRect 真实矩形)**，RenderTargetBitmap 不作验收证据。
