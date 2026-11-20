# ncm 小播放器（MiniPlayer）安装说明

最后更新：2026-11-07 16:47（交付与健壮化 brief）；正文里的 15:xx 时间戳一律取自 runtime\server.log 的应用时钟
当前可执行文件：`overlay\MiniPlayer.exe` 164352 字节，mtime 2026-11-07 18:56:44（点击链那轮修复，csc exit=0）
上一版留档（`build.ps1` 现在每次编译前自动生成，见 §7）：
`overlay\MiniPlayer.prev-1856.exe` 164352B（18:56:44）/ `prev-1520.exe` 139264B（15:20:05）/ `prev-1449.exe` 124416B（14:49:26），只保留最近 3 份，改名即可回滚

---

## 1. 这套东西到底装了什么

只写**当前用户**和**本项目目录**。逐项列清，全部可核验：

| 位置 | 内容 | 怎么核验 |
|---|---|---|
| `HKCU:\Software\Classes\ncmmini` | 协议处理器：默认值 `URL:NCM Mini Protocol` + 空 `URL Protocol`；`shell\open\command` = `"D:\dsh工作区\netease-mini\overlay\MiniPlayer.exe" --uri "ncmmini://show"` | `Get-ItemProperty 'HKCU:\Software\Classes\ncmmini\shell\open\command'` |
| `D:\dsh工作区\netease-mini\overlay\` | 程序本体 `MiniPlayer.exe` + 源码 `src\*.cs` + 历史留档 exe | 目录列表 |
| `D:\dsh工作区\netease-mini\bridge\` | 页面脚本 `mini.js`(58607B) / 样式 `mini.css`(1490B) | `Get-FileHash` |
| `D:\dsh工作区\netease-mini\runtime\` | 运行期文件：`port.json` `MiniPlayer.lock` `position.json` `config.json` `winrect.json` `hotspots.json` `server.log` | 见 §5 |
| `D:\dsh工作区\netease-mini\install\backup\` | `orpheus.ntpk.orig`（网易云原包只读留档）+ `body.zip` + `header100.bin` | `Get-FileHash` |

**明确没有做过的事**（2026-11-07 15:44 实测取证）：

- **没有改网易云任何文件。** `D:\CloudMusic\package\orpheus.ntpk` 现场
  `sha256=170413B1675708E87A9E89C2D122A8643A51E86B5B5AE1C6260CFEAF25B19BE4`，`38322062` 字节，mtime 2026-10-20 15:16:30，
  与 `install\backup\orpheus.ntpk.orig` **同一个 sha256、同一个字节数**（逐字节一致）。
  `D:\CloudMusic` 全树近 3 天只有两个文件被写过：`debug.log`(11-07 12:48)、`isc_chat.dat`(11-07 10:12)，都是客户端自己写的。
- **没有改系统文件**：`C:\Windows`、`Program Files` 无写入；`HKLM:\SOFTWARE\Classes\ncmmini` 实测 `Test-Path = False`（协议只写 HKCU）。
- **没有往用户配置文件目录放任何东西**：`%LOCALAPPDATA%` / `%APPDATA%` / `%USERPROFILE%`（深度 2）里 `*mini*`、`MiniPlayer*` 命中数 = 0。
- **没有关闭或重启 cloudmusic / Folia**，也没有点过网易云界面任何一下。

## 2. 当前生效路径 = CDP 连页面 + overlay 自己分发 bridge（闭环，无需外部临时服务）

链路：网易云带 `--remote-debugging-port=9222 --disable-background-timer-throttling
--disable-backgrounding-occluded-windows` 启动 → `MiniPlayer` 的 `CdpClient` 连上
`ws://127.0.0.1:9222/devtools/page/<id>`（页面 `orpheus://orpheus/pub/app.html`）→ overlay **先自注入**：
`Runtime.evaluate` 在页面里加载 `http://localhost:<自己的端口>/mini.js`（文件由 overlay 自己的 HTTP 路由
`GET /mini.js` 从 `bridge\mini.js` **按原始字节**送出，sha256 与磁盘完全一致）→ 等 `window.__NCM_MINI__`
出现（实测 ≤1.2s）→ 再做端口交接（`port.json` 经 CDP 递给页面并 `connect()`）→ 页面把状态 POST 回
`http://127.0.0.1:46210/state`，并取走小窗下发的命令队列执行。页面 reload / bridge 被回收后，overlay 每 3s
自检一次 `__NCM_MINI__`，发现缺失就重新分发（实测 1.7-3.7s 自愈），因此**不再需要任何外部临时 HTTP 服务**。

注入方式实测：app.html 的 `meta` CSP 为 `script-src 'self' 'unsafe-inline' 'unsafe-eval' … http://localhost
http://localhost:2000 … orpheus:`，而 Chrome/CEF **拒绝** `<script src="http://localhost:46210/mini.js">`
（日志原文：`Refused to load the script … violates … script-src`）。所以自注入用「fetch/XHR 取本文件 +
间接 eval」这条被同一条 CSP 允许的路径（`unsafe-eval` + `connect-src http:`），脚本标签只是首选尝试、
失败即回退；`/info` 的 `bridgeInject` 字段会写明本次是哪条路成功：
`read-null :: fetch-eval status=200 bytes=56485 bridge=up ms=1079 -> up (+wait 0ms) via http://localhost:46210/mini.js`。

取证（`runtime\server.log`）：

    15:34:51.122 cdp connected ws://127.0.0.1:9222/devtools/page/54FC233186A79E3D7CC32A502CA85533 page=orpheus://orpheus/pub/app.html
    15:37:30.339 handoff page 0 -> 46210 :: null (file={"port":46210,"pid":36832,...})
    15:35:58.495 POST /state accepted=true changed=true reason=changed frames=1 bytes=7867 reply={}

`GET /info` 里能对上：`backend=HttpListener`、`cdpConnected=1`、`cdpEvals`，且 `bridgeBuild` 与
`pageBuildProbe` 相同 —— 页面里跑的确实是这份脚本。

## 3. 为什么不走"包内注入"（这条路已实测封死）

`orpheus.ntpk` 有完整性校验。`install\build-package.ps1` 能产出把 `mini.js` 打进包里的
`orpheus.ntpk.new`(38276192B)，但**替换后网易云直接拒绝启动**（实测：换包 → 客户端起不来 → 立刻从
`backup\orpheus.ntpk.orig` 还原）。旧 `install.ps1` 走的就是这条路，所以现在：

- 生效的是 §2 的运行时注入（overlay 自分发 + 自注入），包文件保持原厂（§1 的 sha256 为证）；
- **改包这条路已被网易云完整性校验封死，任何情况下不得再试**（不要再产 `orpheus.ntpk.new`、不要再换包、
  不要为此重启网易云）；换包 = 客户端拒绝启动，只能从 `backup\orpheus.ntpk.orig` 还原；
- `install.ps1` / `build-package.ps1` 只是历史归档，**不要再执行**；
- `uninstall.ps1` 已改写为「校验优先」：只哈希比对，不再往 `D:\CloudMusic` 写任何东西，也不再 `Stop-Process cloudmusic`。

## 4. 启动 / 停止

自动拉起闭环 = 下面**四**件事的合取，缺一不可（前三条已实测，第四条**还缺**，2026-11-07 20:48~20:50 取证）：
1. **用户级启动入口**（本轮已建，机器级原图标一个字没动）：两处「网易云音乐（小窗版）」.lnk ——
   `%APPDATA%\Microsoft\Windows\Start Menu\Programs\` 与 `%USERPROFILE%\Desktop\`，实测回读原值：

       TargetPath=[D:\CloudMusic\cloudmusic.exe]
       Arguments=[--remote-debugging-port=9222 --remote-debugging-address=127.0.0.1
                  --disable-background-timer-throttling --disable-backgrounding-occluded-windows]
       WorkingDirectory=[D:\CloudMusic]   IconLocation=[D:\CloudMusic\cloudmusic.exe,0]
       两份 .lnk 各 999B，mtime 2026-11-07 20:48:29，args/target/icon 回读比对 = True/True/True

   用它启动一次网易云后 CDP 真的开着：`20:49:02.519` 由 .lnk 拉起（cloudmusic pid=40380），
   `20:49:05.108` `GET http://127.0.0.1:9222/json/version` 应答 `Browser=Chrome/91.0.4472.169`、
   `User-Agent=… NeteaseMusicDesktop/3.1.42.205594`、`webSocketDebuggerUrl=ws://127.0.0.1:9222/devtools/browser/ac8809d6-…`；
   对照：`Stop-Process cloudmusic` 之后同一请求是「无法连接到远程服务器」。
   后两个 flag 是页面在后台仍按 ~250ms 节拍轮询 `/state` 的前提，缺了小窗就只能在 CDP 兜底路径上走。
2. **overlay 自注入**（§2）：`MiniPlayer.exe` 冷启动连上 CDP 后自己把 `bridge\mini.js` 挂到页面，
   不依赖任何外部临时 HTTP 服务；实测 `/info` = `cdpConnected=1`、`pageBuildProbe=ncmmini-bridge-2fa9287e-057384`
   == `bridgeBuild`、`bridgeInject=cold :: fetch-eval status=200 bytes=56485 bridge=up ms=1080`。
3. **`ncmmini://` 协议已注册在 HKCU**（`HKCU:\Software\Classes\ncmmini`，`MiniPlayer.exe --register`；
   实测 handler = `"D:\dsh工作区\netease-mini\overlay\MiniPlayer.exe" --uri "ncmmini://show"`）。
4. **还缺的一环：没人负责把 overlay 自己拉起来。** 冷启动对照实测（不手工启动 MiniPlayer）：
   `20:48:59.735 --quit`（pid 41772 退干净、port.json/lock 已无）→ `20:49:00.468 cloudmusic 全退` →
   `20:49:02.519` 用上面那份 .lnk 启动网易云 → CDP 9222 于 `20:49:05` 应答 → 之后 `20:49:31~20:50:46`
   每 5s 采样 15 次：`MiniPlayer=0` 进程、`port.json=False`、`lock=False`、`runtime\server.log` 字节数
   冻结在 10080063（没有任何进程试图启动）→ **小窗没有被自动拉起**，此时只有空闲态灰卡。
   根因（代码位置）：页面里唯一能发起 `ncmmini://start` 的是 bridge 的 `selfStart()`（`bridge\mini.js:668-700`，
   `location.href`/隐藏 iframe 两条路），而这份 bridge 本身要靠 overlay 经 CDP 注入（`overlay\src\Channels.cs:652
   data-ncmmini="overlay-self-inject"`）；overlay 不在场 = 页面里没有 bridge = 没有谁会去点那根协议。
   `overlay\src\*.cs` 里也扫不到任何 `CurrentVersion\Run` / 启动文件夹 / autostart 钩子（0 命中）。
   最小修法（按代价排序，都要父 agent 拍板；本轮只写文档，没动源码也没自建第三个入口）：
   (a) 再加一个用户级开机自启入口 `%APPDATA%\Microsoft\Windows\Start Menu\Programs\Startup\MiniPlayer（小窗常驻）.lnk`
       → `MiniPlayer.exe --hide`（隐藏冷启动已实测不闪现，见下一段引文），只写用户目录、不碰 HKLM/ProgramData；
   (b) overlay 侧加冷启动自举（`--watch` 之外新增"CDP 在、我没在"的自拉起），需要改源码 → 派单；
   (c) 让 `--watch` 守护常驻（它会重启网易云，代价最大，README §4 现有实测禁止项）。

### 如何启动（口径写死，别再靠手工带参数）

| 打开方式 | 结果 |
|---|---|
| 原图标 `C:\ProgramData\Start Menu\Programs\网易云音乐.lnk`（target=`D:\CloudMusic\cloudmusic.exe`，args 空，本轮未动） | 没有 9222 → overlay 连不上 CDP → 不自注入 → **小窗只有空闲态灰卡**，要手工 `--toggle`/`--state` 才有内容 |
| 「网易云音乐（小窗版）」（用户级 Start Menu + 桌面，本轮新建）+ overlay **已在跑**（常驻或手工起过一次） | **全自动**：CDP 9222 在 → overlay 自注入 → 页面 ~250ms 推 `/state` → 小窗出真实歌曲/歌词/队列 |
| 「网易云音乐（小窗版）」+ overlay **没在跑** | 只有网易云自己起来，小窗不会被拉起（= 上面第 4 条缺环）；先手工 `& $MP` 一次 |

所以今天的正确用户口径是：**先把 overlay 跑起来（常驻修法见上第 4 条 (a)），之后一律用「小窗版」入口打开网易云**；
用原图标打开 = 只有空闲态。


    $MP = 'D:\dsh工作区\netease-mini\overlay\MiniPlayer.exe'
    & $MP                       # 冷启动，窗口显示
    & $MP --hide                # 冷启动且全程不显示（本轮修好的写法，见下）
    & $MP --toggle              # 显示 / 隐藏
    & $MP --quit                # 停止
    ncmmini://show              # 从浏览器或其它程序唤起
    & $MP --info ; & $MP --state ; & $MP --rect ; & $MP --ports
    & $MP --watch --dry --once  # D3 守护"只判断不动手"单轮：不启动、不关闭、不隐藏任何东西
    & $MP --unwatch             # 停掉守护

> **本轮修掉的一个坑**：`--hide` 作为第一个参数时，旧代码无条件当成"给已运行实例发指令"，
> 于是没有实例在跑时 `MiniPlayer.exe --hide` 只打印一句 `no running MiniPlayer` 就退出，压根起不来；
> 想冷启动隐藏就只能裸启动 → 窗口必然闪现（这正是本轮观察到 `visible=1` 的来源之一）。
> 现在 `Program.cs:69`：`first == "--hide" && LivePort() == 0` 时落到服务端路径，隐藏冷启动。
> 日志证据：`15:39:53.304 cold start hidden (--hide with no live instance)`，随后同一进程的
> `dbg-pointer` 报 `renderedSeen=False rectSource=derived windowVisible=false` —— 全程没渲染过一帧。

只读调试接口（隐藏态可查、不需要前台、不碰输入）：

| 接口 | 用途 |
|---|---|
| `GET /ctl/dbg-pointer` | 指针/矩形/lit 判定链即时自述：`rectSource`、`lit` / `litApplied` / `litReason`、`dpi` / `physPerDip`、`probeBudgetLeft` |
| `GET /ctl/dbg-layout` | 20 个节点的 `boundsDip` + `hit` / `actualOpacity` / `targetOpacity` / `effectiveOpacity` / `visibility`，另给 `dpi` / `physPerDip` / `originPhysX,Y`。点击屏幕坐标 = `originPhys + boundsDip * physPerDip` |
| `GET /ctl/dbg-probe-reset[?budget=N]` | 复位探测预算，长测前打一发 |
| `GET /ctl/lit:on\|off\|auto` | 强制 / 还原 lit（`auto` 现在立刻按当前指针重算） |
| `GET /state` `GET /info` `GET /hotspots` `POST /cmd` `POST /click` | 状态、健康、热点、下发命令 |
| `GET /ctl/hide\|show\|top\|theme\|queue\|taskbar\|rect\|quit` | 窗口动作 |

## 5. 出问题先看哪个文件

1. **`runtime\server.log`** —— 唯一事实来源。关键字：
   `cdp connected`（通道是否活着）、`POST /state accepted=`（页面有没有在推状态）、
   `cmd enqueue`（小窗有没有下发）、`queue auto-collapse accepted=true ... since=`（6s 收起判定）、
   `probe cur=... rect=... inside=... lit=... hover=... cover=...`（80ms 判定链每一步）、
   `lit rejudge`（`/ctl/lit:*` 的重算结果）。
2. **`GET /ctl/dbg-pointer`** —— 判"为什么没亮 / 没展开"，看 `litReason`：`hidden` / `clickThrough` /
   `idle` / `pointerOut` / `override=1` 一句话定位卡在哪一环。
3. **`GET /ctl/dbg-layout`** —— 判"点不到 / 点错地方"，看 `geometryUsable` 和每个 key 的 `hit`、`boundsDip`。
4. **`runtime\winrect.json`** —— 1s 一次的快照，适合看趋势；**隐藏态下 `dumpTimer` 是停的，文件会陈旧**
   （尤其 DPI 被游戏改过之后）。要新值就查 `dbg-pointer`（每次调用都重解析 DPI）。本轮起
   `DumpRect` 也不再等首次渲染才解析 DPI，所以只要它被调用一次，文件里的 `dpi/physPerDip` 就是当下的。
5. 端口/进程对不上：`runtime\port.json`（`port/pid/backend/at`）与 `MiniPlayer.lock`，或 `& $MP --ports`。

## 6. 已知限制（本轮如实记账）

- `GET /mini.js` / `GET /mini.css` **已实现并实测**（sha256 与磁盘逐字节一致，`OPTIONS` 204 + `ACAO: *`），
  自动拉起链路因此闭环；`verify-all.ps1` 的 V8/V10 现在是真的判据（V10 会把页面 bridge 抹掉，再看 overlay
  自己重新分发并把真实歌曲数据接回来）。
- 页面 **真 reload 未做**（要打断你正在听的歌，需你点头才跑）；等价路径已实测：抹掉 `window.__NCM_MINI__` +
  删掉注入过的 script 标签 = 「页面上没有 bridge」这个 reload 后的状态，overlay 在 1.7-3.7s 内自愈。
- 交互类验收（真鼠标进卡、十键、拖拽 seek、wash 实拍、四角、三态封面）现由 `verify-all.ps1` 一次跑完，
  输出在 `D:\ncm-re2\verify-out\run-5.txt` 与 `rows.tsv`；判定的可复核性说明见 `docs\STATUS.md` §G。
- `--watch` 守护**只实现 + 静态自检，没有实际运行过**（它会重启网易云，本轮禁止）。
- 交互类验收（真鼠标进卡、十点、拖拽、wash 实拍、四角透明）本轮**没跑**，都在
  `D:\ncm-re2\verify-all.ps1` 里，桌面空闲时一条命令跑完并打表。
- 一切尺寸判据必须**运行时**读 `physPerDip`：本轮实测桌面在 2560x1440@125%（dpi=120、physPerDip=1.25、
  卡 753x230）与 1920x1440@100%（dpi=96、physPerDip=1、卡 602x184）之间被游戏来回切过，写死任何一边的数字都会错。

---

## 7. 如何回滚（先 DryRun，再覆盖，再验一次）

留档机制：`overlay\build.ps1` 在调用 csc **之前**把当前 `MiniPlayer.exe` 复制成
`overlay\MiniPlayer.prev-<HHmm>.exe`（`<HHmm>` 取的是**被替换那个 exe 自己的 mtime**，也就是它当时的
构建时刻，和 prev-1449 / prev-1520 的既有命名一致），并按 mtime 倒序只保留最近 **5** 份（原来是 3；3 份的滚动在 wash 标定那几轮里把 prev-1856——点击链验收那版——挤掉了，见 docs\STATUS.md §K8），多出来的直接删。
`MiniPlayer.old.exe` 不在 `prev-*` 过滤范围内，任何情况下都不会被这个逻辑删掉。
只想跑留档、不想编译：`$ArchiveOnly = $true; (Get-Content -Raw overlay\build.ps1) | Invoke-Expression`
（实测输出见 docs\STATUS.md §I）。

**只读验收底档（keep 滚动也丢不了）**：`$PinAccepted = $true` 时 `build.ps1` 会把当前 exe 钉成
`install\backup\MiniPlayer.accepted-<HHmm>.exe` 并置 `IsReadOnly=True`，同时打印 sha256。
当前钉的是**通过验收 run-17（60 PASS / 0 FAIL）的那一版**：

| 文件 | 字节 | 源 mtime | IsReadOnly | SHA256 |
|---|---|---|---|---|
| `install\backup\MiniPlayer.accepted-2019.exe` | 167424 | 2026-11-07 20:19:39 | True | `602AD21A30CFBCCEE98D01E0E2A453647E2EF37B8CE4C49B286B557693D2B72F` |

只读位只挡对这个文件本身的写入，**复制它是允许的**，回滚不需要先解除只读：
`Copy-Item install\backup\MiniPlayer.accepted-2019.exe overlay\MiniPlayer.exe -Force`。
钉底本身不自动发生：只有过了验收 run 的那版才该钉（`$PinAccepted = $true` 手动带上）。

**回滚三步（顺序不能换）：**

1. **先看会动什么**：`powershell -NoProfile -ExecutionPolicy Bypass -File install\uninstall.ps1 -DryRun`
   —— DryRun 只打印计划（进程、HKCU 键、注入文件、端口），一个字节都不动。
2. **再覆盖**：把要回到的那份 prev（或上面那份只读 accepted 底档）复制成当前 exe，然后重启实例：
   `Copy-Item overlay\MiniPlayer.prev-1520.exe overlay\MiniPlayer.exe -Force` →
   `& overlay\MiniPlayer.exe --quit` → `Start-Process overlay\MiniPlayer.exe`。
   覆盖前 `MiniPlayer.exe` 本身也会被这次构建/留档逻辑另存，所以回滚本身不会丢当前版本。
3. **再验一次**：`powershell -NoProfile -ExecutionPolicy Bypass -File D:\ncm-re2\verify-all.ps1`
   （桌面空闲时跑；它会动真鼠标并显示卡片）。判定口径见 docs\STATUS.md §G/§I。

真要把安装期改动整体撤掉：`install\uninstall.ps1`（不带 `-DryRun`）；网易云原包 `orpheus.ntpk` 的还原
从 `install\backup\orpheus.ntpk.orig` 按 §3 的说明做（sha256 逐字节一致才叫还原成功）。

> 留档缺口：17:40 → 18:56 那次覆盖直接替换了 exe、没留 prev 档，回滚链在那一步是断的（当时能拿到的
> 只有 15:20 的 prev 和 12:01 的 old）。本节和 §1 的 prev 列表就是补上这个口子后重新建立的。
