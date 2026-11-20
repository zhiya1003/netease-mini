# Folia 0.7.24 Remote Control Window 逆向规格（供 WPF 复刻照抄）

目标产物：把 Electron 的 remote control 小窗 1:1 翻成可照抄实现的规格。所有数值都带证据坐标：
- `geometry:N` = app.asar:/electron/remoteControlWindowGeometry.cjs 第 N 行（副本 folia-re/extract/electron/ 同名文件，行号一致）
- `settings:N` = app.asar:/electron/remoteControlWindowSettings.cjs 第 N 行
- `main:N` = app.asar:/electron/main.cjs（副本 extract/electron/main.cjs，314,920 B）第 N 行
- `preload:N` = app.asar:/electron/preload.cjs（副本 30,192 B）第 N 行
- `css:选择器` = folia-re/remote.css 中的原文规则（抽自 app.asar:/dist/assets/bootstrap-DwFEGzvL.css，222 条 `.remote-*`）
- `js@偏移` = app.asar:/dist/assets/bootstrap-BJsYUc2U.js 的字符偏移（渲染层是压缩产物，偏移是唯一稳定坐标）

版本证据：`js@332543` 处字符串 `Folia/0.7.24-adf8621 (chthollyphile/folia-major)`。

---

## 0. 总览：一个窗口的三层几何

| 层 | 尺寸 | 位置 | 证据 |
|---|---|---|---|
| OS 窗口 | 602 × 184（只有展开播放队列时才变高） | 见 §1.2 | geometry:11-12, main:5501-5514 |
| 卡片 card | 594 × 176，居中（窗口四周留 4px） | `top:50%;left:50%;margin:-88px 0 0 -297px` | geometry:4(注释)+11-13, css:`.remote-card` |
| 在用卡片 face（右） | 420 × 176 | 相对 card `top:0;left:174px` | css:`.remote-face`, geometry:14 |
| sleeve（左侧 idle 大封面） | 174 宽，封面本体 176×176 | 相对 face `left:-174px` | geometry:14, css:`.remote-cover` |

推导：strip 宽度 = `floor((602-594)/2)+174 = 4+174 =` **178px**（geometry:28-30）。
队列左边缘 = `calc(50% - 123px)` = `301-123` = **178px**，与 strip 右边缘严格相等（css:`.remote-queue`）。即"点击穿透带" = 窗口左 178px，"在用区" = 右 424px。

---

## 1. 窗口 / 卡片几何

### 1.1 常量清单（照抄）

| 常量 | 值 | 证据 |
|---|---|---|
| REMOTE_CONTROL_WINDOW_WIDTH | 602 | geometry:11 |
| REMOTE_CONTROL_WINDOW_HEIGHT | 184 | geometry:12 |
| REMOTE_CONTROL_CARD_WIDTH | 594 | geometry:13 |
| REMOTE_CONTROL_SLEEVE_WIDTH | 174 | geometry:14 |
| LEGACY_REMOTE_CONTROL_WINDOW_WIDTH | 428 | geometry:16 |
| REMOTE_CONTROL_QUEUE_MIN_HEIGHT | 160 | geometry:107 |
| 卡片高 | 176 | css:`.remote-card{…height:176px…}` |
| face 宽 | 420 | css:`.remote-face{width:420px;height:176px;left:174px}` |
| 窗口内边距 | 上下各 4px | geometry:4 注释；css:`.remote-window[data-queue=down] .remote-card{margin-top:0;top:4px}`、`[data-queue=up] .remote-card{bottom:4px}` |
| REMOTE_CONTROL_POINTER_POLL_INTERVAL_MS | 80 | main:5323 |
| WINDOW_STATE_SAVE_DEBOUNCE_MS | 300 | main:2576 |
| REMOTE_CONTROL_WINDOW_TITLE | 'Folia Remote' | main:2617（页面侧副本 js@1770700 区 `JMe='Folia Remote'`） |

### 1.2 初始位置算法（geometry:19-26）

```js
legacyX = workArea.x + floor((workArea.width - 428) / 2)   // 老窄窗曾被 Electron 居中在主屏
x = max(workArea.x, legacyX - 174)                          // 向左让出 sleeve 宽度
y = workArea.y + floor((workArea.height - 184) / 2)
```
意图：把"在用卡片"摆在老窄窗的原位置。调用点 main:5502-5504（`getRestoredRemoteControlWindowPosition() ?? getInitialRemoteControlWindowPosition(getPrimaryDisplay().workArea)`）。

### 1.3 strip 命中区（geometry:34-38，main:5362）

```js
stripRight = bounds.x + floor((bounds.width - 594)/2) + 174
inStrip = point.x >= bounds.x && point.x < stripRight && point.y >= bounds.y && point.y < bounds.y + bounds.height
```
strip 内忽略鼠标（穿透给下层窗口），strip 外正常。Wayland 不参与（main:5362 的 `!onWayland`）。

### 1.4 X 轴 clamp：拖动时保持"画出来的部分"可见（geometry:58-80，main:5376-5391）

```js
inset  = expandCover ? 0 : 178
drawn  = { x: bounds.x + inset, y, width: bounds.width - inset, height }
leftEdge = { x: drawn.x, y: drawn.y + floor(drawn.height/2) }
if (某个 display.bounds 含 leftEdge) return bounds.x              // 合法
home = 与 drawn 面积重叠最大的 display（overlapArea, geometry:46-49）
return home ? max(drawn.x, home.x) - inset : bounds.x
```
Windows 在 `will-move` 每帧执行（main:5550-5552 → keepRemoteControlCoverOnScreen main:5376-5391）：x 被改则 `event.preventDefault()` + `setBounds({x, y:newBounds.y, width, height})`，宽高由 `getRemoteControlWindowBounds(newBounds, remoteControlQueueExpansion)` 给出（队列态尺寸不被平台重算，注释 main:5385-5387）。
关掉封面展开后的补偿：`bringRemoteControlCoverOnScreen`（main:5395-5406，仅 win32）按 `clampRemoteControlWindowX(bounds, displays, true)` 把窗口右移回屏内。

### 1.5 记忆位置 restore / 跨显示器回退（geometry:85-100，main:5477-5484）

```js
drawn = 同 §1.4（inset 由 expandCover 决定）
if (存在某个 workArea 完整包含 drawn 矩形) return { x: saved.x, y: saved.y }
return null      // 显示器拔掉 / 分辨率变化 → 回落 §1.2 居中默认
```
saved 非法（非对象 / x,y 非有限数 / workAreas 非数组）同样返回 null。

### 1.6 队列扩展高度算法（geometry:127-146，main:5433-5457）

渲染层想要的像素高（js@1750406-1750455）：
```js
S7 = 184   // 基础窗口高
C7 = 6     // 队列与卡片之间的间隙
mMe = n => 46 + clamp(n, 1, 8) * 36 + C7      // n = 队列条数
```
`46 = 6(间隙) + 40(搜索框高 css:`.remote-queue-search{height:40px}`)`；`36` = 虚拟列表 `rowHeight:36, overscanCount:4`（js@1753726 bMe 内），行内按钮高 32（css:`.remote-queue-song{height:32px}`），差 4px 为行距。条数封顶 8 行。
样本值：1 行 → 88（被 MIN 抬到 160）、3 行 → 160、5 行 → 232、8 行 → 340。
面板净高 = `expansion.height - C7`（js@1750861 gMe 返回 `panelHeight: u.height - C7`）。

主进程分配方向与截断：
```js
wanted = max(0, round(wantedHeight))
if (!workArea) return { direction:'down', height: wanted }      // Wayland 不知窗口位置：向下、不截断
room.down = workArea.y + workArea.height - (bounds.y + bounds.height)
room.up   = bounds.y - workArea.y
least     = min(wanted, 160)
side      = (preferred && room[preferred] >= least) ? preferred
            : room.down >= wanted ? 'down'
            : room.up   >= wanted ? 'up'
            : room.up > room.down ? 'up' : 'down'
return { direction: side, height: min(wanted, max(least, room[side])) }
```
证据 geometry:127-146。`preferred` 传参见 main:5441-5445：优先保持队列已展开的那一侧（`remoteControlQueueExpansion?.direction ?? direction`），只要那侧还容得下 `least`。

### 1.7 bounds ↔ 卡片位置 换算（记住的一直是卡片位置）

```js
// geometry:149-155
windowBounds(card, exp) = { x: card.x, y: exp && exp.direction==='up' ? card.y - exp.height : card.y,
                            width: 602, height: 184 + (exp ? exp.height : 0) }
// geometry:160-166
cardPosition(bounds, exp) = { x: bounds.x, y: exp && exp.direction==='up' ? bounds.y + exp.height : bounds.y }
```
改高要绕过 min==max 钳制（main:5414-5425 `resizeRemoteControlWindow`）：变高先 `setMaximumSize` 再 `setMinimumSize`，变矮顺序相反。
`preview` 语义（main:5448-5450）：渲染层先问"会往哪边长"，把卡片锚到那侧后再真长，避免卡片跳动。

### 1.8 Electron BrowserWindow 参数逐项（main:5505-5535）

```js
{ modal:false, x, y, width:602, height:184, minWidth:602, minHeight:184, maxWidth:602, maxHeight:184,
  frame:false, transparent:true, hasShadow:false, backgroundColor:'#00000000',
  title:'Folia Remote', name:'folia-remote', autoHideMenuBar:true,
  resizable:false, minimizable:true, maximizable:false,
  alwaysOnTop: remoteControlAlwaysOnTop, skipTaskbar: remoteControlSkipTaskbarEnabled,
  icon: APP_ICON_PATH,
  webPreferences: { preload: './preload.cjs', nodeIntegration:false, contextIsolation:true,
                    webSecurity:true, backgroundThrottling:false } }
```
入口 URL：`loadAppEntry(win, { remote: '1' })`（main:5577 → main:4873-4884 `win.loadFile('../dist/index.html', {query})`），实际 `…/index.html?remote=1`。
路由位：`Ld = !isObs && new URLSearchParams(location.search).get('remote') === '1'`（app.asar:/dist/assets/playerOverlayLayout-D39qTzM2.js，与 `Fd/Id/Rd` 同段），入口 `js@1810340` 的 `APe` 里 `OPe ? <ZMe/> : …` 渲染 RemoteWindow（组件 `ZMe` 定义于 js@1770916）。
生命周期：`page-title-updated` 阻止并复位标题（main:5546-5549）；`ready-to-show` 里重设标题 + alwaysOnTop + 下发 settings + 补发快照（main:5579-5586）；`closed` 清指针定时器与队列扩展态并刷托盘（main:5588-5598）。已存在窗口复用路径：main:5487-5497（setTitle → apply 三件套 → show → focus → 返回）。

---

## 2. 视觉规格

### 2.0 remote.css 本体

`D:\dsh工作区\folia-re\remote.css`（26,933 B）= 源文件 `/dist/assets/bootstrap-DwFEGzvL.css`（33,730 B，压成 1 行）里**全部** 222 条选择器含 `.remote-*` 的规则原文，逐条换行、声明体一字未改、顺序 = 源顺序 = 层叠顺序。
抽取器是字符串级规则扫描（识别引号/嵌套花括号），不是正则粗切；计数与校验命令见 §7 报告。

### 2.1 CSS 变量（`css:.remote-window` + 主题/背景覆盖）

| 变量 | dark（默认） | light | 证据 |
|---|---|---|---|
| --remote-ink | `255, 255, 255` | `17, 17, 22` | css:`.remote-window` / css:`.remote-window[data-theme=light]` |
| --remote-paper | `#111114` | `#fff` | 同上 |
| --remote-sung | `rgb(var(--remote-ink))` | 同 | css:`.remote-window` |
| --remote-unsung | `rgba(var(--remote-ink), .4)` | 同 | 同上 |
| --remote-second | `rgba(var(--remote-ink), .64)` | 同 | 同上 |
| --remote-next | `rgba(var(--remote-ink), .3)` | 同 | 同上 |
| --remote-settle | `cubic-bezier(.32, .72, 0, 1)` | 同 | 同上；JS 同值 js@1753554 `_Me=[.32,.72,0,1]` |
| --remote-snap | `cubic-bezier(.2, .8, .2, 1)` | 同 | 同上 |
| --remote-progress | 初值 `0%`，JS 每帧写 `--remote-progress:<pct>%` | — | css:`.remote-line` + js@1747292 lMe |
| --remote-fill | 每个歌词字 0..100（`Math.round(x*1000)/10`） | — | css:`.remote-lyric__word` + js@1744879 oMe |
| --transition-glow-color | `rgba(ink,.8)` | `rgba(ink,.45)` | css:`.remote-window` / `[data-theme=light]` |
| paused 覆写 | `--remote-sung:rgba(ink,.62); --remote-unsung:rgba(ink,.3)` | — | css:`.remote-window[data-paused=true]` |
| cover/transparent 背景 | 强制深墨：`--remote-ink:255,255,255; --remote-paper:#111114` | — | css:`.remote-window[data-bg=cover],.remote-window[data-bg=transparent]` |
| transparent+idle 覆写 | `--remote-unsung:#ffffff9e; --remote-second:#ffffffe6; --remote-next:#ffffff9e`；再 paused → `--remote-sung:#ffffffc7; --remote-unsung:#ffffff80` | — | css:`.remote-window[data-bg=transparent][data-idle=true]`（+ `[data-paused=true]`） |

### 2.2 卡片本体（圆角 / 渐变 / inset 描边 / 外阴影）

| 属性 | 值 | 证据 |
|---|---|---|
| 圆角 | 卡片/队列/ring/glass 18px；sleeve 封面 `18px 0 0 18px`；小封面 line 态 48px、card 态 34.57px；按钮 50%；seg/field/switch 8px；song 9px；tag 4px；预览封面 6px；cloud 26px | css:`.remote-queue`、`.remote-ring`、`.remote-glass`、`.remote-window[data-sleeve=true] .remote-cover`、`.remote-cover`、`.remote-song[data-mode=card] .remote-cover`、`.remote-seg`、`.remote-queue-song`、`.remote-queue-song__tag`、`.remote-preview__cover`、`.remote-cloud` |
| 卡片底（bg=default, dark） | `linear-gradient(#18181c 0%, #0f0f12 100%)` | css:`.remote-window[data-bg=default] .remote-paper`（队列同值 `css:.remote-queue`） |
| 卡片底（bg=default, light） | `linear-gradient(#fbfaf8 0%, #f3f2ee 100%)` | css:`.remote-window[data-bg=default][data-theme=light] .remote-paper`、`…light] .remote-queue` |
| 卡片底（bg=cover） | `#0d0d10` + 封面 wash 层 | css:`.remote-window[data-bg=cover] .remote-paper`、`.remote-wash`、`.remote-wash-shade{linear-gradient(#09090d75,#09090db3)}` |
| 卡片底（bg=transparent） | 玻璃 `#0d0d11cc` + `inset 0 0 0 1px #ffffff1a`，ring 隐藏 | css:`.remote-glass`（`opacity:0` → `[data-idle=false] .remote-glass{opacity:1}`）、`.remote-queue[data-bg=transparent]` 变体、`.remote-window[data-bg=transparent] .remote-ring{display:none}` |
| 外阴影 + 描边（ring/queue） | `0 1px 3px #00000073, inset 0 0 0 1px rgba(ink,.08), inset 0 1px 0 #ffffff0d` | css:`.remote-ring`、`.remote-queue` |
| light 主题 ring/queue | `0 1px 3px #0000002e, inset 0 0 0 1px #11111617` | css:`.remote-window[data-bg=default][data-theme=light] .remote-ring`、`…light] .remote-queue` |
| paper 裁剪 | `inset(0 0 0 174px round 18px)` → sleeve `inset(0 round 18px)` | css:`.remote-paper`、`.remote-window[data-sleeve=true] .remote-paper` |
| ring 裁剪 | `inset:0 0 0 174px` → sleeve `left:0` | css:`.remote-ring`、`[data-sleeve=true] .remote-ring` |
| wash 外扩 | `inset:-70.4px -168px -70.4px 6px`，`blur(38px) saturate(1.2)` | css:`.remote-wash` |
| transparent 封面投影 | `0 8px 24px #00000052`（sleeve 改 `none` + `border-radius:18px`） | css:`.remote-window[data-bg=transparent] .remote-cover`、`…[data-sleeve=true] .remote-cover` |

### 2.3 字体族与各级字号

族：`Inter, system-ui, -apple-system, Segoe UI, sans-serif`；`-webkit-font-smoothing:antialiased`；根 `line-height:normal`（css:`.remote-window`）。时间戳/序号/数字域统一 `font-variant-numeric:tabular-nums`。

| 用途 | 选择器 | 字号 / 行高 / 字重 / 颜色 |
|---|---|---|
| 在用卡片标题（大字，2 行截断） | `.remote-song__cardtitle` | 20px / 26px / 600 / `rgb(ink)`，letter-spacing -.012em，line-clamp 2 |
| 在用卡片歌手 | `.remote-song__cardartist` | 13px / 19px / 400 / `rgba(ink,.58)`，nowrap ellipsis |
| 单行标题行容器 | `.remote-song__row` | 高 22px，11.5px / 22px，nowrap，width fit-content |
| 单行标题 | `.remote-song__title` | 600 / `rgba(ink,.84)`，ellipsis |
| 单行歌手 | `.remote-song__artist` | max-width 110px / 400 / `rgba(ink,.46)`，margin-left 8px |
| 歌词大字 | `.remote-lyric__hero` | 20px / 26px / 600 / `var(--remote-unsung)`，clamp 2 |
| 歌词大字（小档） | `.remote-lyric__hero[data-size=small]` | 17px / 22px |
| 歌词罗马音 | `.remote-lyric__roma` | 11px / 15px / `rgba(ink,.44)`，letter-spacing .02em |
| 歌词副行（译文/下一句） | `.remote-lyric__sub` | 13px / 19px / 400 / `var(--remote-second)`，clamp 2；`[data-lines="1"]` 单行 ellipsis；`[data-kind=next]` → `var(--remote-next)` |
| 时间戳 | `.remote-deck__slot--time`（`i` = " / 总时长"） | 11px / 500 / `rgba(ink,.78)`；`i` → `rgba(ink,.38)` |
| 队列行歌名 | `.remote-queue-song__name` | 12.5px / 500 / `rgba(ink,.86)`（当前行 `rgb(ink)` / 700） |
| 队列行歌手 | `.remote-queue-song__artist` | 11px / `rgba(ink,.45)`，flex-shrink 2 |
| 队列行序号 | `.remote-queue-song__position` | 10.5px / 500 / `rgba(ink,.32)`，宽 26px 右对齐（当前行 `rgb(ink)`/700） |
| 队列行标签 | `.remote-queue-song__tag` | 9.5px / 15px / `rgba(ink,.6)`，底 `rgba(ink,.1)`，padding 0 5px |
| 队列搜索 | `.remote-queue-search input` | 12px / `rgb(ink)`；placeholder `rgba(ink,.38)` |
| 队列空/无匹配 | `.remote-queue-note` | 11.5px / `rgba(ink,.45)`，padding 10px 18px |
| Sheet 标签 | `.remote-sheet__tabs` | 12px / 22px / 600，gap 16px（选中 .96，未选 .38，hover .7） |
| Sheet 大字状态 | `.remote-sheet__hero` | 20px / 26px / 600 |
| Sheet 副文案 | `.remote-sheet__sub` | 13px / 19px / `rgba(ink,.64)` |
| Sheet 错误 | `.remote-sheet__error` | 11px / 16px / `#ff5d73` |
| 行标签/注记 | `.remote-row__label` / `.remote-row__note` | 11.5px / `rgba(ink,.46)` |
| 分段控件 | `.remote-seg__cell` | 11.5px / 18px / 500（选中 600，底 `rgba(ink,.94)`，字 `var(--remote-paper)`） |
| 按钮 | `.remote-btn` | 11.5px / 600，高 24px（行内 22px） |
| 开关 | `.remote-switch` | 11.5px / `rgba(ink,.5)`（on → .92） |
| 数字输入 | `.remote-field` | 11.5px / 500，52×22，居中 |
| 无封面占位 "F" | `.remote-cover__blank` | 88px / 700 / `rgba(ink,.35)`；card/sleeve 态 76px |
| Hover 预览（上/下一首） | `.remote-preview` | 11.5px / 600 / `rgba(ink,.9)`，max-width 118px；note 变体 11px/13px/clamp 3 |

### 2.4 图标尺寸与间距

| 项 | 值 | 证据 |
|---|---|---|
| 播放/暂停按钮 | 36×36 圆形，svg size 21（playing）/22（paused） | css:`.remote-key--play`；js@1738795 Qje |
| 上/下一首 | 32×32，svg 17 | css:`.remote-key--skip`；js Qje |
| 小按钮（喜欢/循环/队列/更多） | 28×28，svg 15（更多 16） | css:`.remote-key--small`；js Qje |
| transport 间距 | `gap:14px`，`top:24px;left:50%;translate(-50%,-50%)` | css:`.remote-transport` |
| 循环开启指示点 | 3×3 圆点（录制中变 `#ff4d5e`） | css:`.remote-key__dot`、`.remote-key__dot--live` |
| 工具行（背景模式 / 置顶 / 关闭） | 24×24 圆形，svg 13，gap 2px，`top:10px;right:16px` | css:`.remote-tools`、`.remote-tool`；js@1762942 EMe |
| 队列行操作 | 26×26，svg 14，仅 hover/focus-within 显示 | css:`.remote-queue-action`；js@1748946 dMe |
| 搜索清除 | 20×20，svg 12 | css:`.remote-queue-search__clear` |
| Sheet 返回 | 24×24，svg 15，`top:10px;left:13px` | css:`.remote-sheet__back` |
| 队列滚动条 | 宽 10，thumb 圆角 5 + 3px 透明边，hover 提亮 | css:`.remote-queue-scroll::-webkit-scrollbar*` |
| 主要内距 | stage/lyric/line/rows 左右 22px；deck slot 左 22 / 右 16 | css:`.remote-lyric`、`.remote-line`、`.remote-rows`、`.remote-deck__slot--time`、`.remote-deck__slot--keys` |
| 纵向锚点 | stage 高 124 @top 0；song line @top 11 left 53；song card @top 35 left 94 right 22；progress `top:118px`；deck `top:127px;height:49px`；actions `top:46px;height:32px`；sheet 高 124 | css:`.remote-stage`、`.remote-song__line`、`.remote-song__card`、`.remote-line`、`.remote-deck`、`.remote-actions`、`.remote-sheet` |
| cover 三态（176×176 本体） | sleeve：`transform:none; radius 18 0 0 18`；line：`translate(196px,11px) scale(.125)` radius 48；card：`translate(196px,35px) scale(.318182)` radius 34.57 | css:`.remote-cover`、`.remote-song[data-mode=card] .remote-cover`、`[data-sleeve=true] .remote-cover` |
| 封面内描边 | sleeve `.5px`、line `4px`、card `1.57px`，色 `#ffffff24` | css:`.remote-cover:after` 及三变体 |
| 模式切换文字位移 | row `translate:-31px`、cardtext `translate:-72px`（sleeve 态） | css:`.remote-window[data-sleeve=true] :is(.remote-song__row,.remote-song__cardtext)` |
| 无标题时单行标题加宽 | 257px → 345px | css:`.remote-song__line`、`.remote-window[data-notitle=true] .remote-song__line` |

### 2.5 时长与缓动总表

| 场景 | 时长 | 缓动 | 证据 |
|---|---|---|---|
| 队列高度（CSS 侧） | .22s | `--remote-settle` | css:`.remote-window[data-queue=down] .remote-queue` |
| 队列进场（framer-motion） | 340ms（reduceMotion → 0） | `(.32,.72,0,1)` | js@1753726 bMe + `_Me` js@1753554 |
| 队列出场 | 200ms | `(.4,0,.7,.2)` | js@1753572 `vMe` |
| 队列 clip 收展 | 与进场同段 | — | js@1753595 E7（`yMe=4`，inset ±`h+4` → -4，round 22px） |
| paper 裁剪 | .3s `--remote-snap` → sleeve .56s `--remote-settle` delay 60ms | — | css:`.remote-paper`、`[data-sleeve=true] .remote-paper` |
| ring left / cover transform | 同上（.3s / .56s+60ms） | — | css:`.remote-ring`、`.remote-cover` |
| deck 进/出 | .22s（settle+80ms） / .18s | — | css:`.remote-deck`、`[data-idle=true] .remote-deck` |
| idle 时 progress 下移 | translateY(34px)，.44s settle delay .12s；非 idle .24s snap | — | css:`.remote-line`、`[data-idle=true] .remote-line` |
| 歌词换行 | 进 .26s `[.2,.8,.2,1]`；出 `v7=.18s easeIn` + 20ms 落地 | — | js@1746196 sMe（`v7` js@1744515，`aMe=20` js@1744522） |
| glass / cloud / lit | .32s / .32s / .22s(snap 80ms)；idle 时 .14s / .18s | — | css:`.remote-glass`、`.remote-cloud`、`.remote-lit` |
| 换高期间隐藏整窗内容 | 90ms | ease-out | css:`.remote-window[data-shifting=true]` |
| idle 提交前淡出/淡入 | 90ms / 140ms（WAAPI） | ease-in / ease-out | js@1767541 `LMe=90`、`RMe=140`，zMe js@1767556 |
| 按钮 hover/active | .14s / active scale(.94)、.12s | ease-out | css:`.remote-key`、`.remote-btn`、`.remote-tool`、`.remote-action` |
| Sheet 进/出 | .18s（y ±4） | easeOut | js@1759081 wMe |
| 无障碍降级 | `data-reduce-motion~=uiMicroMotion` 时窗内全部 transition duration/delay = 0s | — | css 末条 |

---

## 3. 数据模型

字段级清单（名 / 类型 / 来源 / 赋值行）见同目录 **SNAPSHOT-MODEL.md**（A3）。此处只给接线：

- 生产者：主窗口渲染层 `publishRemoteControlSnapshot`，首帧 `{includeLyrics:true}`，之后 `setInterval(…, 500)` + 事件即时（js@1439927；DPR 变化时 `reportDevicePixelRatio`）。
- 缓存：`latestRemoteControlSnapshot`（main:2519 声明；main:7602-7628 赋值；main:7630-7636 读取）。
- 消费者：remote 页 `getRemoteControlSnapshot()` 拉初值 + `onRemoteControlSnapshot` 增量合并（js@1725821 Nje）；`lyrics` / `queue` **仅当 payload 显式带该 key 才覆盖**（`Object.prototype.hasOwnProperty`，非 `!== undefined`）。
- `withoutRemoteControlQueue`（main:5167-5170）：`const {queue:_queue, ...rest} = snapshot`，用于"这次没带队列"的转发。
- `patchRemoteControlSnapshot`（main:5172-5183）：合并补丁 → 走 `without` 转发；**保留 `updatedAt`**（注释 main:5177：那是主窗口读播放时刻的时间，这里不重读）。补丁调用点：main:3266-3268（`mainWindowAlwaysOnTop`）、main:5285-5287（`mainWindowClickThroughEnabled`）、main:6034-6037（`transparentModeEnabled` + `mainWindowClickThroughEnabled:false`）。

IPC 通道全表（`window.electron` 方法名 → 通道）：

| 方法（preload:行） | 通道 | 允许来源（main 校验行） |
|---|---|---|
| openRemoteControl (270) | `remote-control-open` | 主窗口 7497 |
| toggleRemoteControl (271) | `remote-control-toggle` | 主窗口 7506 |
| closeRemoteControl (272) | `remote-control-close` | remote 页 7520 |
| getRemoteControlAlwaysOnTop (273) | `remote-control-get-always-on-top` | 两者 7533 |
| setRemoteControlAlwaysOnTop (274) | `remote-control-set-always-on-top` | remote 页 7550 |
| getRemoteControlWindowSettings (275) | `remote-control-get-window-settings` | 两者 7567 |
| onRemoteControlWindowSettingsChanged (276-280) | 事件 `remote-control-window-settings-changed` | 下发 3223 |
| getRemoteControlPointerInside (281) | `remote-control-get-pointer-inside` | remote 页 7575（Wayland 返 null 7579-7581） |
| setRemoteControlQueueExpansion (282) | `remote-control-set-queue-expansion` | remote 页 7588 |
| onRemoteControlPointerInside (283-287) | 事件 `remote-control-pointer-inside` | 下发 5350 |
| publishRemoteControlSnapshot (288) | `remote-control-publish-snapshot` | 主窗口 7603 |
| getRemoteControlSnapshot (289) | `remote-control-get-snapshot` | 两者 7631 |
| sendRemoteControlCommand (290) | `remote-control-send-command` | remote 页 7639 |
| onRemoteControlCommand (291-295) | 事件 `remote-control-command` | 主窗口，转发 7686 |
| onRemoteControlSnapshot (296-300) | 事件 `remote-control-snapshot` | remote 页，发送 5307 |

鉴权：`isTrustedMainWindowContents`（main:3587-3594）、`isTrustedRemoteControlContents`（main:3596-3603），按 `webContents.id` 严格比对。

---

## 4. 行为规格

### 4.1 拖动
- 拖动区 = `.remote-face` 整块（`WebkitAppRegion:'drag'`，`jje` js@1725098）；卡内每颗交互控件用 `e7={WebkitAppRegion:'no-drag'}`（js@1725127）单独关掉 → "除按钮外整窗可拖"。
- Windows：`will-move` 每帧 clamp（§1.4），越界即 `preventDefault + setBounds`，观感是"沿屏幕边缘滑动"（main:5382-5390）。
- 其它平台拒绝不了移动，只在 `moved` 后落盘，越界靠下次开窗 restore 回退（§1.5）。
- 队列换高期间 `data-shifting=true`（isShifting，js@1750861 gMe）→ 整窗 opacity 0 / 90ms（css:`.remote-window[data-shifting=true]`），避免看到平台重排跳变。

### 4.2 置顶 alwaysOnTop
- `remoteControlAlwaysOnTop` 默认 **true**（main:2799；key `REMOTE_CONTROL_ALWAYS_ON_TOP` main:2597）。
- 施加：`win.setAlwaysOnTop(flag,'screen-saver')` + `moveTop()`（main:3189-3192）；创建参数（main:5525）；`ready-to-show` 再施一次（main:5581）；复用路径也施（main:5489）。
- 平台不支持（Wayland）→ `windowAlwaysOnTopSupported=false`：get 直返 false（main:7538-7540），set 无效（main:7554-7556），并随 settings 下发 `alwaysOnTopSupported`（main:3211），渲染层禁用按钮 + 换 tooltip（js@1762942 EMe）。
- 页内切换：`setRemoteControlAlwaysOnTop(!cur)`，按返回值回填，失败回滚（js@1762942）。

### 4.3 跳过任务栏
- `remoteControlSkipTaskbarEnabled` 默认 **false**（main:2800；key `REMOTE_CONTROL_SKIP_TASKBAR` main:2598）；创建参数 main:5526；`applyRemoteControlSkipTaskbar` → `win.setSkipTaskbar()`（main:3196-3203）；复用路径 main:5490。

### 4.4 隐藏标题栏
- 系统层：`frame:false`（main:5515）+ `autoHideMenuBar:true`（main:5521）+ `hasShadow:false`（main:5517）+ `transparent:true`/`backgroundColor:'#00000000'`（main:5516,5518）。
- 设置层：key `REMOTE_CONTROL_HIDE_TITLEBAR`（settings:5，默认 false）→ `settings.hideTitlebar`（main:3207）→ `data-notitle = hideTitlebar || clickThrough`（js@1770916）。
- 视觉差：`data-notitle=true` → `.remote-tools{display:none}`（工具行整体隐藏）+ 单行标题宽 257→345。

### 4.5 点击穿透
- 两触发，共用一个 80ms 轮询（main:5323 + main:5542-5544 `setInterval(pollRemoteControlPointer, REMOTE_CONTROL_POINTER_POLL_INTERVAL_MS)`；Wayland 不启动）。
  1. 用户开关 `REMOTE_CONTROL_CLICK_THROUGH`（settings:6）→ 整窗穿透；
  2. 光标进 strip（左 178px）→ 局部穿透（main:5361-5362，`isPointOverRemoteControlStrip`）。
- 落地：`applyRemoteControlMouseIgnore(win, ignore)` → `win.setIgnoreMouseEvents(bool)`，**故意不带 `{forward:true}`**（settings:26-42 注释：穿透时页面绝不能收到 hover/move，否则 hover 态卡死）。对照：主窗口穿透用 `{forward:true}`（main:5257-5260）。
- 幂等：只在结果变化时调（main:5363-5365，`remoteControlMouseIgnored` 比较）。
- 指针是否进窗（另一路，供 hover/lit 用）：`isCursorInsideRemoteControlWindow`（main:5328-5340：可见 && 未最小化 && 未开全局穿透 && 光标在 bounds 内），变化时推 `remote-control-pointer-inside`（main:5342-5352）。Wayland 无法回答 → get 返回 null（main:7579-7581），页面退回 `document.mousemove` + `documentElement.mouseleave` 自测（js@1726946 Ije）。
- 托盘解锁项仅在"窗开着 + 穿透中"出现（`shouldShowRemoteUnlockTrayItem` settings:22-25 → main:3372-3377，点击 `setRemoteControlClickThroughEnabled(false)`）。
- 开关持久化 + 广播：`setRemoteControlClickThroughEnabled`（main:3229-3238）写 store、下发 settings、刷托盘、并向主窗口发 `wallpaper-mode-changed`。

### 4.6 expandCover（封面展开）
- key `REMOTE_CONTROL_EXPAND_COVER`（settings:7），**默认 true**（settings:13-19；main:2529；启动读 main:2802-2807）。
- 视觉位：`data-sleeve = isIdle && settings.expandCover`（js@1770916：`M = j && r.expandCover`）。
- 关闭后卡左侧 178px 恒空 → clamp/restore 的 inset 变 178（§1.4/§1.5），打开时把窗口右移回屏（main:5395-5406）。
- 无独立"展开动画"逻辑，全部走 CSS（§2.5 paper/ring/cover 三段的 .3s ↔ .56s+60ms 切换）。

### 4.7 textShade（透明背景文字底衬/描边）
- key `REMOTE_CONTROL_TEXT_SHADE`（settings:8，默认 false）→ `settings.textShade`（main:3210）。
- 生效条件：`N = backgroundMode==='transparent' && settings.textShade`（js@1770916）。
- 开：CSS 给 `.remote-song,.remote-lyric__hero,.remote-lyric__roma,.remote-lyric__sub,.remote-transport` 叠 `filter:drop-shadow(0 0 .6px #000000e6) drop-shadow(0 1px 2px #0000008c)`（`css:.remote-window[data-bg=transparent][data-idle=true] :is(…)`），并渲染 `.remote-cloud`：JS 聚合所有 `[data-remote-shade]` 文本的外接框写进绝对定位底衬（js@1768072 WMe；外扩常量 k7=14 / BMe=40 / A7=4 / VMe=10 / 左夹 HMe=22，js@1767977-1768009；样式 `blur(12px) #0009 radius 26`，css:`.remote-cloud`）。重算触发：MutationObserver（childList/subtree/characterData/attributes，attributeFilter = UMe `[data-remote-shade,data-size,data-lines,data-mode]`）+ `transitionend` + rAF 节流；无可见文本 → `data-empty=true` → `opacity:0`。
- 关：`shade=undefined`（`d = i ? '' : void 0`，js@1744879），元素不带 `data-remote-shade`，cloud 不渲染。
- 背景模式本身存 localStorage `remote_background_mode`（js@1770736 `j7`，取值 default/cover/transparent，循环顺序 default→cover→transparent→default，js@1770916 `onCycleBackgroundMode`）——**不在 electron-store 里**。

### 4.8 队列上下展开动画与 --remote-settle
状态机 js@1750861（gMe）。要点：
- 常量：`x7=90`（锚定期）、`pMe=220`（变矮前沉淀）、`fMe=6000`（无指针且搜索框无焦点 6s 自动收起）。
- 打开：`preview:true` 问方向 → 置 `data-queue=direction` 把卡片锚到该侧（`top:4px` / `bottom:4px`）→ 等 2 帧 → 真 resize → `hMe(targetHeight)` 等 `resize` 事件或 300ms 超时（js@1750559）→ 清 `isShifting`。
- 已在 up 且要变高：`n(fn, S7 + e.height)` 等页面高到位。
- 已在 down 且只需变矮：先改面板高不动窗，`T7(220)` 后再收窗（防抖）。
- 关闭：down 等 200ms 收窗；up 等 `200 - x7/2 = 155`ms 后走"锚定 → 收窗"。
- 面板动画：CSS `height .22s var(--remote-settle)`（`top:186px`/`bottom:186px` 定位）+ framer 进 340ms / 出 200ms（§2.5）；`clip-path` 由 E7 生成（±4 出血，round 22px）。
- 面板内：搜索框（`data-remote-queue-search`）、`ArrowUp/Down` 焦点跳（跳首行时聚焦搜索框）、`Enter` 播放第一条匹配、`Esc` 先清查询再关、行 hover 出 3 颗操作（play-next / move-to-end / remove），命令 `queue-play`/`queue-play-next`/`queue-move-to-end`/`queue-remove`（js@1748946 dMe；主窗口侧判定 `M3` js@1419186）；`queueReadOnly`（stage 上下文）或 `controlsDisabled` 时不给操作。
- 虚拟化：`rowHeight:36`、`overscanCount:4`、滚动 `scrollToRow({align:'auto',behavior:'instant'})`。

### 4.9 位置持久化
- key `REMOTE_CONTROL_WINDOW_POSITION`（settings:10；读写 main:5478 / 5567），值 `{x,y}`，且**永远是卡片位置**（`getRemoteControlCardPosition`，main:5566）。
- 写时机：`moved` → 清 pending + `setTimeout(persist, WINDOW_STATE_SAVE_DEBOUNCE_MS=300)`（main:5570-5573）；`close` → 立即写并清 pending（main:5574；注释 main:5553-5557 说明 macOS 每帧触发 moved 而 electron-store 同步重写，所以必须防抖）。
- 读时机：main:5503-5504（restore ?? 初始居中）。

### 4.10 idle 与 in-use 的视觉差
`wantedIdle = !hoverOrFocus && !sheet.isOpen && !queue.isOpen`，再经 `zMe(wantedIdle, reduceMotion, cardRef)` 提交（js@1767556：非 reduceMotion 时先用 WAAPI 跑一次 opacity 0→1（淡入 140ms / 淡出 90ms）才改态，避免交叉闪）。落到 `data-idle` / `data-sleeve` / `data-lit`：

| 元素 | idle（data-idle=true） | in-use（data-idle=false） | 证据 |
|---|---|---|---|
| deck（transport + 两侧 slot） | `opacity:0; translateY(6px); pointer-events:none`（.18s） | 可见（.22s settle+80ms 入场） | css:`.remote-deck`、`[data-idle=true] .remote-deck` |
| progress 线 | `translateY(34px)`（.44s settle delay .12s） | 原位 `top:118px`（.24s snap） | css:`.remote-line`、`[data-idle=true] .remote-line` |
| cover | 176×176 齐左（`transform:none`，radius 18 0 0 18） | 小封面：line 22px@translate(196,11) r48；card 56px@translate(196,35) r34.57 | css 见 §2.4 |
| paper / ring 裁剪 | `inset(0 round 18px)` / `left:0`（含左 174） | `inset(0 0 0 174px)` / `inset:0 0 0 174px`（只画右 420） | css:.remote-paper / .remote-ring + sleeve 变体 |
| glass（透明背景） | `opacity:0` | `opacity:1`（.14s） | css:`.remote-glass`、`[data-idle=false] .remote-glass` |
| cloud（textShade） | 参与底衬计算 | 不参与 | css:`.remote-window[data-bg=transparent][data-idle=true] :is(…)` |
| 文本色 | transparent+idle 走固定白（§2.1） | 走主题 ink | §2.1 |
| 歌词 | `data-shown=false` → opacity 0（.18s） | 显示当前行 + 副行 | css:`.remote-lyric[data-shown=false]` |
| 封面暗化 | 暂停时 `brightness(.78) saturate(.75)` | 无 | css:`[data-idle=true][data-paused=true] .remote-cover__image` |
| lit（hover/focus 强化） | `data-lit = O && !isIdle`，其中 `O = !clickThrough && (hoverLinger || focus-visible)` | 同 | js@1770916；css:`.remote-lit`、`[data-lit=true] .remote-line__track`（2px→3px，`rgba(ink,.11)`→`.16`）、`[data-lit=true] .remote-lit{opacity:1}` |
| track/fill 颜色 | lit 时 fill 变实色 | 半透明 | css:`[data-lit=true] .remote-line__fill` |

### 4.11 关闭按钮与最小化
- 关闭：`.remote-tools` 第三颗 → `window.electron.closeRemoteControl()`（js@1762942）→ `remote-control-close`（main:7519-7530）→ `close()`；`close` 先落盘位置（main:5574），`closed` 清定时器/态 + 刷托盘（main:5588-5598）。托盘 checkbox 项 create/close 二选一（main:3359-3371）；命令面板项 `desktop-toggle-remote-control`（js@249926）。
- 最小化：`minimizable:true / maximizable:false`（main:5523-5524）；`isMinimized()` 仅用于"指针不算在窗内"（main:5330）。**remote 页没有最小化按钮，源码中也不存在针对 remote 窗的 `.minimize()`**（全仓 `.minimize()` 唯一命中 main:7106，属 mainWindow）。系统级最小化仍可发生（minimizable:true）。
- 无 remote 窗的 resize/maximize 逻辑；尺寸只在队列展开时变（main:5501 注释 + §1.6）。

### 4.12 页面计时器（hover / 预览 / seek / 自动收起）

| 常量 | 值 | 作用 | 证据 |
|---|---|---|---|
| YMe | 800ms | 指针离窗后 hover 态保留（防 idle 抖动） | js@1770764 + Lje js@1727510 |
| Zje | 350ms | transport hover 350ms 才显示上/下一首预览 | js@1738657 |
| cMe | 150ms | range 停止拖动 150ms 后提交 seek | js@1747284 |
| DMe | 800ms | seek 释放后 800ms 内不覆盖 seekPreview | js@1763936 |
| x7 | 90ms | 队列换高前锚定等待 | js@1750400 |
| pMe | 220ms | 队列变矮沉淀后再收窗 | js@1750392 |
| fMe | 6000ms | 队列无指针无焦点自动收起 | js@1750384 |
| FMe | 6000ms | Sheet 同上 | js@1766298 |
| v7 / aMe | .18s / 20ms | 歌词换行出场 + 落地 | js@1744515 / 1744522 |
| rMe | 26px | 歌词行高（用于测行数 → clamp 档） | js@1744496 |
| jMe | 3s | 下一句提前判定"跟唱"窗口 | js@1765465 |
| GMe | 5000ms | 手动切歌意图保留期（决定 handoff 方向） | js@1768968 |
| LMe / RMe | 90 / 140ms | idle 提交前的淡出/淡入 | js@1767541 / 1767548 |
| 500ms | — | 快照发布间隔（主窗口） | js@1439927 |
| 80ms | — | 主进程指针轮询 | main:5323 |
| 300ms | — | 位置保存防抖 | main:2576 |

### 4.13 命令表（remote → 主窗口 → 主窗页）
`remote-control-send-command`（main:7638-7688）：主进程直接消化 `set-main-window-click-through`、`set-main-window-always-on-top`、`set-transparent-mode-enabled`、`disable-transparent-mode`、`resize-main-window`（main:7643-7680，后者走 `sanitizeVideoExportSize` main:5604-5616：≥320 且 ≤3840）；其余原样广播 `remote-control-command`（main:7686）。
页面实发 type 全清单（js 检索汇总）：`play-pause`、`previous`、`next`、`seek`(`{type:'seek',time}`)、`toggle-like`、`cycle-loop-mode`、`queue-play`、`queue-play-next`、`queue-move-to-end`、`queue-remove`、`remote-action`（模组动作 `{type,epoch,handle,trackKey}`，js@1757424）、`stop-export`、`open-export`、`resize-main-window`、`set-transparent-mode-enabled`、`set-main-window-border-visible`、`cycle-player-chrome-visibility-mode`、`set-player-chrome-visibility-mode`。
主窗口兜底：未识别 type 且当前在播 → 切播放/暂停（js@1442222 区）。

---

## 5. WPF 复刻映射建议

单位换算：Electron 用 DIP（96dpi 基准 CSS px），WPF 也用 DIP → **所有 px 数值原样当 WPF unit 用**；DPR 只影响位图采样密度（模糊/wash）。

### 5.1 窗口层

| Folia | WPF | 说明 / 可达性 |
|---|---|---|
| 602×184 固定 | `Width=602 Height=184 ResizeMode=NoResize SizeToContent=Manual` | ✅；队列态改 `Height`（up 同时改 `Top`，§5.4） |
| `frame:false transparent:true hasShadow:false` | `WindowStyle=None AllowsTransparency=True Background=Transparent` | ✅；AllowsTransparency 即 WS_EX_LAYERED（per-pixel alpha），原生阴影自动没有 |
| `backgroundColor:'#00000000'` | 同上（`Transparent`） | 别用 `Window.Opacity`，会连子树一起半透明 |
| `minimizable:true maximizable:false resizable:false` | `ResizeMode=NoResize`（系统仍可最小化） | ✅；复刻时不加最小化按钮（§4.11） |
| `skipTaskbar` | `ShowInTaskbar=False`（绑开关） | ✅，默认 false |
| `setAlwaysOnTop(flag,'screen-saver')+moveTop()` | `Topmost=true`；要严格对齐 screen-saver 带，P/Invoke `SetWindowPos(hwnd, HWND_TOPMOST, …)`（必要时 NOTOPMOST→TOPMOST 强制） | ⚠️ WPF 只有 WS_EX_TOPMOST，没有 level |
| `page-title-updated` 复位 | `Title="Folia Remote"` + `SourceInitialized` 里 `SetWindowText` | ✅（None 样式下只影响 alt-tab/任务栏文本） |
| `-webkit-app-region:drag/no-drag` | 卡片 Grid `MouseLeftButtonDown → DragMove()`；更忠实：钩 `WM_NCHITTEST` 对非控件区返回 `HTCAPTION` | ✅；HTCAPTION 才保留系统拖动 + DWM 贴边 |
| `will-move` clamp + `preventDefault` | 钩 `WM_MOVING`（原地改 `RECT.left`）或 `WM_WINDOWPOSCHANGING`，用 §1.4 同一算法 | ⚠️ `DragMove()` 不派发事件，只能在 hook 里钳 |
| `moved` 300ms 防抖存 `{x,y}` | `LocationChanged` + `DispatcherTimer(300)`；`OnClosing` 立即存 | ✅ |
| restore 跨屏校验 | `SystemParameters.WorkArea` + 多屏 bounds（`Screen.AllScreens`，物理 px ÷ (Dpi/96) 转 DIP） | ✅ |
| `setIgnoreMouseEvents(bool)`（整窗 / strip） | 80ms 轮询 `PointToScreen` vs 窗口 rect → 切 `WS_EX_TRANSPARENT`（GWL_EXSTYLE） | ✅；`AllowsTransparency=True` 已提供 LAYERED。Folia **不** forward → 不要做 HTTRANSPARENT+SendInput 转发 |
| `remote-control-pointer-inside` 推送 | 同轮询里算 `IsMouseOverRect` → VM 属性 | ✅ 间隔照抄 80ms（main:5323） |

### 5.2 卡片视觉

| CSS | WPF | 可达性 |
|---|---|---|
| `border-radius:18px` | `Border CornerRadius="18"` | ✅ |
| `linear-gradient(#18181c,#0f0f12)` | `LinearGradientBrush 0,0→0,1` + 2 GradientStop | ✅ |
| `inset 0 0 0 1px rgba(ink,.08)` | Grid 叠一层 `Border BorderThickness=1`（`SnapsToDevicePixels=True`） | ✅ inset 不占布局 → 用叠层，不用 margin |
| `inset 0 1px 0 #ffffff0d` | 顶部 1px `Rectangle Fill=#0DFFFFFF VerticalAlignment=Top` | ✅ |
| `box-shadow:0 1px 3px #00000073` | `DropShadowEffect BlurRadius=6 ShadowDepth=1 Direction=270 Color=Black Opacity=0.451` | ⚠️ WPF BlurRadius 是半径，CSS blur radius ×2 近似；0x73=45.1% |
| light 主题描边/阴影 | 同上换 `#0000002E` / `#11111617` | ✅ |
| `clip-path:inset(0 0 0 174px round 18px)` | **无等价**。A：paper 只画右 420（`Margin="178,4,4,4"`，sleeve 态把左 margin 动画到 4）；B：`OpacityMask` = `DrawingBrush` 里 `RectangleGeometry{RadiusX=18}`，动画改 X/Width | ⚠️ 单边圆角收缩只能靠几何遮罩 |
| `18px 0 0 18px` | `CornerRadius="18,0,0,18"` | ✅ |
| cover `translate()+scale()` | `TransformGroup{TranslateTransform; ScaleTransform}`，`RenderTransformOrigin=0,0`（CSS `transform-origin:0 0`；WPF 默认 0.5,0.5 必须显式改） | ✅ |
| 小封面 radius 48 → 视觉 6 | 在 scale 后的框上直接 `CornerRadius=6`（48×0.125） | ✅ 等价写法，省一层 scale |
| `filter:blur(38px) saturate(1.2)`（wash） | `BlurEffect Radius=76`；**saturate 无原生**（需 ShaderEffect），或退化为 `.remote-wash-shade` 的 `#09090d75→#09090db3` 渐变 | ⚠️ 且"对窗口后方内容模糊"（backdrop blur）WPF 做不到，只能模糊自备位图 |
| `backdrop-filter` | 无 | ❌（Folia 本窗没用，别引入） |
| `.remote-cloud`（模糊底衬 + 动态外接框） | `Border Background=#99000000 CornerRadius=26 + BlurEffect Radius=24`；几何用 `TransformToVisual` 聚合带 `Tag="Shade"` 的 TextBlock，`LayoutUpdated` 节流重算 | ✅ 注意 BlurEffect 强制位图化，只 blur 这一小块 |
| 文字 `drop-shadow×2`（描边） | 无 CSS 文字描边。① `DropShadowEffect{BlurRadius=1,ShadowDepth=0,Opacity=0.898}` + 第二层 `{BlurRadius=2,ShadowDepth=1,Opacity=0.549}`（需两层 Effect 或外层 Grid）；② `FormattedText`/`GlyphRun` 自绘加 `Pen` | ⚠️ 近似 |
| `-webkit-background-clip:text` + `--remote-fill` 逐字推进 | 每字一个 `Run`/`TextBlock`，`Foreground` = 带 2 stop 的 `LinearGradientBrush`（stop 位置绑 fill%）；或用 `OpacityMask` 横向矩形 | ⚠️ 需自排版；`box-decoration-break:slice` 无对应 |
| `-webkit-line-clamp:2` | `TextBlock TextWrapping=Wrap MaxHeight=52 TextTrimming=None`（裁掉多余行；无 …） | ❌ 原生无 line-clamp |
| `letter-spacing:-.012em` | `TextBlock` 无 tracking（`Typography` 不解决） | ❌ 忽略或 `GlyphRun` 自绘字距 |
| 字体族 | `FontFamily="Inter, Segoe UI"`（Inter 需随包字体 `FontFamily="/…#Inter"`） | ✅ |
| `font-variant-numeric:tabular-nums` | `Typography.FontVariant="TabularNums"`（或 `FontFeatureValues`） | ✅ |
| `cubic-bezier(.32,.72,0,1)` / `.2,.8,.2,1` / `.4,0,.7,.2` | `CubicEase{EasingMode=EaseOut, ControlPoints="0.32,0.72 0,1"}` 等 | ✅ KeySpline ≡ CSS cubic-bezier |
| `transition-delay:60ms/80ms/120ms` | Storyboard `BeginTime="0:0:0.06"` | ✅ |
| `::-webkit-scrollbar` 定制 | 重写 `ScrollBar` ControlTemplate（宽 10、thumb CornerRadius=5 + Margin=3 透明边、track 透明） | ⚠️ 工作量大，可先默认 |
| `pointer-events:none` | `IsHitTestVisible=False` | ✅ |
| `user-select:none` | 容器 `IsHitTestVisible` + 仅搜索框用可交互 `TextBox` | ✅ |
| `cursor:pointer` | `Cursor=Hand` | ✅ |
| `outline:1.5px solid …; offset:1px` | 焦点模板 `Rectangle Stroke` + `Margin=-2.5` | ✅ |
| 透明 range 输入（`appearance:none;opacity:0` 覆盖自绘进度条） | 双图层：自绘 track/fill/knob + 上层 `Slider Opacity=0` 铺满（`IsMoveToPointEnabled`）做命中 | ✅ 最省事的等价方案 |
| `role`/`aria-*` | `AutomationProperties.Name/HelpText` + 用 `RadioButton`/`CheckBox` 获得正确 ControlType | ⚠️ 语义降级可接受 |

### 5.3 视觉树（照 §5.1/§5.2 的层级）

```
Window(602x184, None, AllowsTransparency, Background=Transparent, ShowInTaskbar=False, Topmost=True)
 Grid                                   // = .remote-card 594x176 居中，四周 4px
  ├ Border#paper  Margin=左 178→4       // 渐变 / cover 色 / wash
  ├ Grid#face     宽 420，左 178，可拖    // 交互层
  │   ├ Border#ring  描边+阴影，左 178→4
  │   ├ ContentControl#cover 176x176 (RenderTransformOrigin=0,0)
  │   ├ Grid#stage 高 124 @top 0        // song / lyric / sheet
  │   ├ Grid#line  @top 118 (idle +34)
  │   └ Grid#deck  @top 127 高 49        // slot-left / transport / slot-right
  ├ Border#glass  (transparent 背景，idle 隐藏)
  ├ Border#tools  右上 3 颗 @top10 right16
  └ Border#queue  宽 420 左 178，top 186 或 bottom 186，高绑定动画 .22s settle
```

### 5.4 队列换高落地顺序（对齐 §4.8）
1. `wanted = 46 + clamp(queueCount,1,8)*36 + 6`；由 `workArea` 求 `room.up/down`，按 §1.6 选侧 + MIN 160 截断。
2. 卡片锚定（`VerticalAlignment` Top/Bottom 切换），`Shifting=true`（整窗 Opacity 0，90ms）。
3. `Height = 184 + h`；up 侧同帧 `Top -= h`。
4. 等 `ActualHeight` 到位（`SizeChanged` 或轮询，上限 300ms，对应 `hMe`）。
5. `Shifting=false`；队列面板 clip + opacity（340ms / `.32,.72,0,1`）。
6. 关闭：down 等 200ms、up 等 155ms 再收窗。

---

## 6. 缺口 / 源码中不存在（必须显式记入实现清单）

1. **remote 窗内无最小化按钮，也无 `.minimize()` 调用**（全仓唯一 `main:7106` 属 mainWindow）。
2. **没有窗口尺寸动画**：换高是一次性 `setBounds`（main:5414-5425），靠 `data-shifting` 隐内容掩盖跳变。WPF 若给 Height 加动画反而偏离原实现。
3. **队列无滚动吸附动画**：`scrollToRow({align:'auto',behavior:'instant'})`（js@1753726）。
4. **透明背景不做真 blur**：`.remote-glass` 只是 `#0d0d11cc` 半透明；未使用 `backdrop-filter`。复刻别上毛玻璃。
5. **无 remote 快捷键**：globalShortcuts.cjs 不含 remote 窗项（命令面板有 `desktop-toggle-remote-control`，js@249926）。
6. **背景模式不持久化到 electron-store**：存 `localStorage['remote_background_mode']`（js@1770736）；置顶/穿透/封面展开/文字底衬/隐藏标题栏/位置 6 项才在 store（settings:5-10）。
7. `--remote-settle` 只在 sleeve 与队列高度两处使用，其余一律 `--remote-snap`：别全局套同一缓动。
8. idle 判定不是纯布尔：需等一次 WAAPI 淡入淡出（js@1767556）才提交，否则出现交叉闪。
9. `lyrics`/`queue` 的"仅在携带时覆盖"协议（main:7607-7625 + js@1725821）必须一起复刻；判据是 `hasOwnProperty`，不是 `!== undefined`。
10. 队列面板 `clip-path` 的 round 半径是 **22px**（E7），卡片是 18px：两套半径不同，不要合并。
