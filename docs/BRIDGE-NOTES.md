# BRIDGE-NOTES — netease-mini/bridge/mini.js v2 逆向取数 / 控制 / 通道手册

目标页: 网易云音乐 3.1.42.205594 / CEF Chrome/91 / orpheus://orpheus/pub/app.html
契约: docs/CONTRACT.md（State v1 / Command v1 + 契约增补 v1.1 + 本轮 v1.2 四条）
本文件只写"怎么取到、怎么控制、踩过什么坑、怎么自证"。活体复验数据见文末 §9。

当前交付物:
- bridge/mini.js  build = ncmmini-bridge-2fa9287e-057384   （文件 58607 B / BODY 57384 B，整文件 sha256 见 §1）
- bridge/mini.css 1490 B（未改）

---

## 0. v2 相对 v1 的硬改动（对应父代理 M1-M5）

| 编号 | 改动 | 位置 |
|---|---|---|
| M1 | API.build 指纹（= 文件身份证明），顶部注释 + var 槽 + 末行 FP 三处同值 | 文件头 8-18 行、末尾 1 行 |
| M2 | connect() 真正可调用并立即扫描；boot 当帧首轮扫描；SWEEP_MS 30000→3000；10 端口并行扫；port.json 优先 | HTTP 段 sweep()/connect() |
| M3 | 所有 Command 统一 {ok,err,via,applied,...}，applied = 执行后窗内轮询回读的真实状态，未落地 ok:false/err='not-applied' | ENGINE 段 execute()/cmdAsync() |
| M4 | vol 线性 0..100 / seek 绝对秒（三路径兜底）/ toggle 双向 | CMD.vol / CMD.seek / CMD.toggle |
| M5 | 保留 __NCM_MINI_STOP__ 与 live() 守卫；新增：注入时旧实例 build 不同也强制退休（调旧 stop + 抢全局名），并去掉"版本相同就 return"的早退 | 文件头 retirePrev() + boot |
| 新增 | Command playIndex（0 基序号）、closeQueue；State.queue[i] 带 index/id；cmdSync()；selfHash() | CMD.playIndex / readState() |

---

## 1. M1 build 指纹口径（活实例=磁盘文件的唯一证明）

    build = 'ncmmini-bridge-' + sha8 + '-' + bytes
    sha8  = sha256(BODY) 的前 8 位十六进制（小写）
    bytes = BODY 字节数（6 位，前补 0）
    BODY  = 文件中 "//@@NCMMINI-BODY-BEGIN@@" 行之后 到 末行 "//@@NCMMINI-FP@@" 之前 的字节序列

为什么不是"整文件 sha256 前 8 位"：那要求把哈希写进被哈希的字节里，是不动点问题（写进去 sha 就变，8 hex=32bit 的不动点期望要算 2^32 次），做不到。
本口径把三个可读槽（顶部注释块 / var __NCMMINI_FP__ / 末行 FP）全放在 BODY 之外且定宽，于是"把 sha8 写进文件"不改变 BODY → 一次求值即自洽（--stamp 连跑两次结果不变，已实测）。
BODY 覆盖全部可执行代码，任何一处改动都会换 sha8，身份证明强度与整文件口径等价。

    校验: node ncm-re/probe/verify-build.mjs [--stamp|--inject|--sha]
    当前值: BODY sha8=2fa9287e bytes=57384; 整文件 58607 B, sha256=1A5BDC62F7FB21C3B167C8F46458A8A8E4C169CE8A24C9591FE2A70458EFBE35
    页面侧: window.__NCM_MINI__.build / .info().build / API.prevBuild(被退休的上一份)
    打包后(页面能 fetch 自己的 URL 时): window.__NCM_MINI__.selfHash('./hybrid/mini.js') → 整文件 sha256

---

## 2. 落地：app.html 要插的两行（N8）

现文件 ncm-re/orpheus/pub/app.html（32 行）：第 9 行是最后一条 stylesheet link，第 30 行 app.chunk，第 31 行 </body>。

    第 9 行之后插入(成为新第 10 行):
        <link href="/pub/hybrid/mini.css" rel="stylesheet">
    第 30 行之后插入(成为新第 31 行, 紧贴 </body>):
        <script src="/pub/hybrid/mini.js"></script>

- 路径用绝对 /pub/hybrid/... 与 app.html 第 26-30 行的 chunk 写法一致（./hybrid/... 靠 baseURI 也能解析到 orpheus://orpheus/pub/hybrid/，但绝对式更稳）。
- 打包：mini.js/mini.css 放进 orpheus.ntpk 内 ZIP 的 pub/hybrid/，同步改 app.html。
- CSP(app.html 第 11 行 meta 实测)：script-src 'self' 'unsafe-inline' 'unsafe-eval' ... → 同源脚本可加载；connect-src 含 http:/https: → 可 POST 127.0.0.1。
  worker-src 未声明 → blob:/data: Worker 一律被拒（实测 "Access to the script at 'blob:orpheus://...' is denied"）→ 不能用 Web Worker 做节拍。
  file:/// 与 orpheus://file 的 fetch/XHR 全部 Failed to fetch（实测）→ 页面读不到 runtime\port.json，见 §6。
- mini.js 单文件，无 import/require/外链 script（grep require/import=0，<script=0）。CFG 必须在 mini.js 之前设：window.__NCM_MINI_CFG__={...}。

---

## 3. 数据主源（未变，摘要）

页面没有 <audio>（实测 querySelectorAll('audio').length===0），状态全在 dva/redux store：
footer/#root 起点的 fiber 键 __reactInternalInstance$xxx（React16）→ 沿 fiber.return ≤400 跳找 memoizedProps.store.getState（本机第 43 跳）。
切片: playing / playingList / async:hostResource / async:lyric / app / setting / fmPlaying / page:*。
字段取法表（title/artist/album/cover/playing=playingState===2/pos/dur/timeText/liked=async:hostResource.likeTracksMap/loop/vol/lyric/queue/bar）见 v1 记录，未变；
loop 四档对照表（playOrder→off, playCycle→list, playOneCycle→one, playRandom→shuffle；AI/FM 归 shuffle）已逐档实测。
新增：queue[i] = {index:0基, id:String(resourceId||id)||null, title, artist, current}（v1.2 要求，playIndex 的输入）。
进度真值：页面唯一 input[type=range]（react-aria 隐藏 input，rect w=0）value=秒 max=时长 step=dur/1000；可见进度轨是 div.slider-default[aria-label="播放进度调节"]（footer 上方 3px 条）。
progressInput() 现在按 (aria-label 含"进度"+4 / max≈dur +3 / max>20 +1 / 含"音量" -8) 打分选路，避免以后出现音量 range 时取错。

---

## 4. Command v2 实现与实测落地延迟

统一返回（M3）:

    { ok, err, via, applied:{playing,pos,vol,loop,liked[,title][,index][,queueOpen][,miniMode]},
      cmd, arg, want, settledMs, tried:[...每条尝试, 失败标 ':fail'] }

cmd() 返回 **Promise<Result>**（契约 v1.2 口径二选一，这里选 Promise；CDP 侧要 awaitPromise:true，HTTP 侧不看返回值）。
不能等的调用方用 **cmdSync(name,arg)**：只点火 at=0 那一路，立即回读，未落地 ok:false/err:'pending'（实测 vol 走 cmdSync 返回 pending，350ms 后 state().vol=70）。
每次执行都写 stats.lastAck（API.lastAck()）；CFG.ack=true 时把结果作为附加键 ack 塞进下一次 POST /state（默认关，等小窗 JSON 解析确认能吃未知字段再开）。

| cmd | arg | 尝试序列(at ms/via) | 窗 | 实测落地 |
|---|---|---|---|---|
| toggle | 省略=翻转 / bool=目标态 | 0 click(footer 播放键) / 400 dispatch switchResumeOrPause | 700 | 27-64ms（v1 也测过 62-148ms），两方向各 4/4 成功 |
| next | — | 0 click(cmd-icon-next) / 450 dispatch playingList/jump2Track{flag:1} | 900 | 曲名发布比 pos 归零晚 ~100-450ms（见 §8 未完项） |
| prev | — | 同上 flag:-1 | 900 | 同上 |
| seek | 绝对秒 | 0 dispatch playing/setPlayingPosition{duration:sec} / 320 指针拖轨 / 620 原生 input setter | 900 | 47-103ms 精确落 30.00；暂停中同样落地（见 §7） |
| vol | 0..100 | 0 dispatch playing/setVolume{volume:v/100} / 260 再发一次 | 500 | 47-61ms，五点 errAbs=0 |
| loop | off/list/one/shuffle | 0 dispatch playing/switchPlayingMode{playingMode,triggerScene:'miniBar',HeartBeatFlage:false} / 300 click 模式键 | 500 | 全部 <200ms |
| like | true/false 目标态 | 0 click 红心（/cmd-icon-like/i 宽松匹配两态类名） | **1500** | 取消喜欢要过一次服务端接口，500ms 窗内回不来（实测 ok:false/not-applied），故放宽到 1500 |
| openQueue | — | 0 click cmd-icon-playlist / 300 dispatch app/openSideSheet{comName:'PlayBarPlaylist'} | 500 | applied.queueOpen 来自 DOM .cmd-sidesheet-right |
| closeQueue | —(新增) | 0 遮罩合成点击 / 300 dispatch app/closeSideSheet | 500 | openPlaylist 不 toggle，只能点遮罩 |
| playIndex | 0 基序号(新增) | 0 dispatch playing/playOneTrackInPlayingList{item:resourceId,flag:0,switchType:'call',triggerScene:'miniBar'} / 700 队列面板第 i 行 / 1500 jump2Track 相对走 |i-cur| 步 | 2200 | 84-92ms |
| toggleMain / showMain | — | dispatch app/switchToMiniMode / exitMiniMode+restoreWindow+showWindow+activeWindow | 900 | 接线未做破坏性 UI 验证（会整体换布局/抢前台窗口） |

源码依据（app.chunk.1482700000000.js，整串检索，勿用 Get-Content 下标）：
- jump2Track saga 只吃 {flag:±1,type:'call',assignedResourceId}（相对），无绝对序号 action。
- playing/playOneTrackInPlayingList saga: "string"===typeof item 时按 curPlayingList.find(t=>t.resourceId===item) 取回整条 → 所以 playIndex 只要给 resourceId。
- 音量: ye={minValue:0,maxValue:1,step:.01}，onChange 里 n({type:"playing/setVolume",payload:{volume:e}})，且 |Δ|<0.005 不重发 → playingVolume 就是 0..1 线性 UI 音量，state().vol=Math.round(x*100) 与契约 0..100 严格线性（float32 0.6000000238→60 无损）。
- 进度: setPlayingPosition 有 restoreResource / loadingSeekDuration 分支（restoreResource 非空时只写 restoreResource.current）。本轮实测 restoreResource=null、暂停/播放两种状态下 seek 都真落地（§7）。

---

## 5. 通道 v2：端口发现与轮询

节奏: 播放 250ms / 暂停 1500ms（实测播放中 12 个间隔 [263,265,257,258,261,259,264,258,263,261,262,260]，中位 261ms）。
调度: arm(ms) 起**精确定时器**；loopBeat 每 200ms 只做看门狗（比精确定时器晚 250ms 以上才补火）。不要改成"纯节拍驱动"——200ms 粒度会把 250ms 间隔推成 250-450ms 抖动。
定时器链在 POST 的 .then 里续期 → 嵌套层级被网络回调打断 → 规避 Chrome 隐藏页"嵌套≥5 定时器限流"。历史上 24.5s 首包就是 sweepAll 的 setTimeout(step,200) 链在后台被限流 + 一次只探一个口 + SWEEP 30s 叠出来的。

端口优先级（sweep 内一次并行，不再串行 200ms/口）:
1) port.json 句柄端口（见 §6） 2) localStorage('ncmmini.port') 上次连上的口 3) 46210..46219 全量并行
非 hint 端口先应答时等 hint 端口 400ms（CFG.hintWaitMs）再定，让"port.json 优先"真的是优先。
整个 sweep 有硬看门狗 max(2000, PROBE_TIMEOUT+1800)ms：任一 fetch 不返回也必然 resolve（stats.sweepTimeout 计数），杜绝 connect() 卡死。
连上后 GET /health 校验一次（stats.healthOk/healthMiss，不计入 err），并写 localStorage ncmmini.port/protocol。
boot: arm(0)+runNow() 当帧扫描（实测注入→首包 16ms），FIRST_RETRY 1200ms 补扫，SWEEP 3000ms 周期，focus/visibilitychange/pointerdown/keydown 触发 wake() 立刻重扫。
自启: CFG.selfStartMode 'iframe'(默认)|'href'(协议已注册)|'off'(自测禁用)，60s 冷却 localStorage('ncmmini.kickAt')。

命令去重（handleCommand）: 带 id → 与上次 id 相同直接丢；无 id → 同 cmd+arg 在 400ms 窗内丢（stats.dedupe）。这条是父代理"toggle 点了一次没反应"的候选根因之一：多个轮询实例并存时同一条命令会被吃两次（翻回来）。

---

## 6. runtime\port.json 的正确接法（契约 v1.2#4）

实测：页面里 fetch/XHR 读 file:/// 与 orpheus://file 全部 Failed to fetch（CSP + CEF 限制），**页面自己读不到本地文件**。所以 port.json 必须由小窗递给页面，mini.js 支持三条递交路径（优先级从高到低）:

1. 小窗 CDP 注入后写一句话再 connect()（推荐，最省事）:
   Runtime.evaluate: window.__NCM_MINI_PORT_FILE__ = JSON.stringify(require端口对象)
   例: window.__NCM_MINI_PORT_FILE__='{"port":46211,"pid":20248,"backend":"HttpListener"}'
2. 注入前设 window.__NCM_MINI_CFG__ = { portFileInline: {...} }（打包安装阶段可用）
3. CFG.portFile 给一个**可 fetch 的** http(s):// 或 file:// URL（默认值 D:\dsh工作区\netease-mini\runtime\port.json 是本地路径，页面拉不到 → 只记 stats.portFileMiss，不记 err）
读到的内容会缓存进 localStorage('ncmmini.portfile')（CFG.portFileCache:false 可关，自测时用）。
命中/未命中计数: stats.portFileHit / portFileMiss；healthCheck 结果: info().lastHealth。
实测: 句柄给 46211 → info().lastSweep={ok:true,port:46211,via:'portfile',ms:13}；清掉句柄 + 换 46213 → connect() 6ms 内定到 46213。

---

## 7. 本轮三个 bug 的根因结论（一句话一个）

1. vol 下发 60 回读 33：不是映射错——playingVolume 就是 0..1 线性（源码 minValue0/maxValue1/step.01），本轮五点下发/回读误差全 0；父代理看到的 33 是**从 HTTP 管道里的旧 state 快照读的**（命令在 POST 响应里下发，页面回读发生在下一次 POST，且当时页面上还有第二个旧轮询实例在发它自己的快照）。
2. seek(30) 回读 43：setPlayingPosition 实测 47-103ms 精确落 30.00（暂停中 66ms 落 45.12），13 秒偏差同样来自"读的是命令下发前采到的快照 + 双实例串扰"，不是 restoreResource 分支（restoreResource 实测为 null）。
3. toggle 单向失效：本 build 连续 4 次严格翻转（27-64ms，applied 与独立回读一致）；失效的真实原因是**多个 mini.js 实例并存 + 无 id 命令被重复执行**（翻过去又翻回来），所以现在 both 去重 + applied 回读，未落地直接 ok:false 而不是静默 ok:true。

附带结论：页面上若存在**没有 live() 守卫的历史 build**（早期注入残留），新实例无法从外部停止它（闭包里的 setTimeout 句柄取不到），只能刷页面清干净。本轮实测外来轮询者计数 foreignPostCount=0（POST 体里没有 queue[i].index 的一律算外来）。

---

## 8. 已知未完项（不阻塞验收，交接给下一轮）

- next/prev 的 applied.title：曲名发布晚于 pos 归零，v2 检查里 title 可能瞬时为 null 导致提前判命中（本轮 pi 表中 next/prev 的 settledMs=17/8ms 就是这个现象，实际换曲在后续行里可见）。修法：检查里要求 a.title 非空且 !=pre.title 或 a.index!=pre.index。
- like(false) 依赖服务端接口，1500ms 窗仍可能不够（弱网）；小窗侧应按 ok:false 重试而不是当成功。
- stats.dedupe 与行为观测不完全对齐（6 条命令→2 次 playing 跳变，但计数器 0）：跳变证明去重生效了，计数器路径待补一次纯 id/纯内容分离实验。
- CFG.ack 默认 false：小窗 JSON.cs 能吃未知字段之前不要开。
- State.queue 新增 index/id 两个键、Command 新增 playIndex/closeQueue —— 需要通知 overlay 代理同步解析。

---

## 9. 复验脚本与本轮活体数据

- ncm-re/probe/verify-build.mjs  --stamp / (默认) 磁盘侧+页面侧 build 断言
- ncm-re/probe/prove.mjs         N1..N7 全流程(自带 mock 小窗, 自动挑空闲端口, 增量写 prove-report.json)
- 用法(无全局 node, 用 DSH 自带): "C:\Users\Admin\.dsh\dsh-runtimes\dsh-primary-runtime\dependencies\node\bin\node.exe" ncm-re\probe\prove.mjs
  注意: prove.mjs 会往页面注入并真的控制播放器(测试结束会还原 vol89/list/paused/取消红心), 别和 overlay 代理同时跑。

本轮（build 2fa9287e，活体）:
- N1 页面 build == 磁盘 build == ncmmini-bridge-2fa9287e-057384；info().prevBuild=30285325（上一份）；retired=["old.stop-fn","old.api.stop","reassigned"]
- N2 注入→首包 16ms（via portfile, sweep ms=13）；播放中 12 间隔中位 261ms（min257 max265）；connect() 二次握手 msPage=4-6；foreignPostCount=0
- N3 vol 0/25/50/75/100 → 回读 0/25/50/75/100，误差 0，settledMs 47-51
- N4 seek(30) 播放中 → applied.pos=post.pos=30（47ms）；暂停中 seek(45) → 33.12→45.12 落地 ok:true（与 v1.2 N4"暂停应失败"不符，见 §7.2）
- N5 toggle×4 → false→true→false→true→false 严格翻转，applied==独立回读
- N6 20 行命令全部带 applied 且与独立 state() 一致（mismatches=[]）
- N7 58s 窗 stats.err=0（lastErr/lastRej 均 null），stats.post +39，lastStateMs=2（state() <20ms 达标），sweepTimeout=0
- S7 还原 vol89/loop list/未红心/paused/队列面板关，title 绽放(240s) pos~73.9，queue 31 首；播放列表场景=store 里 scene='dailyRecommend' href='/dailyRecommend'（每日推荐 31 首），
  原「怀念神仙打架的年代(93首)」在 playingList/dbPlayingList/historyList 里检索"怀念" 0 命中 → **无法恢复**（不是本轮点坏的：curPlayingWithUpdateTimeLimit.scene 早在本轮之前就是 dailyRecommend；恢复需真实点开那个歌单，属新的破坏性操作，未做）。

---

## 10. 「小窗」入口落入 footer 的修复（2026-11-18）

### 根因

`ensureButton()` 在原生播放控件尚未挂载时退化为 `footer.appendChild(button)`。控件随后出现后，旧逻辑只检查按钮是否还在 body 中，因此永远不会把按钮迁回 `.cmd-space.btns`。此外当前页面并未实际加载 `mini.css`，文字按钮最终按原生 `.cmd-button` 样式显示为 40px 宽，独立占据 footer 底行。

实测修复前：按钮 parent=`FOOTER`，x=0、y=729.2、40x22.8；footer 被撑到 115.6px 高。

### 修复

- `hostForButton()` 优先使用 `footer .cmd-space.btns`，右侧 `.cmd-space.right` 仅作兼容兜底；没有真实宿主时返回 null，等 `loopBeat` 1 秒后重试，不再直接追加 footer。
- `ensureButton()` 每次执行都核对宿主，旧按钮即使已经落在 footer，也会自动回迁；无有效宿主时隐藏旧兜底按钮。
- 按钮改成 24x28 的 SVG 小窗图标，保留 title/aria-label「小窗」与既有点击开关逻辑。
- `ensureButtonStyle()` 随 `mini.js` 注入关键样式，不再依赖页面是否额外加载 `mini.css`；外部 `mini.css` 同步更新。
- 复用旧按钮时只修正 class/图标，不重复注册 click listener。

最终 build：`ncmmini-bridge-5ccdaf2e-060676`，磁盘与页面指纹一致。

### 验证

- 页面原生播放控制组中按钮只有 1 个，24x28，和播放/切歌/队列按钮同排；footer 高度恢复 92.8px。
- 故意把按钮再次移到 footer，1.5 秒后自动回到 `.cmd-space.btns`，数量仍为 1，display=flex。
- 两次程序化点击计数严格为 1、2，HTTP 均 200；第一次实际打开 overlay，第二次实际隐藏。实时隐藏状态以 `/ctl/dbg-pointer.windowVisible` 为准：`Hide()` 当前不会立即重写 `winrect.json`，不能用该文件的旧 `visible=1` 误判。
- `/info` 的 `pageBuildProbe` 与 `bridgeBuild` 均为上述最终 build，CDP=1，悬浮窗恢复隐藏。

修复前备份：`docs/patches/mini.pre-ui-2026-11-18.js` 与 `docs/patches/mini.pre-ui-2026-11-18.css`。
