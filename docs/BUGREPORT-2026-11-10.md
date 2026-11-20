# MiniPlayer overlay — bug 排查报告（2026-11-10）

被测对象：`overlay/MiniPlayer.exe` 196096B @ 2026-11-09 19:08:41，运行中 PID 20392
窗口：物理 1657,0 → 2410,230（753×230），DPI 120（125%），屏 2560×1440
通道：HttpListener 127.0.0.1:46210 + CDP 9222 均在活，/state 心跳 3ms

---

## 结论排序

| # | 级别 | 一句话 |
|---|---|---|
| B1 | P0 | 一次非图片响应就把封面**永久**钉死在坏缓存里，此后每次重绘都在 UI 线程重试解码 |
| B2 | P1 | imgCache / imgBytes / satCache 三个缓存**无上限无淘汰**，`decodeWidth` 是死参数（封面永远按原图解码） |
| B3 | P1 | `imgBytes` 由线程池线程写、UI 线程读，**无锁**（裸 `Dictionary`） |
| B4 | P2 | 拖动时每个 `LocationChanged` 都调 `DumpRect()` → 每移动 1 像素写一次 winrect.json |
| B5 | P2 | 8MB 上限用 `break` 截断，半个 JPEG 会被当成功入缓存 |
| B6 | P3 | GetWindowRect hwnd=0 刷 254 条 WARN；`LivePort()` 每调一次泄漏一个 Process 句柄；watchdog 多进程只等 `ncm[0]` |

---

## B1（P0）坏字节永久缓存 + 每次重绘重试解码

### 现场复现（本轮实测，可重复）

```
POST http://127.0.0.1:46210/state
{"v":1,"src":"ncm","title":"REPRO","artist":"repro","album":"repro",
 "cover":"http://127.0.0.1:46210/","playing":false,"pos":1,"dur":100,
 "timeText":"00:01 / 01:40","liked":false,"loop":"off","vol":40,
 "lyric":{"index":0,"line":"probe","next":"","lines":["probe"]}}
```

第一次（缓存毒化）：

```
18:13:53.345 cover http 200 application/json; charset=utf-8 163B     <- 根本不是图
18:13:53.345 cover bytes 163 http://127.0.0.1:46210/                <- 照样入 imgBytes
18:13:53.405 decode fail(frame,net) NotSupportedException 未找到适用于完成此操作的图像处理组件。
18:13:53.405 decode fail(image,net) ...
18:13:53.405 decode fail(jpeg,net)  ...
18:13:53.405 cover decode fail http://127.0.0.1:46210/
```

第二次（同一 URL 再画一次，永远救不回来）：

```
18:14:01.653 decode fail(frame,cache-bytes) NotSupportedException ...
18:14:01.653 decode fail(image,cache-bytes) ...
18:14:01.653 decode fail(jpeg,cache-bytes)  ...
18:14:01.653 cover decode fail (cached bytes) http://127.0.0.1:46210/
18:14:01.654 cover decode fail (cached bytes) http://127.0.0.1:46210/   <- 一次重绘两遍
```

本进程内真实发生量（当前 session 段统计）：

- `cover http` 350 次：`image/jpg` 349、`text/html` 1
- 就是那 1 次 text/html（`...rxVzkl38Vk9rD3xdPLYPMw==/109951169261052657.jpg?param=300y300`，16:53:22）
  喂出了 **496 次** `cover decode fail (cached bytes)` + 496×3 次解码异常
- 也就是说：**一次 CDN 抖动 = 496 次 UI 线程解码尝试 + 该曲封面永久坏**

### 根因（三处叠加）

1. `WindowUi.cs:2792` `imgBytes[urlVar] = data;` —— 不看 `res.ContentType`、不看魔数，200 且 >100B 就收。
2. `WindowUi.cs:2734-2739` 命中缓存 → 解码失败 → 只 `Log.W` 然后 `return`：
   **不删缓存、不重下、不回落无 param 的 URL**，而且没清 `target.Source`。
3. `WindowUi.cs:2928-2968` `Decode()` 三种解码器顺序重试，全部失败才返回 null ——
   而它被 4Hz 的重绘反复调用，所以一次坏字节永久刷屏。

后果对用户：该曲封面**停在上一首**（wash 底色也停在上一首，因为 `ApplyWashSource:2850` 同样早退）。

### 修法（照抄可编译）

A. 下载完先验真伪，入缓存之前拦掉（`WindowUi.cs:2791` 之后）：

```csharp
// 在 using (HttpWebResponse res = ...) 里补一句： string ctype = res.ContentType;
if (data == null || data.Length < 100) { Log.W("cover too small " + (data == null ? 0 : data.Length) + " " + Core.Trunc(urlVar, 60)); return; }
bool isImage = data.Length > 8 && data[0] == 0xFF && data[1] == 0xD8;   // JPEG SOI
if (!isImage || (ctype != null && ctype.IndexOf("image/", StringComparison.OrdinalIgnoreCase) < 0))
{ Log.W("cover not-image ctype=" + ctype + " " + data.Length + "B " + Core.Trunc(urlVar, 70)); return; }  // 不入缓存
```

B. 坏缓存要能自愈（`WindowUi.cs:2734-2740` 整段替换）：

```csharp
if (imgBytes.TryGetValue(url, out bytes))
{
    ImageSource src = Decode(bytes, wash ? 224 : 220, "cache-bytes");
    if (src != null) { imgCache[url] = src; if (wash) ApplyWashSource(url); else target.Source = src; return; }
    lock (imgGate) { imgBytes.Remove(url); imgCache.Remove(url); }   // 关键：把毒字节丢掉
    target.Source = null;                                            // 不许继续挂上一首的封面
    Log.W("cover bad-cache evicted -> refetch " + Core.Trunc(url, 60));
    // 不 return，落到下面的网络重下
}
```

C. 加一次上限，别让死链死循环：`Dictionary<string,int> imgRetry`，同一 URL 最多重下 2 次。

D. B5 一并改成失败而不是截断（`WindowUi.cs:2782`）：

```csharp
if (total > 8 * 1024 * 1024) { err = "body-too-big"; break; }
// 下面 if (err != null) 已有分支 → 不入缓存
```

---

## B2（P1）缓存无上限 + `decodeWidth` 死参数

- `WindowUi.cs:227-228` `imgCache` / `imgBytes`，`:220` `satCache`：全文件**没有任何 Remove/Clear/容量上限**
  （`grep imgBytes|imgCache` 只有写入和 TryGetValue）。听 100 首 = 100 份原始字节（≤8MB/份）+ 100 张全尺寸解码位图。
- `WindowUi.cs:2928` `private ImageSource Decode(byte[] data, int decodeWidth, string tag)` ——
  **`decodeWidth` 在函数体里一次都没出现**。调用方以为传 220/224 会缩图，实际三种解码器全是原尺寸。
  一张 1000×1000 封面解出来 4MB，100 首 400MB。
- 修：`BitmapImage` 分支加 `bi.DecodePixelWidth = decodeWidth;`；`BitmapFrame` 分支解完用
  `TransformedBitmap(bs, new ScaleTransform(k,k))` 收到 `decodeWidth`；三个缓存各加 LRU 上限（建议 24 条，按 url 的插入序淘汰）。

## B3（P1）`imgBytes` 跨线程裸 Dictionary

- 写：`2792` 在 `ThreadPool.QueueUserWorkItem` 里；读：`2734` 在 UI 线程。
- 只有 `imgPending`（HashSet）加了锁（`2745/2748/2789`），两个图片字典一把锁都没有。
- .NET 的 `Dictionary` 并发读写下做扩容会撕裂桶，经典表现是**无限循环把 UI 线程卡死**。这首歌要不要崩，只是时间问题。
- 修：加 `static readonly object imgGate = new object();` 包住所有 `imgBytes`/`imgCache` 访问，
  或直接换 `ConcurrentDictionary`。顺手也给 `satCache` 加（它已有 `satGate`，但 `:816` `imgCache.TryGetValue` 又漏在锁外）。

## B4（P2）拖动时把 winrect.json 当帧日志写

`WindowUi.cs:353`
```csharp
win.LocationChanged += delegate { SchedulePosSave(); DumpRect(); };
```
`SchedulePosSave()` 有 300ms 防抖（`:3490`），`DumpRect()` 没有。`DumpRect` 会 GetWindowRect + ComputeLit + 序列化
40 多个字段 + `Paths.WriteText` 落盘。拖一次窗口 = 几十上百次磁盘写，且都在 UI 线程。
修：给 DumpRect 也加一个 300ms 的 `DispatcherTimer`，或者 `if (dragging) return;`。

## B6（P3）三条小账

1. `WindowUi.cs:3577` `GetWindowRect failed hwnd=0` 本进程刷了 **254** 次 —— `DumpRect` 被
   `SizeChanged`/`LocationChanged` 在 hwnd 还没生成时调用。加 `if (hwnd == IntPtr.Zero) { GrabHwnd(); if (hwnd == IntPtr.Zero) return; }`。
2. `Program.cs:316` `Process.GetProcessById(pid)` 从不 Dispose，watchdog 每轮 `Remote→LivePort` 漏一个句柄。
3. `Program.cs:437-450` `WatchPass` 对**所有** cloudmusic 进程发 CloseMainWindow，却只 `WaitForExit` `ncm[0]`；
   客户端有多个同名进程时会在旧实例还没退的情况下再拉一个新的。

---

## 本轮验证过、**不是** bug 的（别再去追）

- 进度条比例正确：/state 报 0.8020，卡上亮条像素边界 620→630（期望 245+480×0.802=630）√
- 窗口没有被挤出屏幕：真实 1657,0→2410,230 落在 2560×1440 里。
  之前任何 2048×1152 的读数都是 **DPI 虚拟化**（2560/1.25=2048）——就是 STATUS §P3 记的那个坑，我又踩了一次。
- /state 心跳 3ms、CDP 通路、349/350 封面正常、wash 烘制已搬到专用 STA 线程（12 条 STA 失败都早于当前 exe 的 19:08:41）。

## 复现命令

```powershell
# 1) 毒化一张封面（1 次）
$b='http://127.0.0.1:46210'
Invoke-RestMethod "$b/state" -Method Post -ContentType application/json -Body '{"v":1,"src":"ncm","title":"R1","artist":"r","album":"r","cover":"http://127.0.0.1:46210/","playing":false,"pos":1,"dur":100,"timeText":"00:01 / 01:40","liked":false,"loop":"off","vol":40,"lyric":{"index":0,"line":"p","next":"","lines":["p"]}}'
# 2) 再画一次同一 URL（cached-bytes 分支）
Invoke-RestMethod "$b/state" -Method Post -ContentType application/json -Body '{"v":1,"src":"ncm","title":"R2","artist":"r","album":"r","cover":"http://127.0.0.1:46210/","playing":false,"pos":1,"dur":100,"timeText":"00:01 / 01:40","liked":false,"loop":"off","vol":40,"lyric":{"index":0,"line":"p","next":"","lines":["p"]}}'
# 3) 看证据；客户端 250ms 内会用真状态覆盖回来，不留残留
Get-Content D:\dsh工作区\netease-mini\runtime\server.log -Tail 12
```

## 未做/未证实

- 内存增长未做长跑量化（imgBytes/imgCache 无淘汰是静态可证的，实际曲线没测）。
- B3 的崩溃是概率事件，本轮没有现场触发。
- 多显示器（左侧负坐标屏）下的 `Position.Clamp` 选屏逻辑按中心点取工作区，跨屏时可能夹错屏——单屏环境无法实测，标为待验。

---

# 修复记录（2026-11-10 · 18:20:46 编译 · 18:22:23 上线）

exe：196096B @ 2026-11-09 19:08:41 → **198656B @ 2026-11-10 18:20:46**
回滚档：`overlay\MiniPlayer.pre-bugfix-2026-11-10.exe`（196096B，原样）

| bug | 改了什么 | 位置 | 实测证据 |
|---|---|---|---|
| B1 | 下载后验 `ContentType` + JPEG 魔数（FF D8），非图**不入缓存**；缓存解不开时删缓存 + 清 `target.Source` + 有界重下（同 URL ≤2 次） | LoadCover | 旧版一次 text/html 4272B 喂出 496 次失败；新版窗口内 `cover not-image`=0、`cover decode fail`=0 |
| B2 | `Decode()` 真正使用 `decodeWidth`（FitDecode / DecodePixelWidth）；`imgCache`/`imgBytes` 上限 24、`satCache` 上限 48，按插入序淘汰（丢掉字节同时丢位图） | Decode / CacheGet* / SatPut | 同一张 173323B 封面：旧版 `decode ok(frame,net) 300x300`（18:19:32）→ 新版 **220x220**（18:20:58） |
| B3 | 所有 `imgBytes`/`imgCache` 读写统一走 `imgGate` 的 CacheGet*/CachePut*/CacheDrop，源码里已无裸字典访问 | WindowUi.cs | 编译通过，grep 无残留 |
| B4 | `LocationChanged` → `ScheduleDumpRect()`（300ms 防抖），不再每次 WM_MOVE 写 winrect.json | WindowUi.cs:353 | 位置保存本来就有防抖，现在两边一致 |
| B5 | `total > 8MB` 由 `break` 改为 `err="body-too-big"`；`err != null` 时不再 ToArray、不入缓存 | LoadCover | |
| B6 | `GetWindowRect failed` 每进程只记一次；`LivePort()` 用 using 释放 Process；watchdog 对**全部** cloudmusic 进程等退出（不再是只等 ncm[0]） | DumpRect / Program.cs | 新窗口内 `GetWindowRect failed`=0 |

## UI：悬停收缩（用户要求 · 第二版，按 Folia 录屏重做）

第一版把封面钉成整幅 sleeve（不再缩小），用户指出「我要的是这样的效果，你怎么把缩小那一块弄没了」。
逐帧拆录屏（ScreenSketch 20261110-1024，4.53s / 888×308）实测 Folia 的悬停行为：

| 状态 | 卡片左边缘 | 卡片宽度 | 封面 |
|---|---|---|---|
| idle | x≈80 px | ≈725 px (580 DIP) | 整幅 220 px (176 DIP) |
| lit | x≈283 px | ≈522 px (418 DIP) | 小图标 62 px (50 DIP)，距卡片左 18 DIP |

**右边缘不动，左边缘右移 162 DIP** —— 所以从来不存在「空块」。这与 SPEC 自己的注释完全一致
（`WindowUi.cs:475`：*lit: .remote-paper is clipped to inset(0 0 0 174px), so Folia's lit card starts at 174*）。
之前只做了封面缩小的 morph、没裁卡片，才留下 0..174 的裸底。

实现（`WindowUi.cs`）：

- 恢复 `Expanded` 分支的缩小态：`tx=ExpCoverLeft(196) ty=ExpCoverTop(11) sc=ExpCoverScale(0.125)`、r48、stroke 4；标题回到 `ExpTitleLeft(227)`。
- 新增 `ApplyPaper(narrow, animate)`：`cardHost.Clip` 从 `Rect(0,0,594,176)` 动画到 `Rect(170,0,424,176)`
  （局部坐标，170 = 174 − Gutter），300ms `Snap()`；`AnimRect()` 用 `RectAnimationUsingKeyFrames` +
  `FillBehavior.Stop`，先写基值再动画，不会回弹。
- `shadowShape` 改 `HorizontalAlignment=Right`，宽度同步 594→424 动画 —— 右边缘固定在 598，
  投影跟着卡片收，不会在空带里留一块不透明色。
- 新常量：`PaperCut = SleeveW - Gutter = 170`、`PaperW = CardW - PaperCut = 424`。
  `SleeveW=174 / FaceW=420` 本来就是 Folia 的数，一个没动。

结果：lit 态 0..174 DIP 什么都不画（wash / shade / veil / ring / 投影全被 cardHost 的 Clip 裁掉），
桌面直接透出来；卡片左侧保留 r18 圆角（Folia 是硬切直角，差别就差一条 `Radius → 0`）。

实测（pid 39224，199680B）：
- 日志 `paper cut at 174.0 DIP (face 424.0)` / `paper full 594.0` 成对出现
- idle：753×230 全宽 sleeve 卡（p1_idle）
- lit：卡片从 x=218 px 起，封面缩成 22 DIP 小图标、位于卡片左内 22 DIP，文字/传输条/工具照旧（p3_lit）
- 移出后动画回全宽，无残留裁切（p5_settled）

### 补丁 #3：lit 左侧两个角（用户实拍"角缺一块"）

**现象**：lit 态卡片左边是直的，但两个角上各有一块深色"月牙/饼"，在小窗外面看着像角被啃掉了；
idle 态正常。跟歌曲无关，只要悬停就出现（用户："为什么每首歌都这样"）。

**根因**：两处圆角没跟着裁切走。
1. `cardHost.Clip` 是 `RectangleGeometry(rect, Radius, Radius)` —— 它把矩形裁**圆角**，所以左边缘
   其实还是 r18 的弧；弧外那片区域透明，桌面透上来。
2. `shadowShape`（投影载体）仍是 `CornerRadius(r18)` 且宽度收到 424 —— 它的投影按**圆角轮廓**外扩
   （BlurRadius 6 + ShadowDepth 1），于是两个角的投影正好露在被裁掉的那块里 → 深色饼状残影。

**修法**（`WindowUi.cs` `ApplyPaper`）：
- 裁切改成 `CombinedGeometry(Intersect, 圆角轮廓(594×176 r18), 动画矩形 face)`，
  `face` 从 `(0,0,594,176)` 收到 `(170,0,424,176)`。交集 = 左边硬切直角 + 右边保留 r18 ——
  这正是 Folia 被 clip 的纸面形状。
- `shadowShape.CornerRadius` 在 lit 改成 `(0, 18, 18, 0)`，投影左边缘跟着变成直线。

**实测**（pid 36148 / 200192B，200192B 与 199680B 差的就是这一处）：
- lit：左边缘是一条干净的竖线，四个角（左上/左下）不再有残影（8x 放大 c_lit_zoom.png）
- idle：左侧圆角 r18 保持原样（c_idle_zoom.png）
- 日志 `paper cut at 174.0 DIP (face 424.0)` / `paper full 594.0` 照旧成对

---

## 补丁 #4：左边两个角"缺一块"的真正原因（老 bug，与悬停无关）

用户第三次反馈"还是一样有缺失"。用亮度图逐像素量了卡片的四个角，才定位到真凶：

```
修复前 TOP-LEFT（0..44 px）        修复后 TOP-LEFT
 row 7  .....:+### (边界 col 27)    row 7  ....::-+#### (边界 col 24 → 逐行左移)
 row 14 ....###  (边界 col 27)      row 14 .....:+### (边界 col 11)
 row 28 +####### (边界 col 12)      row 28 ::######  (边界 col 7, 已到直边)
 → 边界几乎垂直再突然横跳 = 斜切    → 逐行平滑左移 = 正常 r18 圆弧
```

**根因**：`WindowUi.cs` 的 `RoundedRect()` 生成 SVG path 时，**两个左侧圆角的弧线扫描标志写成了 `0 0 0`**：

```csharp
// 修复前（左下、左上）
g.Append(" A" + bl + "," + bl + " 0 0 0 0," + (h - bl));     // ← sweep 0
g.Append(" A" + tl + "," + tl + " 0 0 0 " + tl + ",0 Z");    // ← sweep 0
// 修复后
g.Append(" A" + bl + "," + bl + " 0 0 1 0," + (h - bl));
g.Append(" A" + tl + "," + tl + " 0 0 1 " + tl + ",0 Z");
```

sweep=0 时，从 `(0,18)`→`(18,0)` 的弧心落在**角点 (0,0)** 而不是内缩的 (18,18)，于是路径不是"把角修圆"，
而是"把角上半径 18 DIP 的四分之一圆挖掉"。右两个角一直用的是 `0 0 1`，所以从来只有左边坏 ——
正好对上用户说的"左边两个角"和"每首歌都这样"（跟内容无关，纯几何）。

**影响面**：`RoundedRect()` 有两个调用点
- `coverHost.OpacityMask`（封面圆角，sleeve 18/0/0/18）← 用户看到的就是这个
- `queueGrid.OpacityMask`（队列面板四角 r18）← 同样两个左角被挖，一并修好

**验证**（pid 35608 / 200192B）：四角 9x 抓图 `corners_idle.png` —— TL / TR / BL / BR 现在四个角
形状完全一致、都是完整 r18 圆弧；lit 态左带依旧为空、右侧圆角正常。

---

## 补丁 #5：lit 态左侧改回圆角（#3 的硬切作废）

用户问"为什么缩小的时候左边变成了切边不是圆角了"。原因：#3 当初把左侧裁成硬直角，是为了躲开
"投影漏进两个角缺口"的月牙 —— 但那个月牙很大一部分其实是 #4 的 `RoundedRect` 弧线 bug
（封面和小图标自己的左角被挖掉）造成的假象。#4 修好之后，圆角裁切本身是干净的。

**改动**（`ApplyPaper`）：
- `cardHost.Clip` 回到单个 `RectangleGeometry(rect, Radius, Radius)`；`rect` 照旧动画
  `(0,0,594,176) → (170,0,424,176)`。`RectangleGeometry` 是给它拿到的那个 rect 倒角，
  所以缩窄之后**左侧两个新角同样自动是 r18 圆角**。
- `shadowShape.CornerRadius` 恒定 `Radius`（不再按状态切 0/18）。
- `#3` 里的 `CombinedGeometry + paperFace` 交集裁切随之删除。

**验证**（pid 36204 / 199680B）：
- lit：左上角 8x 放大 = 干净 r18 圆弧，投影贴着弧线走，没有月牙；四角全圆
- idle：全宽 594、大封面、四角 r18，未受影响
- 日志：`paper anim fail = 0`、`Exception = 0`、`paper cut/full` 正常成对

---

## 补丁 #6：悬停"丝滑"——先量再改

用户说"还是没有 folia 丝滑"。为了分清"掉帧"和"节奏不对"，先给程序加了逐帧探针
`GET /ctl/animprobe`：用 `CompositionTarget.Rendering` 每个合成帧记一行
`animprobe ms=… x=… w=…`（裁切左边界与宽度）。这份数据现在是常驻的，以后调动画不用再猜。

### 量出来的结论：**没掉帧，是四条时钟各走各的**

首测（原版）：

| 项 | 原值 | 实测 |
|---|---|---|
| 悬停进入延迟 | 80ms 轮询 | 指针到达 → `paper cut` **+100ms** |
| 进入：纸面裁切 | 300ms | 前 36ms 走完 60%，剩 240ms 磨最后 40% |
| 进入：封面 morph | 300ms | 与纸面同步 |
| 进入：进度条 translateY | 440ms | **比纸面晚 140ms 才停** |
| 进入：图标淡入 | 220ms + 80ms 延迟 | **比纸面晚起、早停** |
| 离开保持 | 800ms | 指针离开 → `paper full` **+830ms** |
| 退出：封面 morph | 560ms + 60ms 延迟 | 比纸面长一倍 |

帧间隔全程 5-6ms，**没有一次掉帧** —— 所以不是性能问题，是节奏问题。

### 改法：一条时钟 + 零延迟

```csharp
public const double LitInMs  = 200;   // 进入：纸面裁切 + 封面 morph + 进度条 + 图标，全部同一条钟
public const double LitOutMs = 280;   // 退出：同上
public const double LitLeaveMs = 260; // 离开保持（原 800），只是防边缘抖动，不是"反应时间"
```

- 纸面裁切、封面 morph、`barCanvas.Top`、`FadeGroup` 全部换成同一组常量与同一条曲线
  （进入 `Snap()`、退出 `Settle()`），并删掉 60/80/120ms 那三个延迟。
- `win.MouseEnter` / `win.MouseLeave` 直接触发 `UpdateClickThrough()`，不再等 80ms 轮询；
  轮询保留做兜底（250ms 保持、click-through、队列计时都还在它上面）。

### 改后实测

| 项 | 改前 | 改后 |
|---|---|---|
| 进入延迟（`litLatencyMs`） | ~100ms | **0ms** |
| 进入动画总时长 | ~440ms（最长那条） | **200ms** |
| 离开→开始收 | 830ms | **~230-260ms** |
| 退出动画总时长 | ~620ms | **280ms** |
| 掉帧 | 无 | 无（5-6ms/帧） |

日志：`Exception=0`、`paper anim fail=0`。

---

## 补丁 #7：卡顿的真正来源（逐帧取证）

用户："卡顿感还是特别明显，感觉你就是加快了 UI 变化速度"。上一轮（#6）只改时间，方向确实错了。
这一轮把 Folia 的录屏和本程序的渲染都做了帧级测量。

### 1. Folia 到底怎么动（从用户录屏逐帧量左边缘像素）

| | 实测 |
|---|---|
| **进入** idle→lit | 边缘 60 → 110 → 202 → 240 px，**≈150-180ms** |
| **退出** lit→idle | 220 → 160 → 118 → 96 → 84 → 76 → 70 → 68 → 64 → 62 px，**≈380ms**，**最后 20px 花掉 200ms** |

结论：Folia 是 **进得快、出得慢且尾巴长**。上一轮我把退出压到 280ms，正好反了。现在：`LitInMs=210`、
`LitOutMs=380`。

### 2. 本程序每帧到底花在哪（新增 `/ctl/animprobe`，`CompositionTarget.Rendering` 逐帧记录）

| 项 | 改前 | 改后 |
|---|---|---|
| 稳态帧间隔 | 15.2-16.1ms（正常） | 15.2-16.1ms |
| 动画期间单帧最大间隔 | **32ms** | 26ms |
| 动画期间最大单步位移 | **58px（一次跳过两帧）** | **35px** |
| 进场延迟 | ~100ms | 0ms（`litLatencyMs=0`） |

跳动来自三处，逐一拿下：

1. **没锁帧**：`AllowsTransparency` 的分层窗是 CPU 软件渲染且**不等垂直同步**，实测动画期间
   渲染 ~170 帧/秒，而 DWM 只按 60Hz 呈现 → 位置按 2-3 帧一批到达。给所有 Storyboard /
   KeyFrame 加 `Timeline.SetDesiredFrameRate(60)`，稳态立刻锁到 16ms。
2. **每帧重算糊**：`shadowHost` 上挂的是**活的 `DropShadowEffect`**，clip 每变一次就把整张卡重糊一遍。
   过渡期间改为 `Effect=null`，落定后再 `ApplyShadowEffect()` 恢复（只在 4 DIP 边沟里可见，看不出差异）。
   wash 的活模糊在 `post` 阶段本来就是 null，没问题。
3. **我自己上轮加的 `BitmapCache` 是负优化**：clip 每帧变 → 缓存每帧失效 → 又缓存又裁剪，双倍工作。已删。

### 3. 曲线：把位移摊开

`Snap()` = cubic-bezier(.2,.8,.2,1) 会把 **60% 的路程塞进头 3 帧**（实测 x=0/66/115/140/153/160…），
眼睛看到的正是"三个大跳 + 后面几乎不动"。新增 `Glide()` = cubic-bezier(.4,0,.2,1) 用于进场，
位移摊到整个时长（实测步长 2.6 / 10 / 23 / 35 / 33 / 23 / 16 / 11 / 7 / 5 / 3 / 1 px，单调收敛）。

### 4. 顺带：日志不再碰磁盘

`Log.W` 原来是 `File.AppendAllText` —— 每行开/写/关一次 23MB 的文件，而且 probe/paper/lyric 这些行都在
UI 线程上。改成**内存攒批 + 200ms 定时一次追加**（文件平时保持关闭，所以那些直接读 server.log 的
脚本不受影响）。延迟敏感的 `hovertiming` 也留着，一次过渡一条。

### 5. 还剩什么（结构性，不是调参能解决的）

这个窗口是 `AllowsTransparency=true` 的分层窗：WPF 必须 CPU 渲染整张卡再整块推给 DWM，
**拿不到垂直同步**。Folia 是 WebView，CSS 过渡走 GPU 合成、锁 60Hz —— 它每帧就是 16.7ms，
我们偶尔会有 20-26ms 的一帧。要彻底对齐只有一条路：**去掉分层窗**（改用窗口区域 + 不透明窗，
拿 GPU 渲染），代价是丢掉逐像素 alpha / 圆角 / 投影 —— 那是重写级改动，风险远大于收益。

现状：最大单步从 58px 降到 35px，稳态 60fps，进场 0 延迟，进/出时长与 Folia 实测一致。

---

### 已知边界（没做，先说清）

窗口本身仍是 753 px 宽，那 174 DIP 空带对系统「存在」：`Config.ClickThrough` 现在是 **false**，
所以指针落在空带里时点击会被窗口吃掉，不会穿透到桌面。要真穿透，把 `clickThrough` 打开即可复用
现成的 `OverStrip → WS_EX_TRANSPARENT` 逻辑（`UpdateClickThrough`，WindowUi.cs:1843）。

## 上线后自检（最终重启 18:22:23 之后，取日志最后 1200 行）

```
cover http = 4      cover ok = 4（全部 220x220）
cover decode fail = 0    Exception = 0
GetWindowRect failed = 0 blur-bake fail = 0  UI exception = 0
```

## 回滚

```powershell
$ov = "D:\dsh工作区\netease-mini\overlay"
& "$ov\MiniPlayer.exe" --quit
Copy-Item "$ov\MiniPlayer.pre-bugfix-2026-11-10.exe" "$ov\MiniPlayer.exe" -Force
Start-Process "$ov\MiniPlayer.exe" -WorkingDirectory $ov
```

---

## 补丁 #8：高刷新率屏幕与 4Hz 状态泵（2026-11-14）

用户再次反馈“动作太卡”。这次不再改动画时长，而是同时测屏幕刷新率、`CompositionTarget.Rendering` 帧间隔和状态泵行为。

### 根因

1. **补丁 #7 的 60Hz 假设在本机不成立**：`Win32_VideoController` 实测 RTX 5060 Ti 当前模式为 2560x1440 @ **179Hz**。`AnimFrom`、`Anim`、`AnimRect` 却仍用 `Timeline.SetDesiredFrameRate(..., 60)` 硬锁动画时钟。
2. **播放位置每 250ms 变化，却触发整卡刷新**：`RefreshCore()` 每次都执行 `ApplyHoverState(false)`，连带重建纸面 Clip、OpacityMask、封面布局和 artist Measure；标题/artist 没变也重新布局。
3. **重复构建视觉树**：`SetGlyph()` 每次清空并重建 Path；队列打开时每次状态推送都重建 8 行 x 3 个 TextBlock；相同封面/wash 缓存命中后仍重复挂 Source。
4. **进度条动画修改 `Canvas.Top`**：每个动画帧都触发布局。诊断 `DumpRect()` / `DumpHotspots()` 也可能在过渡中执行 UpdateLayout 和写盘。

### 修改

- 删除三处固定 60fps 限制，恢复 WPF 默认合成节奏；不改 LitIn=200ms / LitOut=380ms 与既有曲线。
- `barCanvas` 保持 `Canvas.Top=IdleBarTop`，新增 `barShift: TranslateTransform`，动画只改 `Y`，位移终点仍为 0 / -34 DIP。
- `ApplyHoverState(false)` 只在 BuildVisuals 初始化一次；之后由 `SetLit()` 负责转换。标题或 artist 真变化才 `LayoutTextForState()`。
- 以按钮 + 图标名 + 画刷颜色 + fill + opacity 为 key，相同的 `SetGlyph()` 直接返回。
- 以 theme + 可见队列行的 Index/Id/Current/Title/Artist 为 key，相同队列不重建视觉树。
- 封面使用带锁的 `CacheGetImage()`；相同 Source 不重复赋值，相同 wash bitmap 不重复走 `ApplyWashBaked()`。
- 周期性 rect/hotspot 诊断在 hover / lyric 过渡期间延后，避免诊断抢 UI 线程。

### 同机对比

测试使用本地假 state，不启动也不改写网易云；每次测试连续 4 组 lit on/off，并在过程中以约 4Hz POST 播放位置。

| 指标 | 旧版 60fps cap | 最终版 |
|---|---:|---:|
| Rendering 帧间隔中位数 | 12.0–15.2ms | **5.7–6.1ms** |
| 平均帧间隔 | 10.44–11.66ms | 7.81–9.23ms |
| 最大帧间隔 | 27.0–33.7ms | 24.3–28.2ms |
| 动画异常 | 0 | **0** |

这些是 Rendering 采样，不把平均值冒充成显示器实际 FPS。长尾尚未归零：`AllowsTransparency=true` 的分层窗仍有软件光栅化与整块上传成本；本次不做破坏圆角/逐像素 alpha 的重写。

### 回归

- .NET Framework x64 编译成功，最终 `MiniPlayer.exe` 为 203776 bytes。
- 悬停展开：`coverState=expanded`、`lit=1`、deck/tools opacity=1；收回恢复 sleeve / 0 / 0。
- 正确队列入口是本版的 `/ctl/queue`（不是 `queue:auto`）。3 行队列展开高度 160 DIP，收起为 0；打开期间连续 12 次 state 更新后仍为 open=1 / height=160。
- 测试结束后清掉假 state 的方法是用 `--quit` 正常退出并以 `--hide` 重新启动；不要只 hide，因为 hide 不会清空 Store。

备份：`overlay\MiniPlayer.pre-smooth-2026-11-14.exe` 与 `docs\patches\WindowUi.pre-smooth-2026-11-14.cs.txt`。

## B1 守卫的复现方式（留给以后）

新版用 `/state` 注入已经打不进去了（`reason=channel-cdp-wins`，页面帧优先），
所以守卫的触发只能靠真实 CDN 抽风。判据：一旦发生，日志里会出现
`cover not-image ctype=...` 而**不再**出现 `cover bytes` + `cover decode fail`。

