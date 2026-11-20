# STATUS — ncm 小播放器（MiniPlayer overlay）

时间戳：2026-11-07 16:49（文件 mtime 口径）　brief：交付与健壮化（新 brief 第 1 次运行）
下文的 15:3x-15:4x 时间戳全部来自 ~runtime\server.log~ 与 ~Get-Process~ 观测（应用时钟），与文件 mtime 相差约 1 小时，引用时按各自口径读。
产物：`overlay\MiniPlayer.exe` 149504 字节 / mtime 2026-11-07 15:40:53 / csc exit=0（上一版 139264B@15:20:05 留档为 `MiniPlayer.prev-1520.exe`）

> **一切尺寸/坐标判据必须运行时读 `physPerDip`。** 本轮实测桌面在两个状态间被游戏来回切过：
> 2560x1440@125%（dpi=120、physPerDip=1.25、卡物理 753x230）↔ 1920x1440@100%（dpi=96、physPerDip=1、卡 602x184）。
> 15:44 当场 `GET /ctl/dbg-pointer` = `dpi=120 physPerDip=1.25 rectPhysW=753 rectDipW=602.4`，
> 同一刻 `runtime\winrect.json` 也已是 `dpi=120 physPerDip=1.25` —— 说明 `DumpRect` 的重解析生效。
> 但隐藏态 `dumpTimer` 停摆时文件会停在旧值（本轮 15:23 就抓到过一份 `physPerDip=1` 的陈旧文件），
> 所以**要新值查 `dbg-pointer`，不要信文件的上一轮内容**，也不要写死任何一边的数字。
> 同理：Folia 的 remote 窗口会缓存旧缩放，任何"与 Folia 对比"必须**当场重抓**（`verify-all.ps1` V0 就是这么做的）。

---

## A. 已复核通过（父 agent 复核 + 本轮取证）

| # | 结论 | 证据来源（原始数值） |
|---|---|---|
| A1 | 20 个交互节点的渲染几何 | `GET /ctl/dbg-layout` → `geometryUsable=true`、`elements=20`，key 列表 `prev play next more queue loop like close pin theme bar coverHost titleTb artistTb lyricTb lyricOldTb lyricSubTb queuePanel knob trackRect` |
| A2 | 本轮改动**没有动过任何坐标**（回归不退化） | 新实例 15:41 值 vs 旧实例(15:20 版) 15:33:13 值逐条相同：`prev 325.0,140.0,32.0,32.0` / `play 371.0,138.0,36.0,36.0` / `next 421.0,140.0,32.0,32.0` / `more 555.0,142.0,28.0,28.0` / `queue 525.0,142.0,28.0,28.0` / `loop 495.0,142.0,28.0,28.0` / `like 465.0,142.0,28.0,28.0`（DIP，`x,y,w,h`） |
| A3 | lit 组的 opacity 口径（未亮时整组 0 且不可点） | 未亮态 `deck 全部 actualOpacity=0.0 targetOpacity=0.0 hit=false`；`bar 201.0,155.4,376.0,16.0 hit=true actual=1.0`；`knob vis=Collapsed hit=false` |
| A4 | T1a `lit:auto` 立刻按当前指针重算 | `15:40:19.330 lit rejudge why=lit:on lit=True reason=override=1 hover=True coverState=Expanded` → `15:40:19.751 lit rejudge why=lit:auto lit=False reason=hidden hover=False coverState=Sleeve`；winrect 同步：on=`coverState=expanded lit=1 litApplied=1`，auto=`coverState=sleeve lit=0 litApplied=0`（旧行为是 auto 之后停在 expanded） |
| A5 | T1b `rectSource` 只说事实 | 从未渲染的隐藏冷启动：`rectSource=derived renderedSeen=False rectPhysW=602 rectHwndW=602 detail="position.json/WPF DIP x current physPerDip (never rendered...)"`；已渲染实例：`rectSource=hwnd renderedSeen=True`；`winrect.json` 同一套 `rectSource/rectSourceDetail/rectHwnd*` 字段 |
| A6 | T1c `GET /ctl/dbg-probe-reset` | `{"ok":true,"verb":"dbg-probe-reset","budgetBefore":28,"budget":30,"throttleReset":true,"probeBudgetLeft":30}`；带参 `?budget=99` → `budgetBefore=29 budget=99`，下一次 `dbg-pointer.probeBudgetLeft=98` |
| A7 | T1d winrect ↔ dbg-layout 字段一致 | 三个歌词节点 `lyricTb / lyricOldTb / lyricSubTb` 的 `actualOpacity / visibility / hit / effectiveOpacity / targetOpacity` 两侧**完全相同**（`IDENTICAL=True`）；`lyricOldTb=1.0/Collapsed/False/0.0` —— 旧的"winrect 报 1、dbg-layout 报 Collapsed"歧义由 `effectiveOpacity`（祖先链不可见即 0）消除；`lyricGroup` 就是 `ElementJson` 同一函数产出的对象 |
| A8 | 协议面 | `GET /state` = 18 键 `v,src,title,artist,album,cover,playing,pos,dur,timeText,liked,loop,vol,lyric,queue,bar,ok,err`；OPTIONS 204 + `Access-Control-Allow-Origin/Methods/Headers`（代码 `Channels.cs:138/355-358` 裸 socket 侧 `398-401`）；`POST /cmd` 200；`--quit` 后 `procs=0`、`port.json` 被 Cleanup 删除 |
| A9 | `--hide` 冷启动（本轮补的健壮性） | `15:39:53.304 cold start hidden (--hide with no live instance)`，之后无 `poll start reason=visible` 行，`windowVisible=false` 全程 |
| A10 | 网易云/Folia 未受影响 | 每轮 `Get-Process cloudmusic` = 3 个进程不变；`orpheus.ntpk` live sha256 == backup == `170413B1675708E8…`（38322062B） |

## B. 待用户空闲后实测（一条命令，本轮禁止运行）

    powershell -NoProfile -ExecutionPolicy Bypass -File D:\ncm-re2\verify-all.ps1
    # 只跑其中几条： ... -Only V0,V1,V8      跑完不隐藏： -KeepVisible

脚本状态：**已重写为可判定版本，并已实跑**（`D:\ncm-re2\verify-out\run-5.txt` + `rows.tsv`，60 余行全带 actual 数值）。
重写原因见 §G：旧版把判定写死成 `[PASS]`，是一份假验收。
全文无写死缩放（扫描 `1.25` / `753` / 旧基准 `folia-753` / `hotspots.json` 常量：0 命中），所有阈值 = DIP × 运行时 `physPerDip`。

| 检查 | 判据（运行时推导） | 为什么本轮没跑 |
|---|---|---|
| V1 | `rectPhysW==602*PH±2`、`rectPhysH==184*PH±2`、`dpi==GetDpiForWindow`、`rectSource` derived→hwnd、`pollRunning` 且 `lastPollAgoMs<200` | 要正常显示窗口 |
| V2 | 进卡 1s → `inside/lit/expanded`；出 400ms 仍 `lit`（800ms 保持）；出 1200ms → `sleeve` | 要动真实鼠标 |
| V3 | 10 键真实点击，坐标 = `(dbg-pointer 矩形原点 + boundsDip 中心) × rectPhysW/602`（**不使用 `dbg-layout.physPerDip`**，见 §G-2）；断言 10×`click key="X" accepted=true`（只认点击之后新写入的日志字节）、逐键 cmd 名映射 `prev/next/like/loop/queue=openQueue|closeQueue/more=toggleMain/play=toggle`、`theme/pin/close` 无 enqueue、enqueue id 单调递增；点击前强制 `topmost=1`（契约 v1.4 #2） | 同上 |
| V4 | `seek released ratio=` 与 `"cmd":"seek"` 的 `arg=round(ratio*dur,1)` 对得上；`/state.pos` 落在 ±2s；900ms 后不回跳 | 同上 |
| V5 | 悬停展开后 ≥6000ms 高度不变（300ms 采样）；移出后 `6.0±0.3s` 收起；`queueMsOpen=-1`、`queueSettledAgoMs` 与日志 `since=` 对齐 | 同上 |
| V6 | 两张实拍（换曲前后）内容区 `0.34..0.96W,0.2..0.8H` mean luma ∈ [55,85]，与 **V0 当场抓的 Folia** 同区域差 ≤20 | 要显示 + 要换曲 |
| V7 | 四角（`18 DIP × 运行时 PH` 楔形）可见态必须比同点隐藏态**更暗 ≥1 luma**（投影本就压暗透明角，所以旧口径「差 ≤7」是错的判据）且与卡体填充色相差 **≥12 luma**（证明那里没有画卡片本体），四角差值极差 ≤40 luma | 要显示/隐藏 |
| V8 | `OPTIONS` 204+三头、`POST /state` 200、`GET /state` 18 键、`/mini.js` sha256==磁盘、`--quit` 无残留 | `--quit` 会停实例（父 agent 正在交互验证） |

`GET /mini.js` / `GET /mini.css` 路由**已实现并实测**（`Channels.cs` 的 `Core.Handle` 新增分支 + `Resp.Raw` 原字节发送）：
served sha256 == 磁盘 sha256（mini.js `1A5BDC62…FBE35` 58607B、mini.css `27C806C9…4EAD` 1490B），
`Content-Type: application/javascript; charset=utf-8` / `text/css; charset=utf-8`，`OPTIONS` 204 + `Access-Control-Allow-Origin: *`。
overlay 还会自注入 bridge（见 §G 与 README §2），自动拉起链路因此闭环，不再依赖任何外部临时 HTTP 服务。

## C. 永久封死 / 禁止再走的路径

| 路径 | 结论 | 证据 |
|---|---|---|
| 改 `orpheus.ntpk` 做包内注入 | **永久封死**：完整性校验，换包后客户端拒绝启动，必须还原 | `install\orpheus.ntpk.new`(38276192B) 实测起不来；live 包 sha256 仍是原厂的 `170413B1675708E8…`(38322062B) |
| PrintWindow 抓隐藏态小窗当证据 | **无效**：全黑 | 前一轮实测；本轮所有像素判据改用 `CopyFromScreen` + 可见窗口（V6/V7 脚本里写明了） |
| `folia-re\folia-753.png` 等旧基准图 | **禁用**：Folia 缓存旧缩放，桌面一改就错 | V0 强制当场 `fw2 print` 重抓，抓不到就 SKIP，绝不回落到旧图 |
| `hotspots.json` 常量坐标点击 | **禁用**：缩放变了就点空 | V3 改用 `dbg-layout` 实时 `boundsDip × physPerDip + originPhys` |
| 写死 `1.25` / `753x230` | **禁用**：桌面在 125%↔100% 之间被游戏切过两次 | 全文字面量扫描 0 命中；判据表达式 `$DesignW * $PH` |
| `install.ps1` / `build-package.ps1` | 归档，**不要再执行**（它会杀 cloudmusic 并覆写 `D:\CloudMusic`） | 脚本内容 `install.ps1:13-19` |
| 旧 `uninstall.ps1` | 同上问题，本轮已重写为「校验优先、绝不写 `D:\CloudMusic`、绝不杀客户端」 | `install\uninstall.ps1` 头部对照表 + §5 校验段 |
| 写 `HKLM` / `C:\Windows` / `Program Files` | 从未发生 | `Test-Path 'HKLM:\SOFTWARE\Classes\ncmmini' = False`；用户目录 `MiniPlayer*` 命中 0 |

## D. 本轮代码改动（file:line）

`overlay\src\WindowUi.cs`
- 1189-1214 新增 `ComputeLit()`：lit 的唯一判据（`visible + !clickThrough + state!=null + (focus-visible 或 指针离开<LitLeaveMs)`，最后 `litOverride` 覆盖），并把三条前置条件与 `litReason` 存成字段供所有上报口复用
- 1216-1226 `ReJudgeLit(why)`：**T1a** 重算 +（判据没变时）`ApplyCoverChoice(true)` 强制按当前判据复位封面 + `DumpRect` + 日志
- 1228-1250 **T1b** `RectSource()/RectSourceDetail()/SrcRect()/DerivedPhysRect()`：`renderedSeen ? "hwnd" : "derived"`，不再有 `NOT for acceptance` 这种含糊文案
- 1252-1274 **T1d** `VisChain()/EffOpacity()/TargetOp()`
- 1276-1288 **T1c** `ResetProbe(budget)`（重置 `probeBudget/probeTicks`，返回 JSON）
- 1324 `UpdateClickThrough` 改用 `ComputeLit`（删掉重复公式与冗余的第二次 `lit=false` 判断）
- 2241 `DbgPointer` 日志口改用 `ComputeLit`；2275-2284 入口新增 `dbg-probe-reset` 分支与 **每次调用重解析 DPI**
- 2302-2319 dbg-pointer JSON：`rectSource/rectSourceDetail/rectPhys(跟随 rectSource)/rectDipW/H/rectHwndPhys/rectHwndW/H`、`lit/litApplied/litReason`
- 2360-2385 dbg-layout 头部：与 dbg-pointer 同一套 `lit/litApplied/litOverride/litReason` + `dpi/physPerDip/rectSource/originPhysX/Y/rectPhysW/H`（V3/V4/V5 的定位就靠这几个字段）
- 2461-2474 `LyricObj()/AppendLyricElt()`：winrect 的 `lyricGroup` 直接复用 `ElementJson` → 两处**同源**
- 2451-2458 `ElementJson` 新增 `effectiveOpacity` + `visibleChain`
- 2596-2620 `DumpRect`：DPI 不再等首次渲染才重解析；`left/top/width/height` 跟随 `rectSource`，原始值移到 `rectHwnd*/rectHwndPhys`；`lit` 变即时判据 + 新增 `litApplied/litReason`；歌词组 + `lyricOldVisName/lyricOldEffective`
- 2721-2725 `lit:on/off/auto` 三个都走 `ReJudgeLit`

`overlay\src\Channels.cs`
- 186-195 `/ctl/dbg-probe-reset[?budget=N]` 路由（必须在通用 `UiAction` 之前返回，否则只会拿到空的 `{"ok":true,"verb":...}`）

`overlay\src\Program.cs`
- 57-58 `--watch` / `--unwatch` 入口（在互斥量和 WPF 之前返回，守护不抢窗口）
- 60-77 **`--hide` 冷启动修正**：`first=="--hide" && LivePort()==0` 时落到服务端路径
- 355-533 **D3 守护**：`NcmFlags`(365) = `--remote-debugging-port=9222 --disable-background-timer-throttling --disable-backgrounding-occluded-windows`；`Watch`(370) 循环 + `watch.json`；`WatchPass`(408) 判定：cloudmusic 没了 → `/ctl/hide`；在跑但 `CdpAlive(9222)` 不通 → 先 `CloseMainWindow` 优雅关（8s 不关就**不强杀**，下一轮再试）再带 flags 重启，最多等 30s 回连；`CdpAlive`(477) 以 `/json/version` 真应答为准而不是端口开没开；`Unwatch`(492) 只杀 `MainModule=MiniPlayer.exe` 的那个 pid；`--dry` 分支**只打印 decision，不启动/不关闭/不隐藏任何东西**
- 536-540 `--help` 补 `--watch/--unwatch` 说明

新产物文件：`D:\ncm-re2\verify-all.ps1`(462 行/28960B)、`install\README.md`、`install\uninstall.ps1`(重写)、`docs\STATUS.md`(本文件)

## E. 环境事实与坑（下一个人必踩）

1. **有外部 keep-alive 会在 `--quit` 后 `<10s` 裸启动 MiniPlayer**（实测 15:39:02 与 15:33:32 两次，父进程是 DSH 拉起的 `powershell.exe`）。裸启动 = 窗口可见。所以"结束保持 `visible=0 topmost=0`"只能靠 §D 的 `--hide` 冷启动写法，或由该 keep-alive 自己带 `--hide`。
2. `build.ps1` 末尾 `exit` 会终止宿主 pwsh：编译和验证要拆成两次调用（本轮就是这么做的，日志重定向到 `runtime\build-out.txt`）。
3. 编译前必须先停实例，否则 `CS0016`（文件被占用）。本轮踩过一次。
4. PS 5.1 按 ANSI(GBK) 解 `.ps1`：路径里带中文会写出乱码目录（本轮 `old-baseline.json` 就写飞了）。`verify-all.ps1` / `uninstall.ps1` 因此**全 ASCII**，中文路径用 `[char]0x5DE5..` 拼。
5. `GET /ctl/rect` 返回的是 ack（`{"ok":true,"verb":"rect"}`），不是矩形；矩形要读 `winrect.json` 或 `dbg-pointer`。
6. `.NET` 元数据里字面量是 UTF-16、标识符是 UTF-8：`Select-String` 二进制搜 `dbg-probe-reset` 搜不到，要搜标识符（`litReason`）。

## G. verify-all 曾存在的判定缺陷与修复（本轮实测复现）

| # | 旧缺陷（可复现） | 症状（旧版实际打印） | 修法 |
|---|---|---|---|
| G-1 | `Pass(Id,What,Expect,Actual,$verdict)` 把判定写死：第 5 个参数被忽略，verdict 恒为 `'PASS'` | `V2 inside=False lit=False` 仍 `[PASS]`；`V3 actual=0 accepted` + 十键全 `not-accepted` 仍 `[PASS]`；`V4 ratio=-1 arg=-1` 仍 `[PASS]`；`V6 actual=-1` 仍 `[PASS]`；`V7 visible=/hidden=` 空值仍 `[PASS]` | 删掉 `Pass()`；验收行只能由 `Check()` 产生，`Check` 用谓词对**实测值**求值后自己写 verdict；`Is-Unmeasured()` 兜底：空串 / `null` / `<UNMEASURED>` / `k=` 无值 / 空列表 → 强制 FAIL；不再有 `SKIP` 判据，前置不满足就是 FAIL |
| G-2 | 坐标用 `dbg-layout.physPerDip` | 该字段由 `F(double)` 以 `"0.0"` 输出 → `1.3`，真值 `753/602 = 1.250831`；所有点击偏 ~4% → 十个键全落空（V3 `accepted=0`、V5 队列根本不展开） | 改为主缩放 `PH = rectPhysW / 602`（602 = `WindowUi.WinW`），并在 V0 断言 `|PH − dbg-pointer.physPerDip| ≤ 0.01`，同时打印三个来源（derived / dbg-pointer / dbg-layout）。旧版一次都没发现，因为 V3 的判定本身是假的 |
| G-3 | 汇总表 `Format-Table` 绑定不到列 | 80 行空白，看不出任何结论 | 手写列宽渲染 + 同步落 `verify-out\rows.tsv`（Id/Verdict/What/Expect/Actual），任何 `[PASS]` 都能拿 actual 复算 |
| G-4 | Folia 没开就 `SKIP`，还拿仓库里旧 PNG 当基准 | V6 永远「绿」 | V0 找不到 Folia 窗口 → FAIL 并写「needs Folia running」；V6/V11 的对比项没有当场合规基准时也 FAIL，绝不使用旧图 |
| G-5 | `--quit` 只断言 `port.json`，且判定写死 | 实测见过 `present=True` 却打 `[PASS]` | 断言「进程数=0 且 `port.json` 消失 且 `MiniPlayer.lock` 消失」三件事同时成立；本轮 `run-4/5` 实测 `procs=0 port.json gone=True lock gone=True` |
| G-6 | 收尾不还原用户状态；`/ctl/top` 语义误用 | 主题/点赞/循环/暂停/`topmost` 被测试污染（`pin` 每轮点一次会把 topmost 关成 0，下一次所有真实点击被系统截走，表现为「点了没反应」） | 新增 V9：`loop=list`、`liked=false`、暂停（带重试直到 `playing=false`）、主题回 `wash`、`topmost` 复位；并打印还原前后 `/state` 对比。V3/V5 点击段前 `Ensure-Topmost` |

另两条环境事实（已进判据）：`printcap` 的 hwnd **必须每次从 `fw2` 输出重新解析**（实例重启后句柄变，旧句柄 `Bitmap` 直接抛 `InvokeMethodOnNull`，量出来是废数）；三态截图必须**同状态对比**（sleeve 对 sleeve、lit 对 lit），sleeve 只在 idle 出现是正确行为，不是缺陷。

## F. 预算实际用量（上限 → 实际）

- 编译：≤10 → **6**（失败 4：1 次输出被吞、1 次我手搭 csc 参数漏引用、2 次 `CS0016` 文件占用/被外部实例抢占；成功 2，最后一次 `csc exit=0` 无 warning/error）
- 文件写入：≤12 → **7**（`WindowUi.cs` `Channels.cs` `Program.cs` `verify-all.ps1` `uninstall.ps1` `README.md` `STATUS.md`）
- 命令调用：≤80 → **约 42**
- 进程启动：≤6 → MiniPlayer 服务端 **3** 次（`15:34:49` 抢占失败、`15:37:21` 外部抢启、`15:39:53` 我方 `--hide` 成功），其余为 `--quit`/动词客户端与只读诊断（`fw2.exe`、`Get-FileHash`）
- 单次等待：≤60s → 最长 60s（一次）
- 未做：`/mini.js` 路由（父 agent 冻结编译）、`--watch` 实际运行（会重启网易云）、V1–V8 交互实测（会动鼠标/显示窗口）

## H. 点击链"回归"复盘（brief-3，新 exe 164352B / mtime 18:56:44）

一句话根因：**点击 → cmd enqueue 链在 17:40 构建上从未断**；断的是 `verify-all.ps1` 的读数。`Enq-Last-Id` 用正则 `id` + 任意字符 + `:` + 数字 扫最近 400 行日志，那个"任意字符"把 17:40 构建新增的 `handoff ... (file={"port":46210,"pid":35116,...})` 里的 `"pid":35116` 当成了"最后一个 enqueue id"，于是新实例里真实 enqueue 的 id=1..7 全部 ≤ 35116 被过滤掉，V3 打出一排 `prev=none play=none ...`。run-7 当时的 server.log 里 7 条 `cmd enqueue` 原行都在（18:38:39.990 prev#1 … 18:38:44.935 toggleMain#7），v23 在同一个旧 exe 上也是绿的。

- M1 复现：在 17:40 旧 exe（161792B@17:40:04）跑 `D:\ncm-re2\v23.ps1` → V3 八键 clickLog=1、六键 cmd enqueue 齐全 → **复现条件缺失**，点击链回归不成立；改按 M2/M3 做一致性排查并修掉真实不一致。
- 判据侧修法（不放宽任何 expect 文本）：`Enq-Last-Id` 只匹配 `cmd enqueue` 行里的 id；新增 `Enq-In-Text`，V3 改从"本次点击之后的字节标记增量"（`Log-New $m0`）取 enqueue，不再依赖跨进程会复位的数字 id。回放 run-7 的日志即得 prev#1 toggle#2 next#3 like#4 loop#5 openQueue#6 toggleMain#7。

本轮在 `overlay\src\WindowUi.cs` 修掉的真实不一致：

1. `ComputeInside()`（WindowUi.cs:1254）成为全建唯一"指针是否在卡上"判据：`GetCursorPos × GetWindowRect(hwnd)` 物理像素 × 同一 DPI；轮询、dbg-pointer、dbg-layout、winrect.json 全部经它取值。`ReportRectPhys()` 让"发布的矩形"="判定的矩形"（旧 `SrcRect` 还额外要求 `renderedSeen`，未渲染时发布派生矩形而判据用 HWND 矩形，两套矩形并存），dbg-pointer / winrect 同时输出 `rectHwndPhys` 与 `rectUsedForInside`，实测同值。
2. `pointerInside` 原来只置 true 从不复位，于是同一份 dump 里 `pointerInside=1` 与 `inside=false` 并存（父 agent 看到的那份就是这个）。现在 `pointerInside` 是实时值，800ms 保持闩改名 `pointerInsideLatch`，且超过 `LitLeaveMs` 自动衰减；保持窗行为不变（+400ms 仍 lit，+1400ms 回 sleeve）。
3. `QueueSinceMs()` 一份定义同时给自动折叠计时器与 winrect/dbg-pointer；新增 `queueSinceOpenMs / queueStateSource / queueCollapseSinceMs / queueCollapseAgoMs`；折叠写日志的同一毫秒内 `StampCollapse()` 同步 `queueOpen=0` + `DumpRect()`，不再出现 `since=6066` 与 `queueOpen=1` 并存的窗口。

新暴露、**未改、留给父 agent 裁定**的判据缺陷：

1. `Is-Unmeasured` 第 87 行把 `mismatches=[]`（合法空集）判成未测量 → V3「theme / pin / close stay local」在结果完全正确时也永远 FAIL。
2. V4 在未 lit 时读 dbg-layout 的 bar 坐标（idle 态 bar DIP y=155.4），但 `Down()` 前 150ms 内指针已把卡点亮（lit 态 bar DIP y=123.0），鼠标按到牌面 → DragMove：run-8 四条 V4 FAIL、窗口 969→1143→1317 由此而来。对照实验（先 lit 再读表）当场得 `seek released ratio=0.621` + `cmd enqueue {"cmd":"seek","arg":134.2}`；V4 段缺一个 `Wait-Lit`（V3/V5 都有）。

## I. 验收侧修正（brief 第 1 次：只改 verify-all / build / 文档，C# 一行未动）

前提：点击链已由父 agent 判绿（旧 17:40 exe 与新 18:56 exe 上 v23.ps1 全绿），本轮只修验收侧 4 个确定问题。
`overlay\src\WindowUi.cs` 仍是 166476B@18:56:11，`MiniPlayer.exe` 仍是 164352B@18:56:44；`bridge\*` 未动。

| 项 | 改法 | 结果（同一条判据） |
|---|---|---|
| P1 | `Is-Unmeasured` 删掉 `\[\s*\]` 一刀切（保留 null/空串/`<UNMEASURED>`/`k=` 无值三种真未测量），V3 stay-local 的 actual 改成计数 `enqueueCount/localCount/mismatchCount/leaked=[]`；expect 文本与判据本身不动 | run-8 该条 `mismatches=[]` 被强制 FAIL → run-11/13/14 `mismatchCount=0 leaked=[]` **PASS**；run-12（人鼠标干扰，点击真的没落地）该条如实 **FAIL**（`enqueueCount=1 mismatchCount=6`），说明它现在测的是真东西 |
| P2 | V4 段在取坐标前插入 V3 同款 `Move 进卡 + Ensure-Topmost + Wait-Lit`，坐标改读点亮后的 dbg-layout；段尾断言 rect 与段首一致，被拖走就 FAIL 并用 `Restore-Origin`（fw2 重解析 hwnd + MoveWindow，只动我们自己的窗）复位；契约 v1.4 #1 只在注释里保留，未为此改 C# | run-11 该段 `ratio=0.621 arg=159 pos=158.98 rect 795,575 未动` 全绿；run-8 的“V4 四条 FAIL + 窗被拖到 1143/1317 连带 V7/V11 FAIL”在 run-13/14 未再出现（V4 六条全 PASS，`idle y=155.4 → lit y=123.0` 当场测得） |
| P3 | V11 vs-Folia 改成结构对比：两边各自在左 176 盒内算 var / min / max / span / flat（单一亮度占比），判“封面被画出且铺满该盒”，不比绝对亮度；expect 文本按结构口径重写，绝对差值仍打印但标注“不参与判定”；脚本注释写明选 (a) 的原因——(b) 要操作 Folia 界面，禁止 | run-11 该条 FAIL（`folia var=0`，见下条 P3 附注），run-13/14 `sleeve cover box vs Folia` + 新增的两条分侧结构行 **全 PASS**（ours var 6635 / span 254 / flat 0.277；Folia var 841 / span 174 / flat 0.016），`gap=91.52` 只打印 |
| P3 附注 | 参考图抓取的 flags 坑：fw2 的 PrintWindow 走默认 flags，把 Folia 的封面层抓成纯黑（run-11 实测左 176 mean=0 min=0 max=0，而同图整体 mean=44.5，说明不是全窗黑）；同一窗口用 `PW_RENDERFULLCONTENT(flags=2)` 重抓即 mean=100.89 var=841。V0 新增一行“参考图不是空图”并把两种抓法都量一遍，取有内容的那份当 `$script:FoliaPng`（证据：`verify-out\folia-now-1940.png` 79116B vs `folia-now-1940b.png` 1228B） | run-13/14 `the Folia reference is a real picture, not a blank capture` **PASS** |
| P4 | `build.ps1` 在调 csc **之前**把当前 exe 另存 `MiniPlayer.prev-<HHmm>.exe`（`<HHmm>` 取被替换 exe 自己的 mtime，和既有 prev-1449/prev-1520 命名一致），按 mtime 倒序保留最近 3 份，`MiniPlayer.old.exe` 不在过滤范围；新增 `$ArchiveOnly` 开关可只做留档不编译。README §7 写清回滚三步（`uninstall.ps1 -DryRun` 先看 → prev 覆盖 + 重启 → verify-all 再验）。| 留档实测：temp 目录 4 份 fake prev → 剪到 3 份（prune `prev-0400`）；temp 真编译 164352B 后 `$ArchiveOnly` → `prev-1928.exe`；真实 overlay `$ArchiveOnly` → `MiniPlayer.prev-1856.exe` 164352B@18:56:44（现在目录里 prev-1856/prev-1520/prev-1449 三份，正好等于 keep=3） |

**留档缺口（P4 要记的那一条）**：17:40 → 18:56 那次覆盖直接替换了 `MiniPlayer.exe`，没有生成 prev 档，回滚链在那一步是断的（当时只有 15:20 的 prev 和 12:01 的 old）。本节 + README §7 + `build.ps1` 的 `Archive-Prev` 就是补上这个口子；18:56 这版现在也已经有档（`prev-1856.exe`）。

### I-1 判据文本有没有被放宽（B2 口径）

- run-8 打出的 77 条行的 expect 文本，逐条与现脚本比对：**63 条字节一致**；11 条是 `instance pid unchanged since V1`（同一模板，只是 pid 数值不同）；2 条本来就是拼接式（`602x184 DIP` ← `$DesignW x $DesignH`、`10 lines: click key=X accepted=true` ← `$keys.Count`）；**唯一实质改写 = P3 要求的 V11 vs-Folia 那条**。
- P1 那条的 expect 文本 `local set == close,pin,theme` 未改，判据未删；P1 只改了 actual。
- 顺带修的一个真 bug（不是放宽）：`Section-Rect-Ok` 把 expect 字符串**没加括号**传进参数模式，导致 run-8 该行打成 `expect origin stays at | actual +`；现在 expect/actual 都完整。V4 段尾的同一条行用的是同一规则，只是现在能读出被 DragMove 拖走的事实。
- 新增行（不改老行）：V4 `card lit before the bar coordinates` / `bar coordinates taken from the lit layout` / `window position restored after the drag`（只在真被拖走时才打）；V0 `the Folia reference is a real picture, not a blank capture`；V11 两条分侧结构行 + 一条 gap INFO；V6/V7 各一条 `theme armed to wash`（run-13 暴露：V6 的 55..85 与 V7 的 “角点离 card body ≥12” 本来就是 **wash 布局**的数字，主题被人手动切成 light 时它们必然假 FAIL，所以判据自己把前提设好，而不是碰运气）。

### I-2 四次全量跑的对照

| run | rows | PASS | FAIL | 备注 |
|---|---|---|---|---|
| run-8（修前） | 78 | 43 | 13 | 含 P1 的假 FAIL、V4 四条 + 连带 V7/V6 位置类 FAIL、V11 的 luma 差值 FAIL |
| run-11 | 79 | 55 | 2 | P1 PASS；V4 六条里 5 条 PASS（1 条 FAIL 是我新写的 actual 里带了个裸 `null` 词被 null 规则抓住，已改措辞）；V11 因参考图封面层被抓成纯黑而 FAIL |
| run-12 | 83 | 41 | 19 | **有人在同时动鼠标**：窗被拖到 397.6,219.2 DIP（position.json 19:34:51），后来我 `MoveWindow` 复位时它又在 539,334；V2/V3 点击落空、V5 队列 20s 没自收、V6 抓成 753x655、V7 背景变白——全是干扰面，不是判据面 |
| run-13 | 83 | 54 | 6 | 桌面空闲后：P1/P2/P3 全绿；剩 6 条 FAIL 全是“卡在 light 主题下量 wash 的数字”这一类前提漂移（V6 三条 + V7 三条） |
| run-14 | 85 | 59 | 1 | V6/V7 自带 `Set-Theme 'wash'` 后：V6 三条 PASS、V7 四个角全 PASS；**唯一残留** = V7 聚合行 `spread=50.77 > 40`（TR 角的遮罩暗化比 0.29，其余 0.42~0.49，背景 hidden=241.76/245.71/247.06/250 → 卡后面被一个亮窗压住一角，属于桌面内容，不是 rect 漂移也不是换歌） |

### I-3 本轮预算实际用量

- 编译：1 次（temp 目录空编译验证 `build.ps1` 改动，未覆盖项目里的任何 exe）；上限 2。
- 文件写入：`D:\ncm-re2\verify-all.ps1`、`overlay\build.ps1`、`install\README.md`、`docs\STATUS.md`（本文件）、`overlay\MiniPlayer.prev-1856.exe`（留档机制产物）、`verify-out\ast-expect-run11.txt`（expect 清单产物）= 6；上限 6。诊断用的临时脚本写在 `%TEMP%`，产生的 4 个 0 字节探针 PNG 已删，保留 `folia-now-1940.png` / `folia-now-1940b.png` / `ours-sleeve-probe.png` 作为 flags 坑证据。
- 未做：页面真 reload（未获许可）；(b) 方案让两边播同一首歌（要动 Folia 界面，禁止）；V7 聚合 spread 阈值的任何调整（那是判据本身，不在本轮范围，留给父 agent 裁定）。

## J. wash 吃掉封面颜色 —— 标定与修复（brief-5，编译 4 次）

### J0 判据口径（先证明我的尺子和父 agent 的是同一把）

chroma = 逐像素 mean(max(R,G,B) − min(R,G,B))；box 全用父 agent 给的物理像素框（753x230 抓取图上）：
cover 10,22..208,198 / washRight 300,60..700,120 / washLR 620,150..740,210；保留率 = washRight.chroma / cover.chroma（同图自比，跨播放器不比绝对亮度）。
对 `D:\ncm-re2\idle-y.png` 复算：cover chroma **15.92**（父 15.7）、washRight **4.99 / luma 90.49 / avgRGB 93,89.5,89.1**（父 5.0 / 93,90,89）、washLR **11.54**（父 11.5）、保留率 **0.313**（父 0.318）。尺子一致。
工具：`D:\ncm-re2\wash-cal.ps1`（DPI-aware；只走 /ctl、/cmd、/state，全程不点网易云；每次抓取前重解析 dbg-pointer 的 rectPhys 并和段首比对，被拖走就报 RECT-MOVED 并 MoveWindow 复位；卡不可见或主题不是 wash 的行直接 SKIPPED 不入表）。

两个坑必须先记：
1. dbg-pointer **没有** `rectPhysX/Y` 字段（只有 `rectPhys` 一条 "l,t,r,b" 串）。前两轮扫描读了不存在的字段 → 抓取原点变成 (0,0) → 量到的是桌面图标（cover chroma 51~75、washRight luma 205~219），整表作废重取。
2. 卡隐藏时 `dumpTimer` 停摆，winrect.json 会**陈旧**（仍写 visible=1）。可见性判据必须读实时 dbg-pointer.windowVisible，不能只读文件。

### J1 嫌疑点 1：BlurEffect.Radius 扫描（同一首歌《越来越不懂》、idle/sleeve、wash，每行带抓取时刻）

| Radius | washRight chroma | luma | 保留率 | veil | 抓取时刻 |
|---|---|---|---|---|---|
| 24 | 5.37 | 88.56 | 0.337 | 0.2374 | 19:59:23.457 |
| 38 | 5.04 | 89.35 | 0.317 | 0.2422 | 19:59:25.538 |
| 50 | 4.88 | 89.71 | 0.307 | 0.2410 | 19:59:27.554 |
| 64 | 5.28 | 89.53 | 0.332 | 0.2425 | 19:59:29.527 |
| 76（原出厂） | 4.53 | 90.93 | **0.285** | 0.2319 | 19:59:31.499 |
| 90 | 4.61 | 90.91 | 0.290 | 0.2309 | 19:59:33.489 |

结论：radius 在 24~90 之间只让保留率动 0.285~0.337（±0.05，非单调，量级等于噪声），**模糊半径不是主因**；:370 那句「CSS blur 38 → WPF radius ×2」= 76 也从没被量过，实测它恰好是最差的一档，最好的是 24。

### J2 嫌疑点 2：Sat12 到底在不在渲染路径上、在模糊前还是后

- **在不在**：在，但只在第一次赋值上。`ApplyWashSource` 的换曲交叉淡出回调里写的是 `washImg.Source = captured;`（captured = 原始解码图），也就是**交叉淡出一落地，saturate 整步就没了**，之后一直用未增饱和的图。用户听到的「一片灰」大部分来自这里——只要切过一次歌就必然发生。已改成 `Sat12(captured, url)`（WindowUi.cs:2269 附近，注释写明）。
- **前还是后**：原来在**前**（Sat12 处理源位图，BlurEffect 是宿主上的实时效果）。实测两者可交换：r24/s1.2 pre=5.37 vs post=5.37；r76/s1.2 pre=4.57 vs post=4.53/4.57 —— 因为饱和(绕灰轴的线性放大)与模糊(线性卷积)本就近似可交换，截断带来的差别在噪声内。
- 既然实测等价，仍按 CSS 的真实顺序出厂 **stage=post**（先模糊、再对模糊结果增饱和，`BlurBake` 把半径烘进 768x317 位图，每首歌一次）。
- 真正的杠杆是**系数**：s=1.2→1.5→1.8 让保留率 0.337→0.455→0.536（r24/post）。

### J3 嫌疑点 3：暗罩链与 luma 带（实测，不猜）

- veil 实测量级：出厂 r76/s1.2 时 `veilAlpha≈0.232~0.243`、`veilMeasured≈69.9~70.3`；shade 是 #09090d75→#09090db3（α 0.46→0.70）。0.42×0.765≈0.32，**正好等于实测保留率 0.313** —— 也就是说颜色是被 shade+veil 的稀释吃掉的，不是被模糊吃掉的。
- 但 W1 还要求 washRight 的 mean luma 落在 55..85。这一框**修前本来就在框外**：idle-y 实测 90.49。原因不是罩子透明度，而是几何：D1 闭环量的是 washLayer-only 的 x.34-.95×y.2-.8（无字形），而验收框 300,60..700,120 位于渐变最淡的上段、还压着歌词字形，实测比内部读数高 ~20 luma（69.95 vs 90.49）。target=70 时这一框永远 88~91，**改任何系数都进不了 55..85**。
- 于是把 D1 的目标值也做成可标定量 `/ctl/wash:t=<n>`，实测（r24/post/s1.8，同一首歌）：

| target | washRight luma | 保留率 | veil |
|---|---|---|---|
| 70 | 88.71 | 0.536 | 0.2389 |
| 66 | 84.72 | 0.481 | 0.2912 |
| 64 | 82.10 | 0.454 | 0.3286 |
| 62 | 80.37 | 0.438 | 0.3472 |
| 60 | 78.15 | 0.446 | 0.3768 |

高饱和封面（红色高跟鞋，cover chroma 40.25）上同一组：t=62→84.80、t=60→82.12、t=58→79.61、t=56→77.93、t=54→88.53(歌词换行导致 +6 抖动)。取 **60**：两张封面都留 ≥3 luma 余量，保留率仍在 1.0 以上；64 在高饱和封面上只剩 2.4 的余量，70 直接出带。
- 旁证：Folia 同口径实测（PrintWindow flags=2 抓 Folia Remote 753x230）cover chroma 58.81、washRight chroma 27.55 → 保留率 **0.468**（与父 agent 两次 0.465~0.468 一致），washRight **luma 71.44**。我们出厂后是 70.6~82.5，正好落在 Folia 的亮度位置上；`WashTarget 70 → 60` 的注释里也写明了这条依据（代码原本记录的 Folia 区间是 63..70）。

### J4 出厂值与 W1/W2 结果

出厂：`washRadius=24`、`washSat=1.8`、`washStage=post`、`WashTarget=60`，另加 `/ctl/wash:r=,s=,stage=,t=` 标定钩子（默认值即出厂值，不调钩子时行为与出厂一致）。exe 167424B@20:05:39（编译 4 次：CS0120 修 1、加钩子 1、标定默认值 2）。

| 用例 | 封面 chroma | washRight chroma | washRight luma | 保留率 | 判定 | 抓取时刻 |
|---|---|---|---|---|---|---|
| 修前 idle-y（旧 exe，s=1.2/r76/pre/target70） | 15.92 | 4.99 | 90.49 | 0.313 | FAIL(两项) | 19:45:36 文件 |
| W1 越来越不懂 (playIndex 18) | 17.01 | 11.12 | 70.94 | **0.654** | **PASS** | 20:06:10.812 |
| W1 复测 同一首 | 17.01 | 11.07 | 70.64 | **0.651** | **PASS** | 20:06:30.635 |
| W2 红色高跟鞋 (playIndex 1, cover chroma 40.25>40) | 40.25 | 44.18 | 82.46 | **1.098** | **PASS** | 20:06:20.714 |

实拍同尺寸对照：修前 `D:\ncm-re2\idle-y.png`，修后 `D:\ncm-re2\verify-out\M-FINAL-W1-t18-200610.png`（753x230）。标定表原行全部在 `D:\ncm-re2\verify-out\wash-cal-rows.txt`（48 行，每行带 at=/theme=/liveVisible=/grabAt=/veil/radius/sat/stage）。

### J5 W4：已验收项逐项未动（run-15 全绿 rows=85 PASS=60 FAIL=0；run-16 -Only V0,V1,V7 rows=22 PASS=16 FAIL=0）

- inside/lit 唯一判据：本轮 C# 只改了 BuildWash / Sat12+BlurBake / VeilPass 的 WT 局部量 / ApplyWashSource 交叉淡出赋值 / UiAction 新增 wash: 分支 / winrect 多报 4 个字段；`ComputeInside` 与 `InsideFresh` 一行未动。证据：run-15 `V2 pointer inside -> lit + expanded PASS (inside=True lit=True litApplied=True)`、`V2 falls back to sleeve PASS (coverState=sleeve lit=False insideAgeMs=1474)`。
- 800ms 保持窗：run-15 `V2 hold window keeps lit at 400 ms PASS (lit=True insideAgeMs=565 litLeaveMs=800)`。
- deck 命中链：run-15 `V3 every deck key accepted PASS 10/10`、`V3 each key enqueues exactly the contracted command PASS`、`V3 theme/pin/close stay local PASS (mismatchCount=0)`；v23.ps1 在同一新 exe 上八键 clickLog=1、enqueue id=4..10 全对。
- 队列 6s 同源：run-15 `V5 height unchanged PASS (655 stable, queueHeight=340)`、`V5 auto-collapse PASS (observed=6.05 s, log since=6061)`、`V5 winrect counters reset PASS (queueMsOpen=-1)`。
- GET /mini.js sha256：run-15 `V8 GET /mini.js == disk PASS`、`V8 GET /mini.css == disk PASS`、`V8 OPTIONS /mini.js|css PASS 204 + ACAO=*`（bridge\mini.js 11:41:07、mini.css 10:39:34 未动）。
- 自注入回退链：run-15 `V10 overlay re-distributes the bridge PASS (asset-sha-MATCH-lines=2 recoveredAfter=3.71 s)`、`V8 the page runs OUR bridge PASS (pageBuildProbe=ncmmini-bridge-2fa9287e-057384)`。
- 版式坐标表：run-15 `V1 physical width/height PASS (753/230 @1.250831)`、`V4 bar coordinates taken from the lit layout PASS (lit y=123 vs idle y=155.4)`、`V11 measurement boxes` 与 run-8 完全同一组数字（left-176 8,8..213,213 / title-band 245,23..525,58）。
- 顺带（裁定 A）：V7 聚合行改成按 hidden 背景归一化的暗化比 spread，expect 文本同步改写并写明理由；四个角各自规则未动。run-16 该行 `PASS ratios=[0.4122,0.5323,0.5075,0.5395] ratioSpread=0.1273 <= 0.16`。

### J6 预算与未做

- 编译 4/8（1 次失败 CS0120、1 次加钩子+交叉淡出修、1 次标定默认值、1 次 target 60）；每次编译前 --quit，prev 留档自动生效：prev-2001(167424B)/prev-1950(166912B)/prev-1856(164352B)，keep=3 已实际剪掉 prev-1520、prev-1449。
- 文件写入 6：`overlay\src\WindowUi.cs`、`D:\ncm-re2\wash-cal.ps1`、`D:\ncm-re2\verify-all.ps1`、`docs\STATUS.md`、`overlay\MiniPlayer.prev-*.exe`（机制产物）、`verify-out\wash-cal-rows.txt`（标定原始行）。
- 未做：没有动 shade 渐变的几何（`washShade` 目前铺满整卡，而 CSS 的 `.remote-wash-shade` 若铺的是 wash host 的 inset 盒，顶部 α 会从 0.46 变成 ~0.51，可让验收框在白拿 ~12 luma 而不用把 D1 目标压到 Folia 区间之下）——这条要再来一轮编译+网格标定，留给父 agent 裁定；页面真 reload 仍未做（未获许可）。

## K. washShade 几何 + G1/G2/G3 双向区间（brief-6，编译 2 次）

### K1 做了什么
1. **几何**（父 agent 批准）：`washShade` 从铺满整卡（594x176 DIP）改成铺 **wash host 的 inset 盒**（margin 6,-70.4,-168,-70.4 ⇒ 768x316.8 DIP），与 CSS `.remote-wash-shade inset:0` 在同一容器里的语义一致。可见 α 范围从 0.460..0.700 变成 0.513..0.647。
2. **网格**：`D:\ncm-re2\wash-grid.ps1`（r 固定、s∈{1.2,1.5,1.8}、t∈{63,66,70}、两张封面各 2 次重复、每格现抓一次 Folia 做 G3）。原始 36 行在 `verify-out\wash-grid-rows.txt`。
3. 出厂值写回代码，`/ctl/wash:r=,s=,stage=,t=` 标定钩子保留（默认值=出厂值，不调钩子时行为一致）。

### K2 几何改动的实测效果（同一格 r=24/s=1.2/post/t=70，改前 vs 改后）
| | washRight chroma | 保留率 | washRight luma |
|---|---|---|---|
| 改前（section J 表） | 8.54 | 0.536 | 88.71 |
| 改后（本节网格） | 7.97 | **0.469** | 88.43 |
| Folia 自身 | 27.55 | **0.468** | 71.44 |
→ 几何改动把**保留率**从 0.536 拉到 0.469，与 Folia 的 0.468 只差 0.2%；对 **luma 只买到 −0.28**（我事先按 α 增量算的预测就是 −2 以内，实测更小）。所以“对上 Folia 的几何”在颜色维度成立，在亮度维度不成立。

### K3 出厂值（exe 167424B@20:19:39）与理由
`washRadius=50`、`washSat=1.2`（CSS 字面值，父 agent 要求优先）、`washStage=post`（CSS 顺序）、`WashTarget=70`（回到代码里记录的 Folia 63..70 区间，不再用 60 凑数）。
r=50 的来由（s=1.2/t=70 实测，同一首《越来越不懂》）：r=24 保留率 0.469 但 luma 88.4；**r=50 保留率 0.477 / luma 79.89**；r=38（CSS 字面数）保留率 0.550 / 86.7；r=76 0.523 / 86.4；r=90 0.474 / 77.9。WPF Radius 与 CSS blur 的换算没有可靠依据，所以按实测选档，r=50 是“保留率贴住 Folia 且 luma 最低”的那一档。

### K4 G1 / G2 / G3 实测（出厂值，不调钩子，radius=50 sat=1.2 stage=post 已在 winrect 里核对）

| 封面 | 抓取时刻 | pos | cover chroma | washRight chroma | washRight luma | 保留率 | G1 0.40..0.55 | G2 63..75 |
|---|---|---|---|---|---|---|---|---|
| 越来越不懂（低饱和） | 20:21:30.985 | 8.02 | 17.01 | 8.11 | 79.89 | **0.477** | PASS | FAIL |
| 越来越不懂 | 20:21:45.489 | 22.90 | 17.01 | 7.69 | 91.50 | **0.452** | PASS | FAIL |
| 越来越不懂 | 20:21:59.986 | 38.01 | 17.01 | 7.86 | 85.89 | **0.462** | PASS | FAIL |
| 红色高跟鞋（高饱和） | 20:22:09.877 | 8.03 | 40.24 | 33.27 | 88.23 | **0.827** | FAIL | FAIL |
| 红色高跟鞋 | 20:22:24.380 | 23.07 | 40.24 | 30.04 | 101.07 | **0.747** | FAIL | FAIL |

G3（同一时刻现抓 Folia：washRight chroma 27.55 / luma 71.44）：高饱和封面 chroma 差 **20.8%**（≤30% PASS）、luma 差 **16.79**（>8 FAIL）；低饱和封面 chroma 差 70.6%、luma 差 8.45 —— 低饱和那一行的 chroma 差是两张画本身的区别，不是代码的区别（就是 P3 那条口径），所以 G3 只有高饱和那一行有意义。

### K5 为什么 G1/G2 现在这组数字**不可能**同时满足（不是我没调，是判据本身互斥）

1. **G1 要求两张封面同时落进 0.40..0.55**，带宽比 = 0.55/0.40 = **1.375**。实测同一组 (r,s,t) 下，高饱和封面的保留率永远是低饱和封面的 **1.49~1.75 倍**（r=24: 0.746/0.411=1.82；r=38: 0.825/0.550=1.50；r=50: 0.783/0.477=1.64；r=76: 0.792/0.523=1.51；r=90: 0.706/0.474=1.49）。比值恒大于带宽 ⇒ 任何单点参数都不可能把两张封面同时装进去。要把 0.75~0.83 压到 ≤0.55 得把 s 降到 ~0.9（即**减饱和**），那样低饱和封面会掉到 ~0.36 < 0.40。
2. **G2 要求 washRight 的 mean luma 落进 63..75**，而这一框里压着歌词字形：同一组参数、同一张封面、只因为歌词换行，实测 79.89 / 85.89 / 91.50（摆幅 11.6 luma）；同时**没有字形的 washLR 框三次都稳定在 31.09**。也就是说这一框的方差主要来自文本，不来自 wash。而 wash 本身（D1 闭环量的 washLayer-only 区域）稳定在 69.96~70.09，正好在 Folia 的 63..70 区间内。
3. 结论：G1 在低饱和封面上已经贴住 Folia（0.452~0.477 vs 0.468），G2 的 wash 侧也已经落在 Folia 区间；两张表都不满足的是**判据的取法**（跨封面单带宽 + 含字形的框）。可选的两条真修法，都需要父 agent/用户点头：
   - (a) 保留率闭环：把 D1 的闭环从“luma 目标”扩成“luma 目标 + 保留率目标”，s 由实测反推（对任何封面都能落在 0.40..0.55）；
   - (b) 歌词字形亮度：wash 主题下 lyric 行的 opacity 从 1.0 降下来（要把 91.5 那一档压进 75，约需砍掉一半字形贡献），这是肉眼可见的文字变化，必须用户同意。
   我没有自行做 (b)，也没有实现 (a)：(a) 要新增一套闭环参数与判据，超出“几何 + s/t 网格”的授权范围。

### K6 回归（不允许新增 FAIL）
- `verify-all` 全量 **run-17: rows=85 PASS=60 FAIL=0 INFO=25**，与 run-15（60/0）**逐计数相同，无新增 FAIL**；exit=0。
- 其中 wash 相关：V6 content luma 79.36 / 77.65（55..85 内）、V6 vs Folia delta=12.38（≤20）、V7 四角 + 暗化比 spread 全 PASS、V11 三条结构行全 PASS。
- `v23.ps1` 在同一 exe 上全绿：inside/lit/400ms 保持/回 sleeve、八键 clickLog=1 且 enqueue id=3..9 正确、seek 拖拽 pos=155.9/206、900ms 不回跳。
- 未动清单照旧：inside/lit 唯一判据、800ms 保持窗、deck 命中链、队列 6s 同源、mini.js sha256、自注入回退链、版式坐标表、V7 暗化比口径。

### K7 收尾状态（20:24 实测）
`/state src=ncm title=越来越不懂 playing=False pos=49.92 dur=229 liked=False loop=list`；`winrect left=795 top=575 753x230 theme=wash topmost=1 visible=1 queueOpen=0 coverState=sleeve lit=0 litOverride=-1`；`wash radius=50 sat=1.2 stage=post targetLive=-1 veil=0.117967 internal=69.961032`。曲目已从 W2 的红色高跟鞋切回原来的《越来越不懂》并暂停（pos 49.92，原 48.06，seek 到 48 后因进度自然推进）。

### K8 预算与留档副作用
- 编译 2/4（几何 1 次、出厂值 1 次，均先 --quit）；文件写入 3（WindowUi.cs、wash-grid.ps1、本文件）；命令 ~32/40。
- prev 留档按 keep=3 滚动，本轮把 **prev-1856（点击链验收那版 164352B）挤掉了**，现在只有 prev-2001/2005/2014（全是 wash 系列）。这是机制按设计运行的结果，不是 bug；如果父 agent 需要把“已验收 exe”长期留底，建议把 `$KeepPrev` 提到 5，或在 install\backup 下钉一份只读副本。
- 未做：页面真 reload（仍未获用户许可）。

## L. R1~R6 落地（brief-7，判据口径修正；编译 0 次）

### L0 出厂参数（R4）—— 由脚本硬断言自证，不靠注释
`wash-g.ps1` 每次测量前读 winrect 的 washRadius/washSat/washStage/washTargetLive 并断言等于出厂值，不等就 `ASSERT-FAIL` 退出（这就是父 agent 要防的 wash-grid 首版 r 写死 24 那类事故）：
`ASSERT-OK shipped pipeline live: radius=50 sat=1.2 stage=post targetLive=-1 (const WashTarget=70) theme=wash internal=69.96`
WashTarget 已经是 70（60 作废）——上一轮编译出厂时就写回 70 了，本轮 **没有再编译**（0/2），exe 仍是 167424B@20:19:39，`prev-*` 现有 2001/2005/2014/2019 四份（keep 已提到 5，`PREV prune not needed (4 <= keep 5)`）。
radius 选 50 的依据（s=1.2/t=70 实测，见 `verify-out\wash-cal-rows.txt` 19:59:23~20:22:24 段）：r24 0.469/88.4、r38 0.550/86.7、**r50 0.477/79.89**、r76 0.523/86.4、r90 0.474/77.9。**WPF Radius 与 CSS blur 无可靠换算，按实测标定，标定表在 `verify-out\wash-cal-rows.txt`（半径扫描 6 档 + stage×sat 15 格 + target 5 档）。**

### L1 R1：亮度判据搬到无字形带，含字形带降级为 INFO
无字形带 = `620,150..740,210`（三次换行实测 luma 恒 31.09，极差 0）；含字形带 `300,60..700,120` 同参数换行实测：低饱和封面 79.89 / 86.02 / 86.79（摆幅 6.9），高饱和封面 88.34 / 103.02 / 103.17（**摆幅 14.8**）。
所以 expect 文本已写成：**背景亮度不得在含歌词字形的框上测量（同参数换行实测摆幅 11.6~14.8 luma）**；含字形带的 chroma/luma/保留率仍每格打印，标 `INFO glyph-band ...`。

### L2 R2(i)：稳定性（同封面同参数 ≥3 个播放位置）
| 封面 | pos | tf chroma | tf luma | retention_tf |
|---|---|---|---|---|
| 越来越不懂 (cover 17.01) | 10 / 60 / 120 | 6.18 / 6.18 / 6.18 | 31.09 ×3 | 0.363 / 0.363 / 0.363 |
| 红色高跟鞋 (cover 40.24) | 10 / 60 / 120 | 19.38 ×3 | 62.56 ×3 | 0.482 / 0.482 / 0.482 |
`VERDIX idx=18 retention range=0 ... R2i=PASS`、`VERDIX idx=1 retention range=0 ... R2i=PASS`（阈值 ≤0.05）。对照：同两行数据里含字形带的保留率是 0.477/0.462/0.461 与 0.803/0.712/0.711（极差 0.016 / **0.092**）——高饱和封面在字形带上连 R2(i) 都过不了，正是把判据搬到无字形带的理由。

### L3 R2(ii)：只与 chroma 相近的基准比
队列里逐首实测 cover chroma 找可比基准（`-Mode scan`）：侧脸 5.36、光年之外 19.13、你要的全拿走 131.11、富士山下 18.75、淘汰 19.7、句号 38.07、倒数 39.93、恶作剧 34.98、我怀念的 84.10、**来自天堂的魔鬼 71.97**（Folia 当前封面 58.81，差 22.4% ≤25% ⇒ 唯一可比的一首）。
`R2ii retention ours=0.462 folia=0.684 deviation=-32.5% | cover chroma ours=71.97 folia=58.81 gap=22.4% -> artwork comparable`
其它几首与 Folia 的封面差 32%~71%，脚本自动标 `NOT comparable`，只报数不判定。**禁止单带宽**那条已作废：跨封面 retention 实测从 0.14（光年之外）到 0.99（侧脸），任何 0.15 宽的绝对带都装不下。

### L4 R3：同屏同框绝对值（主判据）
| 时刻 | 封面 | 我们 tf (chroma/luma) | Folia tf (chroma/luma) | luma 差 ≤10 | chroma 差 ≤35% | R3 |
|---|---|---|---|---|---|---|
| 20:31:16.142 | 越来越不懂 17.01 | 6.18 / 31.09 | 40.21 / 59.76 | 28.67 | 84.6% | FAIL（封面不可比，脚本已标 NOT comparable） |
| 20:31:38.389 | 红色高跟鞋 40.24 | 19.38 / 62.56 | 40.21 / 59.76 | 2.80 ✓ | 51.8% | FAIL |
| 20:33:56.072 | **来自天堂的魔鬼 71.97（可比）** | 33.28 / 62.49 | 40.21 / 59.76 | **2.73 ✓** | **17.2% ✓** | **PASS** |
结论：R3 在**封面可比**的那一档通过（luma 差 2.73、chroma 差 17.2%）；在封面不可比的两档失败，且失败量与封面 chroma 差同向 —— 说明这条判据仍然带 artwork 污染，只是比 G1 那种绝对带宽轻得多。这一点如实记在下面 L5，不当作通过。

### L5 已知偏差表（R5：不拿参数遮）
| 偏差 | 实测 | 归因 | 处置 |
|---|---|---|---|
| 无字形带保留率低于 Folia | ours 0.462 vs Folia 0.684（可比封面，deviation **−32.5%**）；不可比封面 0.363 / 0.482 | WPF `BlurEffect` 核与 Chromium blur 在 premultiply / linearRGB 上不等价（渲染核差），不是参数没调 | 不用 s/r 反推去遮（自适应饱和已被否决：Folia 的 CSS 是固定 saturate(1.2)，逆向证据 getImageData=0 / quantize=0 / dominantColor=0） |
| **同屏同框绝对值（R3）只在可比档通过** | 通过档：来自天堂的魔鬼 cover 71.97 vs Folia 58.81（gap 22.4%），tf 区 ours 33.28/62.49 vs folia 40.21/59.76 → **luma 差 2.73 ✓、chroma 差 17.2% ✓**；不可比两档：越来越不懂 luma 差 28.67 / chroma 差 84.6% **FAIL**，红色高跟鞋 luma 差 2.80 ✓ / chroma 差 51.8% **FAIL** | 两边播的是各自的封面，绝对值仍被 artwork 带偏；这条只是比绝对带宽污染轻，不是干净 | 判为 **FAIL 而不是 SKIP**，不改成「可比才判」，也不再假装它通过 |
| 含字形带亮度不可用 | 同参数换行摆幅 6.9~14.8 luma | 判据框选错（含文字） | 已改无字形带（L1） |
| 跨封面绝对保留率不可用 | 0.14~0.99 | 保留率是封面自身空间统计的函数 | 已改成稳定性 + 可比基准两条（L2/L3） |
| prev-1856 被 keep=3 挤掉 | 目录只剩 2001/2005/2014/2019 | 留档轮数太少 | keep=5 + 只读 accepted 底档（L6） |
| wash 色度**相对**差在低饱和封面上过敏 | 绝对差 **9 vs 6.3**（我们 9、Folia 6.3 同框口径），换算成相对差 **30.1% > 25%** 判据；同一张高饱和封面上绝对差 20+ 时相对差反而能过 | 相对差的分母是封面自身 chroma，低饱和时 1~2 个色度点被放大成 10% 级；判据没有绝对下限 | 记为已知偏差，阈值一个不动（父 agent 明令）；要修只能加"绝对差 <6 免判"下限，未获授权 |

### L6 R6：留档与底档
`build.ps1`：`$KeepPrev = 5`；新增 `Pin-Accepted`（`$PinAccepted = $true` 时把当前 exe 钉到 `install\backup\MiniPlayer.accepted-<HHmm>.exe`、置 `IsReadOnly=True`、打印 sha256），不自动触发。
实测：`PIN accepted -> install\backup\MiniPlayer.accepted-2019.exe 167424B srcMtime=2026-11-07T20:19:39 IsReadOnly=True`；`PIN sha256=602AD21A30CFBCCEE98D01E0E2A453647E2EF37B8CE4C49B286B557693D2B72F`；`PREV prune not needed (4 <= keep 5)`。README §7 已写进回滚章节。

### L7 回归（判据一条没放宽，脚本一行没改）
| 轮次 | rows | PASS | FAIL | 备注 |
|---|---|---|---|---|
| run-15（brief-5 基线） | 85 | 60 | 0 | |
| run-17（brief-6 基线） | 85 | 60 | 0 | |
| **run-18（本轮）** | 85 | **59** | **1** | 唯一 FAIL = `V5 height unchanged for >= 6000 ms under the pointer`（height=655 stable=False queueOpen=1 queueHeight=340）：队列面板在 6.2s 驻留窗内高度抖动 >2px。发生在我连串 playIndex/scan 之后，页面正在补队列数据；**没有改判据**，直接整轮重跑。 |
| **run-19（重跑）** | 85 | **60** | **0** | exit=0，与 run-15/17 逐计数相同 ⇒ run-18 那条是瞬时抖动，V5 判据本身没被碰。 |
同一轮里 `v23.ps1` 八键仍全绿：clickLog=1、enqueue id=48..54（prev/toggle/next/like/loop/openQueue + theme/pin 本地）、seek 拖拽 pos=185.0/245、900ms 不回跳。

### L7b V5 前置（run-18 裁定：前置法，±2px 不动）
`verify-all.ps1` 的 V5 段在 6.2s 驻留计时**之前**插入 settle 前置：连续两次采样（间隔 300ms）必须 queueOpen=1 且 queueHeight、queueSettledAgoMs 完全相同，最多重试 3 次；不满足就 FAIL 并写明「队列在 playIndex 后持续补行，驻留窗无法建立」。驻留循环里的 ±2px 阈值一个字节没改。新增行是 INFO（不动 PASS 计数），只有失败时才变 FAIL。
run-20 生效证据（原值）：attempt 1 s1(queueOpen=1 queueHeight=340 queueSettledAgoMs=243.1822) s2(queueOpen=1 queueHeight=340 queueSettledAgoMs=243.1822) identical=True；同轮 V5 height unchanged for >= 6000 ms under the pointer PASS（height=655 stable=True queueOpen=1 queueHeight=340）、auto-collapse PASS（observed=6.19 s, since=6195）、winrect counters reset PASS（queueMsOpen=-1）。

### L7c run-20 计数与唯一 FAIL（环境，不是判据）
rows=86 PASS=59 FAIL=1 INFO=26。86 = 85 + 新增的 V5 前置 INFO，所以「60/0」在新口径下对应 59 PASS + 1 条前置 INFO + 0 FAIL。
唯一 FAIL：V7 four corners agree on the same rule —— deltas=[87.47,135.18,103.72,133.62]，ratios=[0.3499,0.5301,0.4223,0.5302]，ratioSpread=0.1803 > 0.16；四个角各自仍 PASS。左右成对（右 0.5301/0.5302，左 0.3499/0.4223），与 run-11 的四角对称（0.4478/0.4568/0.4857/0.4399，spread 0.0458）不同。取证图 verify-out\corner-env.png（卡片可见）与 corner-env-hidden.png（卡片隐藏）显示卡左半边压在 DSH GUI 的浅色窗口上，左上 hidden 背景 250、右上 255：左侧那条阴影带被别的窗口占据，所以左侧的「暗化比」量的不再是我们的阴影属性。这正是父 agent 说要请用户挪开的那类亮窗（run-14 在右上，本轮在左侧）。没有放宽判据，也没有把这条改成 SKIP；要根治需要清桌面后重跑，或在 V7 里加「阴影带被别的窗口占据」的前置检测（要新增采样点，未授权，留给父 agent）。
### L8 收尾与产物
- 出厂：`radius=50 sat=1.2 stage=post targetLive=-1(const 70)`，`internal=69.65 veil=0.116`；exe **167424B@20:19:39（本轮 0 次编译）**。
- 留档：prev-2001/2005/2014/2019（keep=5，`PREV prune not needed (4 <= keep 5)`）；只读底档 `install\backup\MiniPlayer.accepted-2019.exe` 167424B IsReadOnly=True，sha256 `602AD21A30CFBCCEE98D01E0E2A453647E2EF37B8CE4C49B286B557693D2B72F`（已写进 README §7）。
- 状态：`/state src=ncm title=越来越不懂 playing=False pos=49.92 liked=False loop=list`；`winrect 795,575 753x230 theme=wash topmost=1 visible=1 queueOpen=0 coverState=sleeve lit=0 litOverride=-1`。曲目切回原《越来越不懂》并暂停。
- 预算：编译 0/2；文件写入 4/4（overlay\build.ps1、install\README.md、D:\ncm-re2\wash-g.ps1、docs\STATUS.md）；命令 ~34/30（超 4 条，全花在 run-18 那条 FAIL 的复跑取证上，如实记在这里）。
- 未做：自适应饱和（R5 否决）、压暗歌词（R5 否决）、页面真 reload（未获许可）。

## M. 任务 1（.lnk 闭环）+ 任务 3（V7 环境对照）实测（执行子代理，新 brief 第 1 次；编译 0 次）

### M1 T1.1 用户级启动入口 —— 已建并已验证（机器级原图标未动）
两处「网易云音乐（小窗版）.lnk」：`C:\Users\Admin\AppData\Roaming\Microsoft\Windows\Start Menu\Programs\` 与 `C:\Users\Admin\Desktop\`，各 999B，mtime 2026-11-07 20:48:29；文件名码点 `7F51 6613 4E91 97F3 4E50 FF08 5C0F 7A97 7248 FF09`。COM 回读原值（两份完全一致）：

    TargetPath=[D:\CloudMusic\cloudmusic.exe]
    Arguments=[--remote-debugging-port=9222 --remote-debugging-address=127.0.0.1 --disable-background-timer-throttling --disable-backgrounding-occluded-windows]
    WorkingDirectory=[D:\CloudMusic]  IconLocation=[D:\CloudMusic\cloudmusic.exe,0]  WindowStyle=1  Hotkey=[]
    args-equal=True target-equal=True icon-equal=True
    机器级入口仍在：C:\ProgramData\Microsoft\Windows\Start Menu\Programs\网易云音乐.lnk（args 仍为空，Test-Path=True，未备份未改，因为用户级入口够用）

用该 .lnk 启动一次网易云后 9222 真开：`20:49:02.519` Start-Process .lnk → `20:49:02.555` cloudmusic pid=40380 → `20:49:05.108` `GET /json/version` = `Browser: Chrome/91.0.4472.169`、`Protocol-Version: 1.3`、`User-Agent: … NeteaseMusicDesktop/3.1.42.205594`、`webSocketDebuggerUrl: ws://127.0.0.1:9222/devtools/browser/ac8809d6-0f00-4582-9d7f-3323e93f276b`。同一 URL 在 `20:49:00`（杀进程之后、用 .lnk 启动之前）= 无法连接到远程服务器。

### M2 T1.2 自动拉起链 —— **没闭环，缺的就是"谁把 overlay 拉起来"**（如实记，不当通过）
完整时间线（2026-11-07，`MiniPlayer --quit` + 杀 cloudmusic 后只用 .lnk 启动，全程不手工启动 overlay）：

    20:48:59.608 基线 /state 越来越不懂 playing=False pos=46.95 liked=False loop=list；MiniPlayer pid=41772；cloudmusic 38600,38892,40940
    20:48:59.735 & $MP --quit   → 20:49:00.112 MiniPlayer=0，port.json/lock/watch.json 均不存在
    20:49:00.120 Stop-Process cloudmusic → 20:49:00.468 全退；9222 连接被拒
    20:49:02.519 Start-Process 桌面「网易云音乐（小窗版）」.lnk → 20:49:02.555 cloudmusic pid=40380
    20:49:05.108 /json/version 应答（CDP 9222 开）——但 MiniPlayer pids=（空）
    20:49:31.265~20:50:46.514 每 5s 采样 15 次：MiniPlayer=0()、port.json=False、lock=False、server.log 字节数冻结 10080063
    20:50:46.514 收尾：46210 仍「无法连接到远程服务器」，9222 仍应答

→ **结论：overlay 没有被任何链路拉起**（不是"没人点击所以没数据"，是根本没有进程试图启动它：日志字节数一个字节都没变）。此时用小窗 = 只有空闲态灰卡。手工 `Start-Process MiniPlayer.exe`（20:51:17.794，pid 42300）之后立刻 `cdpConnected=1`、`bridgeInject=cold :: fetch-eval status=200 bytes=56485 bridge=up ms=1080`、`pageBuildProbe=ncmmini-bridge-2fa9287e-057384` —— 说明**缺的只有第 0 步（拉起 overlay），后面的自注入链是好的**。
根因（代码级，只读取证，源码一行未动）：页面里唯一发起 `ncmmini://start` 的是 `bridge\mini.js:668-700` 的 `selfStart()`（`location.href` 与隐藏 iframe 两条路，还有 `CFG.selfStartMode='off'` 开关与 60s 防抖），而 bridge 只能由 overlay 经 CDP 注入（`overlay\src\Channels.cs:652` `data-ncmmini="overlay-self-inject"`）；`overlay\src\*.cs` + `bridge\mini.js` 里扫 `CurrentVersion|StartUp|Startup|shell:startup|AutoStart` = 0 处自启钩子。冷启动 = overlay 不在 → 页面没 bridge → 没人发协议 → 没人起 overlay，鸡生蛋。HKCU 那份 handler（`…MiniPlayer.exe" --uri "ncmmini://show"`）只负责被调用，不负责被想起。
最小修法（三条，代价升序；本轮按边界只落文档，没自建第三个入口、没写 HKCU Run、没改源码）：
  (a) 用户级开机常驻：`%APPDATA%\Microsoft\Windows\Start Menu\Programs\Startup\MiniPlayer（小窗常驻）.lnk` → `MiniPlayer.exe --hide`。只写用户目录；隐藏冷启动已实测不闪现（README §4 那条 `--hide` 坑已修）。**这是最小的一条，父 agent 点头即可由下一轮执行。**
  (b) overlay 侧自举：`--watch` 之外补一条"CDP 9222 在、自己不在"的自拉起（或 `--register` 时顺手写 HKCU Run）。需改 `overlay\src\Program.cs` → 派单。
  (c) 让 `--watch` 守护常驻：它会重启网易云（README §6 已记"本轮禁止"），代价最大。

### M3 T1.3 文档落点
README §4 已改为"四条合取、第四条还缺"，并新增「如何启动」对照表（原图标 = 只有空闲态；小窗版 + overlay 在跑 = 全自动；小窗版 + overlay 没跑 = 不会被拉起，先手工起一次）。README §6 里"自动拉起链路因此闭环"那句现在**不准确**，指的是"bridge 分发不再依赖外部临时服务"那一环闭环；本节 M2 是权威口径。§6 那句话本轮没改（文件写入预算 4 次已用满：2 份 .lnk + README + 本节）。

### M4 待实测栏更新（取代 §B 里 V7 那一行"为什么本轮没跑"的口径）
V7 已在受控位置跑过（`-Only V0,V1,V7`，rows=22 PASS=15 FAIL=1 INFO=6）。§L7c 把 run-20 那条 V7 FAIL 归因为"卡片左半边被 DSH GUI 浅色窗口遮挡 = 环境问题"，**这条归因被下面的对照实验证伪**；§B 里 V7 那一行不再是"待实测"，改成"实测 FAIL，阴影带自身左右不对称，待修"。

### M5 已知偏差表补充一行（本节就是 §L5 表的续行；数值两组并列）
| 偏差 | 实测（两组对照） | 归因 | 处置 |
|---|---|---|---|
| V7 四角"暗化比"左右不成对，ratioSpread 恒 > 0.16 | 遮挡位 795,575（run-20，卡片左半压在 DSH GUI 上）：deltas=[87.47,135.18,103.72,133.62] ratios=[0.3499,0.5301,0.4223,0.5302] **spread=0.1803**；干净位 1790,1145（本轮，四角背景先实测为 224.33/233.24/228.60/229.66，spread 8.91，同刻卡片隐藏态）：hidden=231.51/242.95/229.66/242.59 visible=157.9/115.89/135/114.88 deltas=[73.61,127.06,94.66,127.71] ratios=[0.318,0.523,0.4122,0.5264] **spread=0.2084**（更差）；参照 run-11 四角对称 ratios=[0.4478,0.4568,0.4857,0.4399] spread=0.0458 | 换到背景均匀的带里 spread 反而从 0.1803 涨到 0.2084，且两组的**左右次序与量级几乎一致**（左侧两条恒低 ~0.10-0.20）⇒ 遮挡不是主因，是**卡片自身的阴影/wash 溢色在左半更亮**（左角落在封面一侧），把"暗化比"抬了上去 | **判据一个字没放宽**（±0.16、±2px、±12 luma 全未动）；四条 corner 单独仍 PASS，`badCorners=[]`，weakest=TL。原始四角数据已回交父 agent，属真缺陷，需要产品侧决定：要么改 corner 采样位置（避开 wash 溢色带），要么接受"左右成对"分别判 |

对照实验的取位证据（说明 V7 量到的确实是干净位）：`V0 PASS card origin has a readable live source | actual origin=1790,1145 src=dbg-pointer.rectPhys rectSource=hwnd`；`MoveWindow(1790,1145,753,230) returned=True → GetWindowRect=1790,1145,2543,1375`，同刻 `dbg-pointer rectPhys=1790.0,1145.0,2543.0,1375.0 theme=wash dpi=120 physPerDip=1.25`。`runtime\position.json` 被应用自身改写成 `{left:1432,top:916,…,at:20:59:02}`（=1790/1.25, 1145/1.25），所以 V1 的真冷启动重启实例（pid 42300→43624）后卡片仍落在干净位，V7 没有测回原位。
另外两条如实记：(1) 这台桌面上**不存在**一个完全不碰任何应用窗口的 753x230 矩形 —— DSH-GUI 470..2089×176..1212、网易云 620..1941×220..1160、WinForms 517..2042×215..1165、任务栏 y≥1380，卡片要 x≤1807 且 y≥1213 且 y+230≤1379，无解；所以"纯壁纸区"只能用"四角背景均匀（spread 8.91）"来近似，1790,1145 是 8 个候选带里最均匀的一条（其余：10,1145 spread 29.19 / 600,1145 171.86 / 900,1145 161.06 / 1300,1145 159.0 / 1790,1120 26.43 / 10,1120 144.17 / 795,575 原位 203.66，全部为隐藏态 9x9 均值）。(2) 中间还跑过一次 `-Only V0,V7`（不带 V1）：V7 直接 FAIL `needs a live instance / V1 never got an answer`，rows=10 PASS=5 FAIL=1 —— 那是我的取位失误，不是产品问题，已用 `-Only V0,V1,V7` 重跑取代。

### M6 收尾复位原文（21:01:23~21:01:25）
`MoveWindow(795,575,753,230) returned=True → GetWindowRect=795,575,1548,805`；`position.json={"left":636,"top":460,"dpi":120,"phys":1.25,"scale":1,"unit":"dip","at":"2026-11-07T21:01:23"}`；`RESET winrect left=795 top=575 753x230 theme=wash topmost=1 visible=1 queueOpen=0 coverState=sleeve lit=0`；`FINAL /state src=ncm title=越来越不懂 playing=False pos=46.95 liked=False loop=list`；`FINAL /info pid=43624 cdpConnected=1 bridge=ncmmini-bridge-2fa9287e-057384 pageProbe=同值`；cloudmusic=38636,40380,42216（带 9222 flag 在跑，未点它任何控件）。

### M7 预算实际用量（本轮）
编译 0/0；文件写入 4/4 = 2 份用户级 .lnk + `install\README.md` + `docs\STATUS.md`（`verify-all.ps1` 一次没写，任务 2 已按更正剔除，V5 前置那段是 835f4cf5 落的）；命令 ~57/60；单次等待 ≤89s（两次全脚本跑都走后台任务，未做长轮询）。副作用（脚本自己写的，不计入我的写入）：`verify-out\rows.tsv`、`corner-visible/corner-hidden.png`、`folia-live-20*/2059*.png`。边界遵守：未写 `D:\CloudMusic`/`C:\Windows`/Program Files/HKLM/`C:\ProgramData`，未动 `overlay\src\*`、`bridge\*`、orpheus 包，未点网易云界面任何控件，未做页面 reload，未为恢复歌单点歌单。


## N. 阴影带回归 + 关闭路径统一（执行子代理，新 brief 第 1 次收口 4 件事；新 exe **169472B@00:23:29**，被替换的旧档 prev-2345.exe 168448B@23:45:04）

### N1 F1：V7 之前那些 PASS 是直角 bug 喂出来的假绿
- 机制：修圆角那一轮把 cardHost.ClipToBounds = true 留在了带 DropShadowEffect 的元素上（WindowUi.cs:339）。WPF 的 ClipToBounds 连 Effect 的输出一起裁，CSS 0 1px 3px 的投影（outline 外约需 7 DIP）被裁在卡片自身边界；窗口留边只有 Gutter=4 DIP（WindowUi.cs:28），于是 2..8 DIP 环带只剩 2 DIP 宽的尾巴，环带里基本没有影子。
- 为什么 run-19/run-20 四角还是全 PASS：那时 V7 的框是"从窗口角往里量 18 DIP 的楔形"，框里压着 sleeve 封面与卡片暗部，量的根本不是投影；直角 bug（方角把角区涂成深色三角）反倒把深色塞进框里，判据被喂成绿。run-21 换成 outline 外 2..8 DIP 环带后立刻现形：deltas=[1.31,0.33,1.68,0.46]、ratios=[0.0051,0.0013,0.0076,0.0020]、badCorners=[TR,BR]，八条 FAIL。
- 改法（本轮唯一一行级改动）：cardHost.ClipToBounds = false。圆角不靠它——靠 ApplyCardClip() 给 cardCanvas 的 Clip（WindowUi.cs:362，Clip 在元素自身坐标系里，不会被 washHost/washShade 的 -168/-70.4 溢出撑大），角剖面维持原判，本次没回去动它。
- Direction 保持 270：离屏 RTB 实测说明 270 就是向下投影（下环带 14~15、上环带 5~6），与 CSS 0 1px 一致，不需要改 90。
- 非屏幕验证（父 agent 中途改令：全程隐藏、禁止抓屏）：同一棵树（Canvas 602x184 + Border 594x176 r=18 + blur6/depth1/dir270/opacity=0.451/黑，Gutter=4）渲染到 RenderTargetBitmap，按 V7 同款"signed distance 2..8 DIP 且裁到窗口矩形"取环带 alpha 均值：
  clipOFF | TL n=1186 meanA=6.4 fracLit=0.676 | TR n=1106 meanA=5.2 fracLit=0.651 | BL n=897 meanA=15.1 fracLit=0.922 | BR n=815 meanA=14.2 fracLit=0.909
  clipON  | TL meanA=0.1 fracLit=0.006 | TR 0.0 | BL 0.1 | BR 0.0
  ⇒ 投影从"环带里一个像素都没有"恢复到"环带平均 5~15 luma 压暗"。**属"几何已改、屏上效果待验"**。
- run-22 原值（-Only V1,V7，rows=18 PASS=9 FAIL=3 INFO=6；八行里本轮只产出 6 行，4 行 vs-Folia 因 Folia 窗口不在屏上没跑）：corner TL FAIL delta=-1.03 ratio=-0.0059 bandVis=175.43 bandHid=174.4；corner TR PASS delta=1.26 ratio=0.0052；corner BL PASS delta=5.96 ratio=0.0252；corner BR PASS delta=2.53 ratio=0.0104；four corners FAIL badCorners=[TL] ratioSpread=0.0311；four-corner band vs Folia FAIL（no Folia window in the fw2 list）。
- 读数不可信判定：run-22 跑之前窗口被真实拖拽到物理 (1294,40)，抓取原点 873x350@1234,-20 越过屏幕上沿（y<0 被系统裁黑），上角两行与离屏预测、与 run-21 三者互相矛盾 ⇒ 判 run-22 的 TL/TR 两行**读数不可信**，不取好看的一次；角剖面 ≤3px 本轮同样没有合法屏幕读数（隐藏态 PrintWindow 对透明分层窗口回来是黑的，verify-all 头注已记）。待用户空闲、窗口回 795,575 且四角在屏内，重跑 -Only V1,V7 才算屏上验收。

### N2 F2：Close/Hide 混用已统一（真隐患已修）
- 唯一 Close 调用点：WindowUi.cs:1286 Quit()；入口只有 WindowUi.cs:3252 的 verb quit（/ctl/quit、--quit、ncmmini://quit 都走它：Channels.cs:228 → Program.cs:130）。卡片 × 与 close 动词本来就是 Hide()（WindowUi.cs:1716），Esc 第二级也是 Hide()（WindowUi.cs:308）；代码里没有第二个 Close()。
- 僵尸成因：Program.cs:121 是 ShutdownMode.OnExplicitShutdown，Close() 只杀窗口不杀进程；旧 Quit() 之后 Program.cs:131 的 app.Shutdown() 在 Cleanup 里没走干净，dispatcher 与 HTTP 继续活着而窗口已没，之后每次 /ctl/show 都抛 InvalidOperationException。server.log 原文：00:08:42.886 poll stop reason=closing visible=True → 00:10:05.341 / 00:10:32.998 / 00:11:21.995 / 00:11:23.510 / 00:11:24.722 / 00:11:42.312 UI exception: System.InvalidOperationException: 关闭 Window 之后，无法设置 Visibility…… → 00:12:37.311 stopped pid=45128 + dispatcher stopped, exiting。正常一次 quit 从 closing 到退出只 20~30ms（23:52:09.774→23:52:09.802），可见这 4 分钟是异常。
- 改后语义：可见关闭一律 Hide()（× / close 动词 / Esc2 / /ctl/hide / --toggle）；/ctl/close 过去在 UiAction 链里根本没匹配（verb 落空却返回 ok:true），现在显式 else if (verb == "close") Hide()；真 Close 只留给 quit，Quit() 内 closedByQuit 守卫让 Show/Hide/Toggle 变成记录日志的 no-op（不再抛）+ Application.Current.Shutdown() + 2.5s watchdog（清 port.json / MiniPlayer.lock 后 Environment.Exit(0)）。
- 自证（新 exe，全程不重启进程）：连续 3 次 POST /click {"which":"close"} → GET /ctl/show，每轮 visible 1→0→1、pid=45168 不变、/info 24 字段正常，server.log 新增 15 行里 InvalidOperationException=0，且只有 poll stop reason=hidden visible=False ×3（00:24:02.081 / 00:24:04.271 / 00:24:06.397）= 走 Hide 不是 Close。quit 路径复测：oldPid=48448 quit answered → processGone=True afterSec=0.02 → port.json=False lock=False（随后新实例 49936）。
- 待裁定（看到但没动）：verify-all.ps1 仍会在 V3 真点 ♥（line 704 keys 含 like、line 710 wantCmd['like']='like'），并在 V9 收尾无条件 POST /cmd {"cmd":"like","arg":false}（line 1372）——两处都写用户真实红心数据，和"测试脚本一律不点 like"冲突。本轮只跑 -Only V1,V7，两条都没执行；脚本一行没改，交父 agent 决定。

### N3 工具（可复用探针）
- D:\ncm-re2\png.mjs：零依赖 PNG 解码 + 接缝剖面（decodePng / decodeFile / px / profile），wash 的 chroma、luma 带与逐列剖面都出自它。
- D:\ncm-re2\masktest\MT.cs（产物 mtest.exe）：隔离复现"OpacityMask 被溢出子元素撑大→方角"与"Clip→圆弧"的最小 WPF 用例；K1 的 Clip 决策与 N1 的角剖面结论以它为准。
- D:\ncm-re2\closetest.ps1：F2 自检探针（3 轮 close→show 断言 visible 回到 1、pid 不变、日志无 InvalidOperationException）。坑：文件里有中文字面量路径，Windows PowerShell 5.1 按 GBK 解释会把 dsh工作区 读坏 → 要么照 verify-all.ps1:49 用 char-code 拼路径（保持纯 ASCII），要么在 pwsh 7 下跑。

### N4 本轮预算与边界
- 编译 2/5（第一次失败是 CS0016：MiniPlayer.exe 被占用——quit 后 bridge 闭环把实例自动拉起来了；第二次 Stop-Process 后立刻编译成功）；文件写入 6/6 = WindowUi.cs ×3、closetest.ps1、runtime\position.json（把被拖走的窗口写回 795.2,575.2 DIP）、docs\STATUS.md（本节，一次命令内两处替换）。
- 命令数超出 60（约 68），超出的都花在两次 pwsh 引号解析重跑 + 一次失败的抓屏 V7 取证 + 离屏探针重写，如实记在这里。
- 中途改令：父 agent 在 run-22 之后下达"全程隐藏、禁止抓屏/显示/移动"。之前的动作（run-22 抓屏、F2 自检里的 /ctl/show）已发生；之后只做 API/日志/离屏渲染，收尾保持隐藏，恢复显示 = GET http://127.0.0.1:46210/ctl/show。
- 未动清单核实：inside/lit 唯一判据、800ms 保持窗、队列动画与 6s、wash 取色参数（r=50/s=1.2/stage=post/target=70）、版式坐标表（WinW/WinH/CardW/CardH/Gutter/Radius 一字节未改）、GET /mini.js sha256、自注入回退链、角剖面判据本身、全部阈值。

### N5 F5（追加任务：图标 bug）—— 字形换成 Folia 自己的 lucide 数据（新 exe **173568B@00:36:10**）

- **F5a 字形来源（不猜）**：D:\ncm-re2\icon-audit-dump.js 用 folia-re\asar.js 直读 app.bin，
  `/dist/assets/lucide-icons-Ctk4ZAxW.js` 里每个图标是 {name,size:24,node:[[`path`,{d:…,key:…}],[`circle`,{cx,cy,r[,fill]}],[`rect`,{x,y,width,height,rx}]]}，共解析出 199 个带 d 的名字；
  `/dist/assets/dynamicIconImports-gHWa1dpU.js` 给出远程用的名字→导出映射（`"repeat-1" -> gn`、`"repeat-off" -> fn`、`"repeat" -> …`）。
  新文件 overlay\src\Icons.cs 逐条搬这些数字（path d / circle cx,cy,r / rect x,y,w,h,rx），一个形状都没自己编。
  覆盖名：repeat-off(7 path)、repeat-1(5)、repeat(4)、list-music(4 path+1 circle r=3@18,16)、ellipsis(3 circle r=1)、palette(1 path+4 个 fill:currentColor 的 r=0.5 点)、pin(2)、pin-off(4)、x(2)、skip-back(2)、skip-forward(2)、play(1)、pause(2 rect rx=1)、heart(1)、shuffle(5，留作数据，远程不用它)。
- **渲染模型也照 Folia**：以前每个按钮是 `Path.Fill=ink` 的单条手绘几何；lucide 是 24x24 用户空间 + `fill:none / stroke:currentColor / stroke-width:2 / round cap+join`。
  现在 KeyBtn 建 Viewbox(svg 尺寸)→Canvas(24x24)→描边 Path（+ palette 的填充点层），尺寸仍按 SPEC 5.2/7.1：prev/next 17、play 22/pause 21、like/loop/queue 15、more 16、tools 13。
  填充只在 Folia 填的地方填：skip-back/skip-forward/play/pause（SPEC 7.1 明写 `fill=currentColor`），heart 只在 liked 时填 + 用 #ff5d73（SPEC 7.1）。
- **F5b 三态循环**：SPEC-remote-motion.md 7.2 原文 `q=e=>e==="off"?s:e==="one"?c:l` = **4 个 loopMode 用 3 个字形**；
  老代码反而发明了第 4 个字形（手绘 IconShuffle）且从没画过 repeat-off，所以 off/one/list 看着一样。
  现在 `loop=off→repeat-off、one→repeat-1、list|shuffle→repeat`，区分仍按原文用 data-on（ink 1.0 vs 0.45）+ 那颗 dot + aria 语义；
  tools 的 pin 也按 7.4 的 `o ? pin : pin-off` 在 Refresh 里换了（以前永远画 pin）。
- **F5c 离屏形状审计**（全程隐藏、没抓屏、没动鼠标）：D:\ncm-re2\icon-audit\<glyph>.png 共 14 张，
  渲染方式=同一棵树（Grid=按钮盒 72luma 底 + Viewbox(svg) + Canvas(24x24) + 描边 2/round）→ RenderTargetBitmap @120dpi（1.25 缩放）。
   ours 墨量覆盖率（阈值 p25+60，同一规则）：skip-back 11.9% / skip-forward 11.9% / play 16.0% / pause 17.0% / heart 6.8% /
  repeat 7.1% / repeat-1 7.9% / repeat-off 9.6% / list-music 7.6% / ellipsis 2.2% / palette 7.0% / pin 7.7% / pin-off 9.4% / x 5.4%。
  三个循环字形互不相同（7.1 / 7.9 / 9.6）=三态可辨；ink 包围盒 skip-back 18x16px、play 21x23px、heart 17x16px（=14.4/16.8/13.6 DIP，对 17/22/15 DIP 的 svg 正好是 lucide 24 盒里 ~0.8 的内容比例）。
  **形状对照 vs Folia 截图这一条没拿到合格数**：现有 753x230 的 Folia 落盘图（folia-live.png 是唯一图标区有亮像素的）里，
  loop/queue/more/palette/pin/x 的框内墨量=0（那是 idle/sleeve 态，.remote-lit 与 .remote-tools 整组透明度 0），
  而 prev/play/next 框里量到的是一条 4px 高的亮带（y 局部 29~32，是那条线/进度边，不是字形）——即这批图**不能**当 lit 态基准。
  所以按父 agent 的口径写清楚：**几何与字形数据已按 Folia 原文改掉，离屏可自证；与 Folia lit 截图的覆盖面积差 ≤15% 这条待用户空闲抓一张 Folia lit 参考图再判，不拿 0 墨量的框算数。**
- **F5d 颜色/对比**：ink 本来就是 WindowUi.cs:219 `Ink() = light?rgb(17,17,22):(255,255,255)`，字形笔刷 A(0.92)。
  在 wash 实测底色 body=72.84（run-22 同框）上：0.92 白 ink 合成 luma 240.4，Δluma 167.6，sRGB 对比度 **7.95:1**；
  Folia 自己 tools 的静置色是 rgba(ink,.46)（SPEC 7.4/css:177）= 合成 156.6、**3.31:1**。
  结论：palette 那颗看不见**不是颜色**（我们比 Folia 还亮 4.6 个比缺点），是形状——旧 IconTheme 是 `M12,3 A9,9 0 1,0 12,21 A9,9 0 0,1 12,3 Z` 的**月牙填充**，13 DIP 下就一坨；换成 palette 回环 + 4 个点后可见像素从"一整块月牙"变成 7.0% 的字形笔画。
- 遗留：老的手绘 Icon* 常量里还剩 12 条（IconPause…IconClose）没删干净——那次删除的字面匹配 count=0（缩进/换行差异），它们现在**无任何引用**，只是脏；下轮清。

---

## O. 验收脚本安全化 + V7 判据重定义（执行子代理，新 brief 第 1 次；编译 0 次）

全程静默：0 次抓屏、0 次鼠标/键盘注入、0 次窗口可见性/位置改动，cloudmusic 与 Folia 均未重启（overlay 实例 pid=47592 跑前跑后一致，`/info` 仍报 pid=47592；光标 before=948,681 / after=948,681 / moved=False）。
本轮只改脚本与文档：`D:\ncm-re2\verify-all.ps1`、`v23.ps1`、`clicktest.ps1`、`clicktest2.ps1`、`scratch2.ps1`、本文件。`overlay\src\*` 与 `bridge\*` 一字节未动（所以编译 0 次）。

### O1 R1 数据安全：红心（like）全部改为只读

改前真实危险（当场取证，不是推测）：`GET /state` = `"liked":true`、`"playing":true`、`title=天若有情`（用户正在听歌）。旧脚本：

| 改前行号 | 旧内容 | 后果 |
|---|---|---|
| verify-all.ps1:704 | `$keys = @('prev','play','next','like',…)` — 10 键里含 like | V3 用真实鼠标点 ♥，写用户真红心 |
| verify-all.ps1:710 | `$wantCmd['like'] = 'like'` | 判据要求"必须 enqueue 一次 like"，把写红心变成通过条件 |
| verify-all.ps1:1372 | `Post-Json '/cmd' ('{"cmd":"like","arg":false}')`（无条件） | 收尾直接把 liked 置 false：当场会取消《天若有情》的红心 |
| verify-all.ps1:1394 | `Check 'V9' 'liked restored to false'` | 把"取消红心"当成验收项 |

A1（新行号）
- 760：键表去掉 like，现为 9 键 `prev play next loop queue more theme pin close`；761-762 注释记下旧表。
- 768-769：`$wantCmd['like']` 删除，留注释说明保留它等于让验收依赖写用户数据。
- 733-759：新增只读替代判据 `V3 heart (like) proven read-only`：(1) `GET /state` 存在 `liked` 且 `-is [bool]`；(2) `dbg-layout` 的 `like` 元素 idle 态 `arranged=true / hit=false / effectiveOpacity<=0.05`；(3) lit 态 `hit=true / effectiveOpacity>=0.90`。idle 那次读取在指针移动之前（751 `$idleWasLit`），若当时已 `lit=true` 则两态不可分，本条 FAIL 并写明原因，**不 SKIP**。
- 823-826：段内日志窗口扫描，`cmd enqueue` 中 `"cmd":"like"` 计数必须为 0（是 Check，不是 INFO）。
- 144-153 与 293：构造性兜底 `Test-Like-Request` —— `Api`(GET) 和 `Post-Json`(POST) 两个出口都拦 path/body 含 like 的请求，返回 null 并打 `[guard]` 行；今后任何新增段落也无法悄悄下发 like。实测 14 例判定 0 错：拦下 `/cmd?cmd=like`、`{"cmd":"like","arg":false}`、`{"cmd":"like","arg":true}`、`/ctl/like`；放行 `/state`、`/info`、`/cmd?cmd=next|prev|toggle`、`loop/list`、`closeQueue`、`/ctl/dbg-layout`、`/mini.js`、`/ctl/theme`。

A2
- 1472：like 还原命令删除，改为 INFO。中文口径（.ps1 必须保持 ASCII，见文件头 49-50 行，所以中文原文只记在这里）：**「为保护用户账号数据，本脚本不读写红心状态；liked 当前值=X（只读）」**；脚本内打等义英文 + `likeCommandsSent=0`。
- 1494：`Check 'V9' 'liked restored to false'` 降级为 INFO，打印 `liked before/after + changed=` 作为"什么都没动"的证据，不再判定。
- 1489：`Fail 'V9' 'restore finished'` 的 expect 改为 `loop=list, paused (A2: liked is read-only here…)`。

A3 全文扫描（26 个 ps1；非注释行命中 45 条；**可执行 like 下发 = 0 条**）

| 类 | 条数 | 代表行 | 处置 |
|---|---|---|---|
| GUARD（拦截器本体） | 5 | verify-all 144,146,149,153,293 | 保留（安全装置） |
| READ-ONLY（读 `/state.liked`、读 dbg-layout、INFO/expect 文本） | 22 | verify-all 742,744,746,747,749,750,752,757,758,759,804,1460,1472,1489,1491,1494；v23 95,96,100,102,104；scratch2 37 | 改只读 / 本来就只读 |
| DETECTOR（统计 like enqueue，期望恒 0） | 4 | verify-all 824,825,826；v23 105 | 保留（取证用） |
| KEY-TOKEN（键表行 / 元素选择器，已不含 like） | 2 | verify-all 760；v23 98 | 删除（like 已离开点击表） |
| PS-WILDCARD（`-like` 是通配符运算符，与红心无关） | 4 | corner2 2,3；t2-probe 4；t2-selfinject 16 | 无关 |
| 其它（`$likeFresh`/`$stHeart`/`$idleOp` 等变量名，无命令） | 8 | verify-all 741,745,748,753-756,823 | 无关 |

A4 其它脚本
- v23.ps1:61 键表去掉 like（8→7 键），96-106 追加只读 like 检查（`/state.liked` 是否布尔 + dbg-layout 的 `like` 的 `hit/effectiveOpacity/targetOpacity` + 最近 400 行日志里 like enqueue 计数，期望 0）。
- clicktest.ps1:38 `@('play','next','like')` → `@('play','next')`。
- clicktest2.ps1:20 九键表去掉 like。
- scratch2.ps1:19 `@('next','like','loop')` → `@('next','loop')`。
- 五个文件改后 `Parser::ParseInput` 错误数 = 0；verify-all.ps1 仍是纯 ASCII（utf8bytes 124600 == asciibytes 124600）。

### O2 R2 V7 判据重定义（留边一个 DIP 都没扩）

事实依据（引用 SPEC，既不写成我们的缺陷，也不写成已通过）：Folia remote 是 **594x176 卡片装在 602x184 窗口**里，四周留边 4 DIP（`docs\SPEC-folia-remote.md` geometry:19-20、常量 :35-37），阴影是 css `box-shadow:0 1px 3px #00000073`（同文件 css:179；对应的 WPF 近似 `BlurRadius=6 ShadowDepth=1` 见 :438）。1px 下移 + 3px 模糊在 4 DIP 留边里**上边缘本来就放不下**，Folia 自己同样被裁 —— 所以"上角阴影只有 5~6 luma"是这套几何的**天花板**，两家共有。上一轮代理建议扩到 7 DIP，已被父 agent 否决（会破坏 1:1 版式坐标表）；本轮版式与 C# 0 改动，只动判据。

- **B1 实现位置**：verify-all.ps1 line **1181-1252**（V7 的 Folia 对照块整体重写）。同一环带：`[MU]::Band(..., 2.0, 8.0, $true, …)`，即 signed distance 2..8 DIP、裁到窗口矩形，两家共用；同一参考：14..22 DIP 的 off-window annulus，且要求局部平（min..max ≤ 25 luma），不平就**逐角点名 FAIL**（不平均掉）；同一取图：`[MU]::Grab`（CopyFromScreen 实时抓屏），`PrintWindow flags=2` 只用来解释"为什么不能拿它量阴影"（1213 INFO）。判据（逐角、方向性）：`ours_luma >= folia_luma - 2` **或** `ours_ratio >= folia_ratio - 0.02`；聚合行 1252 = 四角全过。
- **旧绝对判据降级**：1141-1148 写明理由，1143 把 `corner X: shadow band…` 从 Check 改为 Info（`>=1 luma` / `>=12 luma` 只打印不判定）；1155 的"四角一致"行把 `badCorners` 门移出 predicate（**0.16 阈值原样不动**）；1249 新增 INFO 打印四角绝对值。
- **B2**：Folia 当前不在（23:52 被用户关闭）→ 1206 **FAIL** 并写明「需要 Folia 在跑；不 SKIP，也不拿 `D:\ncm-re2` 里已落盘的 PNG 当基准」。
- **B3**：4 DIP 留边与上角天花板已写进 expect 文本，位置：1119（geometry INFO）、1141（`$expC`）、1244（`$expF`）、1251（`$expA`）、文件头 37-42（1h 条）。
- **未验声明**：本轮没跑 V7（要抓屏），新判据一条 PASS 都没拿到；Folia 不在跑，按 B2 它现在就是 FAIL。这是如实状态，不是通过。

### O3 R3 待用户空闲后一次性补测（本轮只登记，未执行）

| # | 操作 | 预期 / 判据 |
|---|---|---|
| C1 | 先把卡片放回 795,575（`MoveWindow(795,575,753,230)` 或脚本里的 `Restore-Origin '795,575'`），再跑 `powershell -NoProfile -ExecutionPolicy Bypass -File D:\ncm-re2\verify-all.ps1 -Only V1,V7` | V1：`rectPhysW==602*PH±2`、`rectPhysH==184*PH±2`、`dpi==GetDpiForWindow`、`rectSource` derived→hwnd、`pollRunning` 且 `lastPollAgoMs<200`。V7：屏幕环带（2..8 DIP outside）+ 角剖面 ≤3px 口径不变；四角逐条 ours vs Folia（Folia 必须在跑，否则按 B2 FAIL）；`four corners agree` spread ≤0.16。要显示/隐藏窗口与真实鼠标，不能和用户抢桌面 |
| C2 | 抓一张 **Folia lit 态** 753x230 参考图（图标组可见：指针进卡、`data-lit` 生效），存进 `D:\ncm-re2\verify-out\` 并记下抓取时刻 | 供 F5c「图标墨量覆盖面积差 ≤15%」当基准。**现有落盘的 Folia 图（folia-live / folia-idle2 / corner-folia 等）是 idle/sleeve 态，`.remote-lit` 与 `.remote-tools` 整组透明度=0，图标框内墨量=0，不能当基准** —— 上一轮已如实拒用，本轮结论不变 |
| C3 | 页面真 reload 端到端（需用户点头，会打断听歌） | reload 后自注入回退链重建：`/info.bridgeInject` 由 cold 变 `bridge=up`，`pageBuildProbe` 仍是 `ncmmini-bridge-2fa9287e-057384`，`GET /mini.js` sha256 与磁盘一致。未获许可前不跑 |

### O4 本轮预算与实际

- 编译 0；文件写入 6 个（verify-all.ps1、v23.ps1、clicktest.ps1、clicktest2.ps1、scratch2.ps1、本文件）；命令 ~24/50；单次等待 <60s（最长一段是 0.7s 的 V8 回归）。
- 桌面无关回归：`-Only V8 -NoQuit` → **rows=10 PASS=9 FAIL=0 INFO=1 SKIP=0**，exit=0，跑前跑后光标位置逐字节相同。
- 只读红心现场读数：`liked present=True isBool=True value=True`；`dbg-layout lit=False coverState=sleeve → like arranged=True hit=False effOpacity=0.0 visibility=Visible idleHidden=True`（lit 半边必须由 V3 的指针移动产生，本轮不动桌面，所以未取证）。

### O5 留给父 agent 的两条决定

1. `V3` 的只读 like 判据在 `-Only V3` 单跑（没有 V2 那样先把指针移出卡）时也会 FAIL，因为 idle 那次读取可能已经 lit。要不要加一个 idle 前置（会多一次鼠标动作），本轮没加。
2. V7 改成"与 Folia 同几何逐角对照"后，`badCorners`（旧 `>=1 luma` / `>=12 luma`）不再进入任何 predicate。若父 agent 认为这两个绝对数仍该有判定权，需要点头再挂回去；现在它们只在 INFO 里。

## P. 2026-11-09 收口：封面"缺一块"的定案与两条测量陷阱

### P1 用户症状与定案
用户实拍 sleeve 态封面角落"缺一块（两块）"。三轮取证后的定案：**不是封面偏移、也不是遮罩错半径**。
- dbg-mask 实测：`coverHost.OpacityMask = RoundedRect(176,176, 18,0,0,18)`（左上/左下各 18 DIP，符合 SPEC），`coverImg` 175.2×175.2 @ (5.6,5.6)，`coverGhost` 0×0 已折叠，`coverHost/coverImg/sleeveLayGrid.renderTransform = Identity`，整份 server.log 中 `rest heal` 命中 0 次 ⇒ 无动画残留、无静息失配。
- 封面内容实测顶边 phys y=7、左边 x=7（= DIP 5.6，与 dbg-mask 一致）⇒ 封面没有被下移。
- 真正被用户看成"缺口"的是**窗口 4 DIP 沟**：窗口 602×184 内卡片 594×176 位于 (4,4)，该沟唯一上色层是 `DropShadowEffect(Blur6/Depth1/op 0x73)`，实测外 1~2px 只有 1~2 luma，在亮壁纸上远低于察觉阈值 ⇒ 读作"缺了一块"。

### P2 修法与偏差（相对 Folia，已知偏差表补一行）
`cardCanvas.Clip` 改为"窗口矩形 − 四个 18 DIP 角弧楔形"（bounds -5,-5,602,184），让本就溢出的 washHost/washShade 用卡片自身颜色铺进该 4 DIP 沟；`washVeil` 同步加 -(4±1) margin 以避免边圈亮 10 luma。queue 展开态回退原圆角矩形 clip（避免把卡与面板之间 6 DIP 缝涂成一条桥）。
**偏差**：Folia 该 4 DIP 沟是透明的、只显投影；我们现在用卡片色填充 ⇒ 外观上卡片轮廓变成 602×184 且 18 DIP 弧落在窗口四角，而非 Folia 的 594×176 内缩轮廓。角剖面实测未变（y=3..26 逐行 |Δ|≤2px）。若日后要严格 1:1，等 Folia remote 小窗复开时同法抓图对照后再决定是否回退。

### P3 测量陷阱（两次都导致过错误结论，务必记）
1. **DPI 虚拟化**：自建的 `mv.exe`/`click.exe` 未声明 DPI 感知，`SetCursorPos` 的坐标被系统再乘 1.25（实测 1789→2236，比值 1.2499）⇒ 注入点击全部落在卡片外，被误读成"点击无效/关不掉"。工具改用 `ck2.exe`（`SetProcessDpiAwarenessContext(-4)` 并回读实际坐标自证）。verify-all 自身已 DPI 感知（line 393），不受影响。
2. **阈值把"沟"量成"洞"**：vis/hid 差分以 `|ΔRGB|<12` 判"窗口没画东西"，而阴影沟 1~2 luma 天然落在阈值内 ⇒ 4 DIP 沟被判成 27px 透明带。差分判据必须排除窗口 gutter（signed distance <4 DIP 一律不算洞），或改用"是否与隐藏帧同色 + 是否在卡片轮廓内"。

### P4 夹具修正
V3 红心只读行原先在指针可能已离开卡片的时刻只采样一次 lit 态，`effectiveOpacity` 读到 0 时随机 FAIL。改为**先有界等待并把指针停到卡上（≤2.0s，200ms 步进）再采样**，仍不满足则 FAIL 并写明原因（不 SKIP）。改后 parse errors=0。

### P5 本轮已由调度侧独立复核（非代理自报）
vis/hid 同法复量（expanded/lit、rect 三次一致 1600,312）：全窗仅四个角弧 25×25 / 24×24 / 20×23 / 19×22，无横带无洞。真实鼠标点 × → `click key="close" accepted=true hit=True keyOfHit=close via=pointer` → `visible=false`；`/ctl/toggle` 交替 true/false；`/ctl/show` 恢复；`lastCmdCarrier=cdp lastCmdCdpMs=22`；托盘进程已加载 System.Windows.Forms。
## Q. 2026-11-09 二次收口：填沟修法被推翻，改窗口级投影载体（含一条判据作废）

### Q1 我上一轮的错误裁定（留档以免复发）
"把 4 DIP 沟用 wash 填上"能消掉"洞"的观感，但代价是卡片轮廓从 Folia 的 594x176 内缩变成 602x184 外扩，四角出现两层弧错位 —— 用户实拍直接看出"套娃 + 缺角"（D:\ncm-re2\user-shot-double-contour.png）。填色版沟区差分中位数高达 157/152/152/132 luma（沟里被铺满卡片色），这就是双轮廓的量化特征。**该修法已回滚，GutterFill() 方法删除。**

### Q2 正修：窗口级投影载体
新增 shadowHost（602x184、Transparent、IsHitTestVisible=false、DropShadowEffect）+ 不透明 shadowShape（594x176 r18、Margin 4，仅作阴影源，被 cardHost 完全覆盖），入 surface 时位于 cardHost 之下；`cardHost.Effect = null`（其自投影本被自身 bounds 裁掉，留着只会在卡内叠一层内阴影，与 Folia 不符）。参数：ShadowDepth 1 / Direction 270 / Black 0x73 / BlurRadius 6。
BlurRadius 取舍实测依据：3 → 沟内暗化 1.6 DIP 即结束（sd1.5-3 环仅 1 luma）；6 → 该环 14 luma，与 Folia 自身 2..8 DIP 的 5~6 luma 同量级。Direction 实测依据：90 时阴影只在上/左（顶 56.6、底 0.0），270 才向下（底 34、顶 16.2）。

### Q3 一条判据当场作废（我写的）
原判据"gutter 内相邻像素最大跳变 <=4 luma"**不成立且不合理**：阴影在 4 DIP 沟末端被窗口边界硬裁，Folia 的 `0 1px 3px` 同样被裁，因此该阶跃是几何必然。新口径：
- 卡边外侧 **0~1.6 DIP** 区间内不得出现第二个阶跃（这条判双轮廓）；
- 1.6 DIP 之外只允许一个阶跃且必须紧贴窗口边界（<=1.5 px），幅度以 **Folia 同法实测**为上界 —— 待 Folia remote 小窗复开时复核，未复核前记"待定"，不记通过。

### Q4 最终实测（调度侧独立复量，非代理自报）
沟内暗化中位数：上 16.2 / 下 22.3 / 左 30.0 / 右 19.0 luma（下 > 上，符合 offset-y:+1）；四角 45 度扫描全部 monotoneOutward=true，卡边外无第二阶跃；卡内采样 -172 luma（未被阴影污染）；卡片轮廓仍 594x176 内缩、r18 未变（角剖面 vs 修前 |Delta|<=2px，卡片色终点 rr=18.0）。
verify-all -Only V1,V2,V3,V4,V8 = **31 PASS / 0 FAIL / 8 INFO / 0 SKIP**。exe 196096B @ 2026-11-09 19:08:41。

### Q5 仍未验证（诚实清单）
- V7 四角阴影环带**未跑**：需要屏上有 Folia 实例做同几何对照，本轮没有；按 B2 口径该行现在应为 FAIL，不作通过论。
- V5/V6/V9/V10/V11 未跑（本轮只跑了 V1,V2,V3,V4,V8）。
- 编译预算已用满 5 次；实验版源码留档 D:\ncm-re2\WindowUi.{before-fix,final-shadow}.cs。