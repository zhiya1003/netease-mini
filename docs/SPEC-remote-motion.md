# SPEC-remote-motion — Folia Remote Control 行为 / 动画 / 取色 逆向规格（B run1）

记法：`js@N` = app.asar:/dist/assets/bootstrap-BJsYUc2U.js 第 N 个字符起（构建 0.7.24-adf8621）；`css:L` = folia-re/remote.css 第 L 行（= /dist/assets/bootstrap-DwFEGzvL.css 内全部 .remote-* 规则原文，逐条换行）；`main:N` = extract/electron/main.cjs 行号（Select-String）；`geo:N` = extract/electron/remoteControlWindowGeometry.cjs 行号；`set:N` = extract/electron/remoteControlWindowSettings.cjs 行号。

本文只补 SNAPSHOT-MODEL.md / SPEC-folia-remote.md 没答的：形态判定、动画时序、歌词换行、wash 取色、交互细节、变量与时长汇总。静态几何不重复推导，只做换算。

---

## Q1 形态判定：两态由什么驱动

### 1.1 判定表达式（根组件 ZMe js@1770916；data-* 装配点 js@1772525）

原文（js@1772525，逐字，含前后 200 字符内的完整属性串）：

    (0,Z.jsxs)("main",{className:"remote-window","data-theme":n.isDaylight?"light":"dark","data-bg":c,
    "data-lit":O&&!j,"data-idle":j,"data-sleeve":M,"data-paused":n.hasTrack&&!m,"data-sheet":k.isOpen,
    "data-notitle":r.hideTitlebar||D,"data-queue":A.direction??void 0,"data-shifting":A.isShifting,
    onFocus:e=>s(e.target.matches(":focus-visible")),onBlur:()=>s(!1),children:[...

驱动变量（js@1771145..1772520 原文）：

    m=n.playerState===W.PLAYING
    D=r.clickThrough, O=!D&&(a||o)
    k=IMe({exportStatus:_.status,isExportBusy:v,pointerInside:i,isDisplayOnly:D})
    A=gMe({queueLength:n.queue?.length??0,pointerInside:i,isDisplayOnly:D,isSheetOpen:k.isOpen})
    j=zMe(!O&&!k.isOpen&&!A.isOpen,t,d)
    M=j&&r.expandCover
    N=c==="transparent"&&r.textShade
    return WMe(N,f,p,M)

- `i = Ije(r.clickThrough)`（js@1726946）= 主进程指针态；`a = Lje(i, YMe)`（js@1727510，`YMe=800` js@1770764）：进入立即 true，离开延迟 800ms。Lje 原文：`Lje=(e,t)=>{let[n,r]=useState(e);useEffect(()=>{if(e){r(!0);return}let n=window.setTimeout(()=>r(!1),t);return()=>clearTimeout(n)},[e,t]);return e||n}`
- `o` = 键盘焦点可见态（onFocus/onBlur js@1772560），并有复位 `(!k.isOpen||!A.isShown)&&!document.activeElement?.matches(":focus-visible")&&s(!1)`（js@1772700）。
- `D`（clickThrough）为 true 时 `O` 恒 false → 永不 lit。
- `t = zn("uiMicroMotion")`（js@1770930）= 减少动效开关；`d` = `.remote-card` 元素 ref（js@1772900）。

主进程判定（main:5323-5352）：

    const REMOTE_CONTROL_POINTER_POLL_INTERVAL_MS = 80;                      // main:5323
    function isCursorInsideRemoteControlWindow() {                            // main:5328
      if (!win || win.isDestroyed() || !win.isVisible() || win.isMinimized() || remoteControlClickThroughEnabled) return false;
      const bounds = win.getBounds(); const cursor = screen.getCursorScreenPoint();
      return cursor.x >= bounds.x && cursor.x < bounds.x + bounds.width && cursor.y >= bounds.y && cursor.y < bounds.y + bounds.height;
    }
    remoteControlWindow.webContents.send("remote-control-pointer-inside", inside);   // main:5350

Wayland：`getRemoteControlPointerInside` 返回 null（main:7579-7581）→ 渲染层退回本地 `mousemove/mouseleave`（Ije js@1726946 原文：`let r=!0,i=!1,a,o=t(e=>{i=!0,n(e)});window.electron?.getRemoteControlPointerInside?.().then(t=>{r&&(t===null?a=e():i||n(!!t))},()=>void 0)`）。

### 1.2 两态映射（用户口径 → 代码状态）

| 用户叫法 | 代码条件 | 关键 data-* |
|---|---|---|
| 缩小态（176 大封面 sleeve + 标题行 + 大字歌词 + 副行 + 底部细进度条） | `j===true` 且 `r.expandCover===true` → `M=true`；此时 `O=false` | `data-idle=true data-sleeve=true data-lit=false` |
| 放大态（封面缩成小圆角图进标题行、右上 palette/pin/close、进度游标、时间+7 图标） | `O=true`（指针进入或 focus-visible）→ `j=false` → `M=false` | `data-idle=false data-sleeve=false data-lit=true` |
| 过渡带 | `O` 已 true 而 `j` 尚未翻转 / CSS 过渡未完 | 两态属性并存 ≤560ms |

`playing` 不参与形态判定，只驱动 `data-paused`（js@1772589）与 `--remote-sung/--remote-unsung` 覆写（css:94）。歌词有无是正交维度：`Xje mode:E?"line":"card"`（js@1773300，`E=T?null:C.line`），落到 `.remote-song[data-mode]`（css:145/146）。

### 1.3 .remote-window 上全部 data-* 及取值来源

| 属性 | 取值 | 来源 | 位置 |
|---|---|---|---|
| data-theme | light / dark | `n.isDaylight`（快照字段 27） | js@1772525 |
| data-bg | default / cover / transparent | `c=useState(XMe)`，XMe 读 localStorage `remote_background_mode` | js@1770772 / js@1772680 |
| data-lit | true/false | `O&&!j` | js@1772555 |
| data-idle | true/false | `zMe(!O&&!k.isOpen&&!A.isOpen,t,d)` | js@1772566 |
| data-sleeve | true/false | `j&&r.expandCover` | js@1772578 |
| data-paused | true/false | `n.hasTrack&&!m` | js@1772589 |
| data-sheet | true/false | `k.isOpen`（IMe js@1766306） | js@1772606 |
| data-notitle | true/false | `r.hideTitlebar||D` | js@1772620 |
| data-queue | down / up / 缺省 | `A.direction??void 0`（gMe js@1750861） | js@1772645 |
| data-shifting | true/false | `A.isShifting` | js@1772660 |

子树 data-*：`.remote-song[data-mode]`（js@1737200）、`.remote-lyric[data-shown]`（js@1746520）、`.remote-lyric__pair[data-dim]`、`.remote-lyric__hero[data-size]`、`.remote-lyric__sub[data-kind][data-lines]`、`[data-remote-shade]`（js@1745700 区）、`.remote-deck__slot[data-covered]`（js@1739700）、`.remote-preview[data-shown]`（js@1739800）、`.remote-key--small[data-on][data-liked]`（js@1741900/1742100）、`.remote-queue-row[data-current][data-queue-row]`（js@1749200）、`.remote-cloud[data-empty]`（js@1773100）。

---

## Q2 形变动画

结论：**形态切换 100% 由 CSS transition 驱动，不用 AnimatePresence、不用 requestAnimationFrame**。framer-motion（`aa` ← /proxy-teCSiYPw.js；`Ua` = AnimatePresence ← /AnimatePresence-D_Ukms2A.js）只用在三处：handoff 面（h7 / Xje）、队列面板（bMe）、Sheet（wMe）。rAF 只用于时钟 OMe（js@1763944）与底衬量算 WMe（js@1768072）。

| # | 元素/属性 | 放大 → 缩小 | 时长 | 缓动 | 延迟 | 证据 |
|---|---|---|---|---|---|---|
| 1 | .remote-paper clip-path | `inset(0 0 0 174px round 18px)` → `inset(0 round 18px)` | .3s / .56s | snap / settle | 0 / 60ms | css:103, css:104 |
| 2 | .remote-ring left | `inset:0 0 0 174px` → `left:0` | .3s / .56s | snap / settle | 0 / 60ms | css:108, css:109 |
| 3 | .remote-cover transform | `translate(196px,11px)scale(.125)` → `none` | .3s / .56s | snap / settle | 0 / 60ms | css:128, css:132 |
| 4 | .remote-cover border-radius | 48px → `18px 0 0 18px`（card 态 34.57px） | 同上 | 同上 | 同上 | css:128/130/132 |
| 5 | .remote-cover:after inset 描边 | 4px → .5px（card 1.57px），色 #ffffff24 | 同上 | 同上 | 同上 | css:129/131/133 |
| 6 | .remote-song__row / __cardtext translate | 0 → -31px / -72px | .3s / .56s | snap / settle | 0 / 60ms | css:148-151 |
| 7 | .remote-line transform | none → translateY(34px) | .24s / .44s | snap / settle | 0 / 120ms | css:182, css:183 |
| 8 | .remote-deck opacity+transform | 1,none → 0,translateY(6px) | 进 .22s / 出 .18s | snap / ease-out | 80ms / 0 | css:197, css:198 |
| 9 | .remote-lit（tools 容器、时间槽、键位槽、游标） | 0 ↔ 1 | 进 .22s / 出 .32s / idle 出 .18s | snap / — / ease-out | 80ms | css:122-124 |
| 10 | .remote-glass | 1 → 0 | .14s / .32s | ease-out / — | 0 | css:115, css:116 |
| 11 | .remote-cloud | 1 → 0 | .14s / .32s | ease-out / — | 0 | css:114, css:118 |
| 12 | .remote-cover__image filter | 无 → `brightness(.78) saturate(.75)`（idle+paused） | .32s | — | 0 | css:137, css:138 |
| 13 | .remote-cover__blank font-size | 88px → 76px | .42s | cubic-bezier(.2,.8,.2,1) | 0 | css:139/140 |
| 14 | line ↔ card 模式互换 | opacity 0↔1 + `translate(41px,30px)scale(1.5)` ↔ `translate(-41px,-30px)scale(.6)` | opacity .22s(+140ms) / .14s；transform .42s | — / cubic-bezier(.2,.8,.2,1) | 140ms | css:141/145/146 |
| 15 | .remote-lyric opacity（data-shown） | 1 ↔ 0 | 进 .26s(+140ms) / 出 .18s | — | 140ms | css:161/162 |
| 16 | 减少动效下的 idle 翻转 | 整卡 WAAPI 淡出→翻转→淡入 | 90ms + 140ms | ease-in / ease-out | 0 | js@1767541 |
| 17 | 队列换高期间 | 整窗 opacity 0 | 90ms | ease-out | 0 | css:91 |
| 18 | 全局减少动效 | 所有 transition duration/delay 归零 | 0s | — | — | css:228 |

zMe 原文（js@1767541）：

    LMe=90,RMe=140,zMe=(e,t,n)=>{let[r,i]=useState(e);useEffect(()=>{let a=n.current;if(e===r)return;
    if(!t||!a){i(e);return}let o=a.animate({opacity:0},{duration:LMe,easing:"ease-in",fill:"forwards"});
    return o.onfinish=()=>i(e),()=>o.cancel()},[e,r,t,n]); ... t&&n.current?.animate({opacity:[0,1]},{duration:RMe,easing:"ease-out"}) ...}

即：**只有开启减少动效时才用不透明度过渡掩盖状态翻转**（因为 css:228 把所有 transition 时长清零）；正常情况直接靠 CSS 过渡形变。

卡片高度：**两态之间不变**。窗口固定 602x184，只有队列展开改高度（main:5385-5388 注释 "this window's size is fixed (resizable: false, min === max; only showing the queue changes it)"；geo:149-153）。

---

## Q3 歌词交叉淡入

### 3.1 DOM 结构（sMe js@1746196 → oMe js@1744879 → b7 js@1744540）

    .remote-lyric[data-shown]                       css:161（absolute top:40 left/right:22）
    └─ motion.div .remote-lyric__pair[data-dim]     key = startTime-endTime-fullText（y7 js@1744540）
       ├─ div .remote-lyric__hero .remote-lyric__measure[aria-hidden]   隐藏测量层（css:168 visibility:hidden）
       ├─ div .remote-lyric__hero[data-size][data-remote-shade]         可见主行
       │   └─ span .remote-lyric__word / .remote-lyric__gap / .remote-lyric__whole
       ├─ div .remote-lyric__roma[data-remote-shade]                    可选
       └─ div .remote-lyric__sub[data-kind][data-lines][data-remote-shade]  可选

外层 `.remote-recede`（Yje js@1736925，css:160）在 handoff 期间给整块歌词加 opacity + blur：`qje=8; Jje=e=>{let t=Math.round((1-e)*qje*4)/4;return t>0?"blur("+t+"px)":"none"}`（js@1736900）→ 最大 8px，步进 .25px。

### 3.2 换行时序（只有一个 pair 元素，靠 key 重挂载）

sMe 原文（js@1746196..1746700）：

    let[c,l]=useState(()=>e?{line:e,nextLine:t,isSinging:n}:null),
        u=!!(c&&e&&y7(c.line)===y7(e)), d=!!c&&!u;
    (u&&(c.line!==e||c.nextLine!==t||c.isSinging!==n)||!c&&e)&&l({line:e,nextLine:t,isSinging:n});
    useEffect(()=>{if(!d)return;let e=window.setTimeout(()=>l(f.current),(o?0:v7*1e3)+aMe);return()=>clearTimeout(e)},[d,o]);
    let h=o?0:6;
    <div className="remote-lyric" data-shown={!!e}>{c&&<motion.div className="remote-lyric__pair" data-dim={!c.isSinging}
      initial={p.current?{opacity:0,y:h}:false}
      animate={ d ? {opacity:0,y:-h,transition:{duration:o?0:v7,ease:"easeIn"}}
                  : {opacity:1,y:0,transition:{duration:o?0:.26,ease:[.2,.8,.2,1]}} }
      key={y7(c.line)}><oMe/></motion.div>}</div>

| 阶段 | 时长 | 属性 | 缓动 |
|---|---|---|---|
| 旧行离场 | 180ms（`v7=.18` js@1744515） | opacity 1→0，y 0→-6 | easeIn |
| 落地等待 | 20ms（`aMe=20` js@1744522） | 无动画 | — |
| 新行入场 | 260ms | opacity 0→1，y +6→0 | cubic-bezier(.2,.8,.2,1) |
| 合计 | 460ms | | |

触发条件：**按歌词行身份变化**（key = startTime+endTime+fullText），不是纯时间轴。`O7`（js@1765707）用 `Jl(lines, clock.getTime()-lyricOffsetSec)` 求当前行；`PMe`（js@1766011）订阅时钟，仅当 `line/nextLine/isSinging` 变化才 setState。时钟 tick 由 `OMe`（js@1763944）在 playing 时的 rAF 循环驱动。

### 3.3 实拍"旧行与新行半透明叠在一起"的两条已证实代码路径

1. **主行 + 下一句同屏**：`$je`（js@1742970）在 `subtitleContentMode==="next"` 或无译文时返回 `{romanization:null,second:{text:下一句,kind:"next"}}` → `.remote-lyric__sub[data-kind=next]` 用 `--remote-next:rgba(ink,.3)`（css:176 + css:90），主行用 `--remote-unsung:rgba(ink,.4)`（css:166），两行流式相距 4px（css:174 margin-top:4px）。屏幕上同时存在两条半透明歌词行。
2. **离场/入场重叠窗口**：`d===true` 的 180ms 内旧行 opacity 在 0..1 之间；换 key 后新行以 `initial{opacity:0,y:6}` 起 260ms 淡入，整段 460ms 内元素带 `filter`（`.remote-recede` 的 blur）与 `opacity` 合成层，截帧可见半透明残字。

代码中不存在第三条路径（同时挂载两个 pair）：检索词 `remote-lyric__pair` 在 bootstrap 命中 1（js@1746560，裸 motion.div，无 AnimatePresence 包裹）；检索词 `remote-lyric` 命中 2（js@1746520、js@1773300）；`Ua`（AnimatePresence）在 remote 渲染树只出现 2 次（js@1774300 sheet、js@1776400 queue）+ h7/Xje 内部各 1 次。

### 3.4 逐字填色（与换行动画独立）

    // oMe js@1744879
    let a=n.endTime-n.startTime,
        s=n.timed&&a>0?Math.round(Math.max(0,Math.min(1,(t-n.startTime)/a))*1e3)/10 : (t>=n.startTime?100:0);
    e[r]!==s&&(e[r]=s,i.style.setProperty("--remote-fill",s+"%"));

    /* css:170 */
    .remote-lyric__word,.remote-lyric__gap{color:#0000;
      background-image:linear-gradient(90deg, var(--remote-sung) calc(var(--remote-fill,0%) * 1.16 - 16%), var(--remote-unsung) calc(var(--remote-fill,0%) * 1.16));
      -webkit-background-clip:text;background-clip:text}

每字一条 16% 宽软边扫过（1.16 倍行程）；无逐字时间戳时整行 0/100 硬切（`timed:false`，nMe js@1743665 把 words[] 归并成带时段的 token，并把一个 word 的时间按字符长度比例摊给子段）。

### 3.5 可照抄实现要点（H4）

- 层数：容器 1 + 行块 1（key 变即重建）+ 主行 2（隐藏测量层 + 可见层）+ 罗马音 1 + 副行 1 = 最多 5 层。
- 关键帧：离场 `{1,0}→{0,-6}` 180ms easeIn；入场 `{0,+6}→{1,0}` 260ms cubic-bezier(.2,.8,.2,1)；中间 20ms 空档。
- 无 scale、无 blur（blur 只在 handoff 外层 recede，0→8px，按 1-opacity 线性，步进 .25px）。
- 触发：行身份变化；跟唱态用 `data-dim` 切 sung/unsung（css:164）。
- 主行档位：`mainLinesAtLarge=round(hero.offsetHeight/26)`（`rMe=26` js@1744496）→ >2 行时 `data-size=small`（17/22，css:167）；副行行数 `eMe`（js@1743472）算出 `secondMaxLines∈{0,1,2}` → `data-lines`。

---

## Q4 wash 背景取色

### 4.1 结论：小窗不取色、不量化、不建调色板

实拍的"渐变"= **封面原图被非对称外扩拉伸 + blur(38px) + saturate(1.2) + 一层竖向黑遮罩**。没有采样、没有色标数组。

证据链：
1. 装配点 js@1772891：`<div className="remote-paper">{c==="cover" && <><h7 handoff={w} imageClassName="remote-wash" reduceMotion={t}/><div className="remote-wash-shade"/></>}</div>` → 只有 `data-bg=cover` 才渲染 wash。
2. h7（js@1736298）：每个 handoff 面渲染 `m7`，`backgroundImage:f7(r.coverUrl)`；`f7=e=>e?"url("+JSON.stringify(e)+")":void 0`（js@1735846）。background-image 直接是封面 URL，无中间处理。
3. m7（js@1736094）：`<motion.div className={n} style={{backgroundImage:i,opacity:o}}/>`，`o=p7(e,t)` 为 handoff 透明度插值（`l7=.5` js@1734428）。
4. CSS：

    .remote-wash{filter:blur(38px)saturate(1.2);background-size:100% 100%;position:absolute;inset:-70.4px -168px -70.4px 6px}   /* css:112 */
    .remote-wash-shade{background:linear-gradient(#09090d75 0%,#09090db3 100%);position:absolute;inset:0}                        /* css:113 */

5. 检索命中数（bootstrap-BJsYUc2U.js）：`getImageData`=0、`createLinearGradient`=0、`quantize`=0、`palette`（remote 区 js@1720000..1780000）=0；main.cjs：`dominantColor`=0、`quantize`=0、`palette`=3（main:3228 "command palette"、main:4497/4514 是 AI 歌词排版提示词，均与 remote 无关）。
6. 主进程/worker 的 `analysis/*`（htdemucs、mnn、beat_this、packedWeights，见 extract/electron 目录清单）只处理音频分轨/BPM；`remote` 与 `mnn|htdemucs|beat` 同行检索 main.cjs 命中 0。

### 4.2 可复现伪代码（输入封面 → 输出屏幕像素）

    # 卡片坐标系 594x176，(0,0)=卡片左上
    wash_box = { left:6, top:-70.4, right:594+168, bottom:176+70.4 }     # css:112 inset
    wash_w   = 756        # 左只让出 6px，右溢出 168px
    wash_h   = 246.4      # 上下各溢出 70.4px
    for P in card_pixels:
        u = (P.x - 6) / 756
        v = (P.y + 70.4) / 246.4
        C = cover(u*176, v*176)                 # background-size:100% 100% → 直接拉伸，不保持宽高比
        C = gaussian_blur(C, sigma = 38 CSSpx)  # 等效封面空间 sigma_u=8.85, sigma_v=21.11
        C = saturate(C, 1.2)
        a = (0x75 + (P.y/176)*(0xB3-0x75))/255  # css:113 竖向遮罩 alpha 0.4588→0.7020
        out = C*(1-a) + [9,9,13]*a              # #09090d
    # 可见范围再被 .remote-paper 的 clip-path(inset(0 0 0 174px|0 round 18px)) 裁剪
    # 放大态左侧 174px strip 由 .remote-cover 本体盖住

"斜向"来源：inset 左 6px / 右 -168px 使水平映射压缩到 756px，模糊在盒内各向同性 → 换算回封面空间 sigma 水平 8.85、垂直 21.11，封面右上亮区被抹向卡片右上，形成对角趋势。

### 4.3 真实数值样例（对 folia-re/folia-753.png 逐像素验证）

该图 753x230 px = 602x184 DIP @1.2508。实测卡片边界：行扫描 y=90 → 内容 x 6..598；列扫描 x=560 → 内容 y 4..179；进度亮行 y=162..165，亮列 x=201..571（与 §5.1 预测一致）。

按 §4.2 公式（±3σ 高斯核，封面取 sleeve 区 176x176 像素）：

| 卡片坐标 | 实拍 | 模型预测 | Δ(R,G,B) |
|---|---|---|---|
| (556,4) | #3e5131 | #394432 | +5,+13,-1 |
| (556,86) | #374127 | #283123 | +15,+16,+4 |
| (556,164) | #4b3218 | #3c2b1d | +15,+7,-5 |
| (196,4) | #30441f | #28351f | +8,+15,+0 |
| (196,164) | #4f3013 | #3d2615 | +18,+10,-2 |
| (296,86) | #42452a | #32372a | +16,+14,+0 |

用户采样 #3f5132 → #4b361b 与实测 #3e5131（x=560,y=8）/ #4b3218（x=560,y=168）同点同值（±1 为取样位置差）。色相、明度阶、方向全部复现；残差为 R/G 系统性 +5..+18（模型偏暗），来自 Blink 模糊的低分辨率近似与 filter 色彩空间（sRGB 与 linearRGB 两种 saturate 都跑过，差 ≤3/255，不足以解释残差，主因是模糊核半径近似）。

### 4.4 与 data-bg / data-theme 的组合

| 组合 | 卡片底 | 墨色 | 证据 |
|---|---|---|---|
| default + dark | linear-gradient(#18181c,#0f0f12) | ink=255,255,255 | css:105 / css:90 |
| default + light | linear-gradient(#fbfaf8,#f3f2ee) | ink=17,17,22 | css:106 / css:92 |
| cover + 任意 | #0d0d10 + wash + shade | **强制** ink=255,255,255（忽略 isDaylight） | css:107 + css:93 |
| transparent + 任意 | .remote-glass #0d0d11cc + inset 1px #ffffff1a，.remote-ring{display:none} | 强制深墨 | css:115/111/93 |
| transparent + idle | 追加 drop-shadow(0 0 .6px #000000e6)drop-shadow(0 1px 2px #0000008c) 到 song/lyric/roma/sub/transport；unsung #ffffff9e、second #ffffffe6、next #ffffff9e | | css:121 / css:119 |
| transparent + idle + paused | sung #ffffffc7、unsung #ffffff80 | | css:120 |
| 任意 + paused | sung rgba(ink,.62)、unsung rgba(ink,.3) | | css:94 |
| 队列（cover） | #0d0d10 | | css:11 |

背景模式循环（js@1774900）：`onCycleBackgroundMode:()=>l(e=>e==="default"?"cover":e==="cover"?"transparent":"default")`；持久化在渲染层 localStorage（js@1772680 `window.localStorage.setItem(j7,c)`，`j7="remote_background_mode"` js@1770736），**主进程无对应 store 键**。

---

## Q5 版式表（H2：两态绝对坐标，基准 602x184 DIP）

派生链：`.remote-card{width:594px;height:176px;margin:-88px 0 0 -297px;top:50%;left:50%}`（css:99）→ 卡片原点 (4,4)；`.remote-face{width:420px;height:176px;top:0;left:174px}`（css:102）→ face 原点 (178,4)；`.remote-cover{width:176px;height:176px;top:0;left:-174px;transform-origin:0 0}`（css:128）→ sleeve 态画在窗口 x=4..180（左侧 174px strip）。

### 5.1 缩小态（data-idle=true data-sleeve=true data-lit=false，mode=line）

| 元素 | x | y | w | h | 排版 |
|---|---|---|---|---|---|
| .remote-cover (sleeve) | 4 | 4 | 176 | 176 | transform:none；radius 18 0 0 18；inset 描边 .5px #ffffff24 |
| .remote-song__row | 200 | 15 | ≤257 | 22 | translate:-31px；11.5px/22px；nowrap；width:fit-content |
| ├ .remote-song__title | 200 | 15 | auto | 22 | 600 / rgba(ink,.84) / ellipsis |
| └ .remote-song__artist | 208 | 15 | ≤110 | 22 | 400 / rgba(ink,.46) / margin-left 8 |
| .remote-lyric 块 | 200 | 44 | 376 | 流式 | top:40 left/right:22（css:161） |
| ├ .remote-lyric__hero | 200 | 44 | 376 | 26 或 52 | 20/26 600 ls -.012em clamp2；色 var(--remote-unsung)=rgba(ink,.4)；small 档 17/22 |
| ├ .remote-lyric__roma | 200 | 44+heroH+1 | 376 | 15 | 11/15 / rgba(ink,.44) / ls .02em / 单行 ellipsis |
| └ .remote-lyric__sub | 200 | 44+heroH(+roma?16:0)+4 | 376 | 19 或 38 | 13/19 400 / rgba(ink,.64)；data-kind=next → rgba(ink,.3)；data-lines=1 → 单行 ellipsis |
| .remote-line 盒 | 200 | 156 | 376 | 16 | top:118 + translateY(34)（css:183） |
| ├ .remote-line__track | 200 | 163 | 376 | 2 | rgba(ink,.11)，radius 2 |
| ├ .remote-line__fill | 200 | 163 | 376×p | 2 | rgba(ink,.58) |
| └ .remote-line__knob | 200+376p−4.5 | 164−4.5 | 9 | 9 | 带 remote-lit → idle 下 opacity 0 |
| .remote-deck | 178 | 131 | 420 | 49 | opacity 0；translateY(6px)；pointer-events:none |
| .remote-tools | 506 | 14 | 76 | 24 | 容器 remote-lit → opacity 0 |
| .remote-ring | 4 | 4 | 594 | 176 | sleeve 态 left:0 |

实测校验（folia-753.png）：亮行 y=162..165 vs 预测 163..165 ✓；亮列 x=201..571 vs 预测 200..576（571 = 进度 98.6%）✓。

### 5.2 放大态（data-idle=false data-lit=true，mode=line）

| 元素 | x | y | w | h | 排版 |
|---|---|---|---|---|---|
| .remote-cover (thumb) | 200 | 15 | **22** | 22 | translate(196,11) scale(.125)；radius 48px→视觉 6px；inset 4px→视觉 .5px |
| .remote-song__row | 231 | 15 | ≤257（notitle 345） | 22 | translate:0；11.5/22 |
| ├ title | 231 | 15 | auto | 22 | 600 rgba(ink,.84) |
| └ artist | 239+titleW | 15 | ≤110 | 22 | 400 rgba(ink,.46) |
| .remote-lyric 块 | 200 | 44 | 376 | 流式 | 同 5.1（lit 时主行色 = unsung .4，逐字 sung） |
| .remote-line 盒 | 200 | 122 | 376 | 16 | translateY(0) |
| ├ .remote-line__track | 200 | 128.5 | 376 | 3 | lit：rgba(ink,.16)，h3，top 6.5 |
| ├ .remote-line__fill | 200 | 128.5 | 376×p | 3 | lit：rgb(ink) |
| └ .remote-line__knob | 200+376p−4.5 | 125.5 | 9 | 9 | top:50% margin −4.5；opacity 1（.22s snap +80ms） |
| .remote-deck | 178 | 131 | 420 | 49 | opacity 1 |
| .remote-deck__slot--time | 200 | 149.5（中心 155） | auto | 11 | 11px/500 rgba(ink,.78)；i → rgba(ink,.38) |
| .remote-transport | 324 | 137 | 128 | 36 | gap 14；left:50% + translate(-50%,-50%)，中心 (388,155) |
| ├ prev | 324 | 139 | 32 | 32 | svg 17 skip-back |
| ├ play/pause | 370 | 137 | 36 | 36 | svg 22 play / 21 pause |
| └ next | 420 | 139 | 32 | 32 | svg 17 skip-forward |
| .remote-deck__slot--keys | 464 | 141 | 118 | 28 | 4×28，gap 2，right:16 |
| ├ like | 464 | 141 | 28 | 28 | heart 15；liked → fill=currentColor + 色 #ff5d73 |
| ├ loop | 494 | 141 | 28 | 28 | repeat-off / repeat-1 / repeat 15 + 3×3 dot |
| ├ queue | 524 | 141 | 28 | 28 | list-music 15 |
| └ more | 554 | 141 | 28 | 28 | ellipsis 16 + 导出中 dot #ff4d5e |
| .remote-tools | 506 | 14 | 76 | 24 | palette / pin(-off) / x，各 24，gap 2，svg 13 |
| .remote-ring | 178 | 4 | 420 | 176 | inset 左 174 |

### 5.3 放大态变体 mode=card（无当前歌词行：无词 / 纯音乐 / handoff incoming 主导）

| 元素 | x | y | w | h | 排版 |
|---|---|---|---|---|---|
| .remote-cover | 200 | 39 | 56 | 56 | translate(196,35) scale(.318182)，radius 34.57→视觉 11 |
| .remote-song__card | 272 | 39 | 304 | 56 | top:35 left:94 right:22（css:144） |
| .remote-song__cardtitle | 272 | 42 | 304 | 52 | 20/26 600 -.012em clamp2 rgb(ink)（css:158） |
| .remote-song__cardartist | 272 | 67 | 304 | 19 | 13/19 400 rgba(ink,.58) margin-top 2（css:159） |

### 5.4 两态之间变化项（含时长/缓动，见 Q2 全表）

变化项：.remote-cover（transform/radius/box-shadow）、.remote-paper（clip-path）、.remote-ring（left）、.remote-song__row/__cardtext（translate）、.remote-line（translateY）、.remote-line__track/__fill（top/height/background）、.remote-lit 组（opacity：tools、时间槽、键位槽、游标）、.remote-deck（opacity+translateY）、.remote-glass/.remote-cloud（opacity）、.remote-cover__image（filter）、.remote-cover__blank（font-size）。不变项：卡片尺寸、歌词块位置、face 布局。

---

## Q6 进度条交互

DOM（lMe js@1747292，原文）：

    <div className="remote-line" style={e7}>                     // e7={WebkitAppRegion:"no-drag"} js@1725127
      <input ref aria-label={seekLabel} type="range" min={0} max={t||1} step={1} defaultValue={0}
             disabled={u} className="remote-line__input"
             onPointerDown={()=>{c.current=!0}}
             onPointerUp={()=>{c.current=!1;d()}}
             onPointerCancel={f} onLostPointerCapture={p} onBlur={p}
             onChange={t=>{e.previewSeek(Number(t.currentTarget.value));
                           c.current||(clearTimeout(l.current),l.current=setTimeout(d,cMe))}}
             onKeyUp={()=>{c.current||d()}}/>
      <div className="remote-line__track"><div className="remote-line__fill"/>
        {r&&<div aria-hidden className="remote-line__glow remote-progress-transition-glow"/>}</div>
      <div className="remote-line__knob remote-lit"/>
    </div>

- 轨道：默认 h2 / top7 / rgba(ink,.11) / radius2；lit 时 h3 / top6.5 / rgba(ink,.16)（css:184/193）。已播 fill 默认 rgba(ink,.58)，lit 时 rgb(ink)（css:185/194）。transparent+idle：track #fff6 + `0 0 0 1px #00000061,0 1px 5px #00000073`，fill #fff（css:195/196）。
- 游标：9×9 圆，`left:var(--remote-progress)`、`top:50%;margin:-4.5px 0 0 -4.5px`、色 rgb(ink)、`pointer-events:none`（css:187/188）。**没有 hover 增长**：只有 disabled 时 `display:none`（css:191）与 remote-lit 淡入淡出（.22s snap +80ms 进 / .32s 出）。
- 热区：`.remote-line__input{opacity:0;cursor:pointer;appearance:none;position:absolute;inset:0;width:100%;height:100%}`（css:189）→ 整条 376×16 可点/可拖（idle 时 y 156..172，lit 时 122..138）。focus-visible 时轨道加 `1.5px solid rgba(ink,.7)` 外描边（css:192）。
- 指针→seek 换算：`--remote-progress = (i/r*100).toFixed(2)+"%"`（每帧，js@1747350）；input value 为**整秒**（step 1，max=duration）。拖拽：pointerdown 置 dragging → `clock.previewSeek(sec)` 只改本地 `getShownTime()`（OMe js@1764100）→ pointerup 调 `releaseSeek()` 取回预览值 → `onSeek` → `$5({type:"seek",time})`（js@1775200）。点击/键盘（非拖拽）：150ms 防抖提交（`cMe=150` js@1747285）；松手后 800ms 内不用快照时间覆盖预览（`DMe=800` js@1763930）。
- 时间文本显隐：`.remote-deck__slot--time` 内两个 span 由 Qje 用 `textContent` 直写（js@1738900）：`i=g7(r>0?Math.min(t.getShownTime(),r):0)`，`a=" / "+g7(r)`，`g7=e=>Math.floor(e/60)+":"+String(Math.floor(e)%60).padStart(2,"0")`（js@1739400）。显隐四条件：① 所在 `.remote-lit` 需 `data-lit=true`（css:123）；② `.remote-deck` 需 `data-idle=false`（css:198）；③ `data-covered=true`（hover prev 且有上一首标题，或 hover like 且有 provider 提示）→ opacity 0（css:217，js@1739700）；④ clickThrough 时整个 deck 不渲染（js@1775240 `!D&&<Qje/>`）。
- handoff 发光：`isTransitionGlowActive`（Kje js@1734434）为真时插入 `.remote-line__glow.remote-progress-transition-glow`，动画 `2.4s ease-in-out infinite`，keyframes `0%,to{opacity:.5;box-shadow:0 0 4px 0 var(--transition-glow-color),0 0 9px 0 ...}` / `50%{opacity:1;box-shadow:0 0 7px 1px ...,0 0 16px 3px ...}`（/dist/assets/src-BNDXyq_f.css@211373）；减少动效时固定 `opacity:.85;box-shadow:0 0 6px 0 ...`。

---

## Q7 图标行与 tools

### 7.1 图标行（Qje js@1738795）

结构：`.remote-deck > .remote-lit(时间槽+prev预览) + .remote-transport(prev/play/next) + .remote-lit(键位槽+next预览)` → 共 7 颗图标 + 时间。

| 按钮 | 类名 | 盒 | svg | 字形 | disabled 条件 | 反馈 |
|---|---|---|---|---|---|---|
| prev | remote-key remote-key--skip | 32×32 @324,139 | 17 | skip-back（fill=currentColor） | `primaryDisabled\|\|!canGoPrevious` | hover bg rgba(ink,.12)+rgb(ink) .14s；active scale(.94) .12s；disabled opacity .3（css:201/202/203） |
| play/pause | remote-key remote-key--play | 36×36 @370,137 | 22 / 21 | play / pause | `primaryDisabled` | 同上 |
| next | remote-key remote-key--skip | 32×32 @420,139 | 17 | skip-forward | `primaryDisabled\|\|!canGoNext` | 同上 |
| like | remote-key remote-key--small | 28×28 @464,141 | 15 | heart（liked 时 fill=currentColor） | `primaryDisabled\|\|canLike===false` | `data-liked=true` → #ff5d73（含 hover，css:211）；外层 `.remote-key-rest` 的 disabled 子元素 pointer-events:none（css:213）；不可收藏时显示 tooltip 文案 `status.providerLikeUnavailable` 覆盖时间槽（js@1739600） |
| loop | remote-key remote-key--small | 28×28 @494,141 | 15 | 见 7.2 | `primaryDisabled` | `data-on=true` → rgba(ink,.96)（css:210）+ 3×3 指示点（css:214） |
| queue | remote-key remote-key--small | 28×28 @524,141 | 15 | list-music | 无 disabled 属性 | `aria-expanded` + `data-on={isOpen}` |
| more | remote-key remote-key--small | 28×28 @554,141 | 16 | ellipsis | 无 disabled 属性 | `data-on={sheet.isOpen}`；导出中且 sheet 未开 → `.remote-key__dot--live` #ff4d5e（css:215） |

transport 内 svg 默认 opacity .72（.32s）；lit 或 paused → .95（.14s ease-out）；hover 或 transparent 背景 → 1（css:206/207/208）。键位槽 gap 2、right 16；transport gap 14。

### 7.2 loop 四态图标切换

    // js@1742100
    let v=um(e.loopMode);
    children:[<v size={15}/>, e.loopMode!=="off" && <span className="remote-key__dot"/>]
    "aria-label": l("remote."+lm(e.loopMode)), "aria-pressed": e.loopMode!=="off", "data-on": e.loopMode!=="off"
    onClick: ()=>$5({type:"cycle-loop-mode"})

`um`/`lm` 由 /dist/assets/playerControlSlotActions-COD4Vrdw.js 导出（bootstrap import 别名 `{r as um, i as lm}`），原文（该文件 @2890）：

    q=e=>e==="off"?s:e==="one"?c:l        // s=repeat-off, c=repeat-1, l=repeat
    J=e=>e==="off"?"loopOff":e==="one"?"loopOne":"loopAll"

即 **4 个 loopMode 用 3 个字形**：off→repeat-off、one→repeat-1、list 与 shuffle 同为 repeat；区分靠 `data-on`（颜色）+ 3×3 圆点 + aria-label（loopAll 覆盖 list/shuffle）。切换 = React 组件标识变化（svg 整颗重挂载），无独立动画（只有 svg opacity .32s/.14s 与底色 .14s）。主窗口侧另有 3 连点触发 shuffle 提示的旁路（该文件 @1943：`N="folia_loop_shuffle_hint_count",P=3,F=1500,I=2,L=8e3`）。

### 7.3 「更多(⋯)」的内容：不是菜单，是 Sheet

`k.toggle()`（IMe js@1766306）→ `tab = isExportBusy?"export":"window"`。Sheet（wMe js@1759081）：

- 顶部：返回键 `.remote-sheet__back`（24×24，`top:10px;left:13px` → 窗口 (191,14)，svg 15）+ `.remote-sheet__tabs`（12px/22px 600，gap 16，`top:11px;left:42px` → 窗口 (220,15)）。
- tab 清单（原文 js@1759400）：`window`（`remote.tabMainWindow`）、`export`（`remote.tabExport`）、`mods`（`remote.tabMods`，仅当 `snapshot.remoteActions.items.length>0`）。
- `window` tab 行（js@1761300）：① seg standard/transparent → `set-transparent-mode-enabled`；② 尺寸按钮 → 进入 `size` 子视图（Vje，预设 + `resize-main-window`）；③ seg always-visible/auto-hide/always-hidden → `set-player-chrome-visibility-mode`；④ 三个 switch：`remote.border` → `set-main-window-border-visible`、`remote.through` → `set-main-window-click-through`（disabled 当非 transparent）、`remote.pin` → `set-main-window-always-on-top`（disabled 当 !alwaysOnTopSupported）。
- `mods` tab：`SMe`（js@1757460）把 `snapshot.remoteActions.items` 渲染成 `.remote-actions`（h32，`top:46px;left:22px;right:22px` → 窗口 (200,50)）内一排 32×32 圆钮；图标异步取（`CMe` → `k_(name,{size:15,strokeWidth:2})`）；`tone==="alert"` → #f87171（css:89）、`pressed` → `.remote-action--on`（css:88）、`disabled` → opacity .35（css:87）；count 角标仅 hover/focus-visible 显形（9px，深色底）。动作项结构（remoteActions-D1sT1sc_.js@136991）：`{id,order,epoch,label,icon,disabled,pressed,count,tone:"normal"|"alert",scope:"track"|"session",run}`；点击 → `$5({type:"remote-action",epoch,handle,trackKey})`（xMe js@1757423）。
- 自动收起：sheet 打开且非指针进入、非导出忙、无文本输入焦点 → 6000ms 关闭（`FMe=6e3` js@1766300）；Esc 关闭（js@1766500）；导出状态从 idle 变为非 idle 时自动打开 export tab。

### 7.4 tools 三颗（EMe js@1762942）

    <div className="remote-tools remote-lit" style={e7}>
      <button aria-label={t(TMe[e])} className="remote-tool" onClick={onCycleBackgroundMode}><palette size=13/></button>
      <button aria-label={s} aria-pressed={o} disabled={!t} className="remote-tool"
         onClick={()=>{let e=!i;a(e);window.electron?.setRemoteControlAlwaysOnTop?.(e).then(e=>a(!!e)).catch(()=>a(!e))}}>
         {o?<pin size=13/>:<pin-off size=13/>}</button>
      <button aria-label={t("remote.close")} className="remote-tool"
         onClick={()=>window.electron?.closeRemoteControl?.()}><x size=13/></button>
    </div>

| tool | 改什么 | 落地 |
|---|---|---|
| palette | 背景模式循环 default→cover→transparent→default（标签 `TMe={default:"remote.backgroundDefault",cover:"remote.backgroundCover",transparent:"remote.backgroundTransparent"}` js@1762827） | 仅渲染层 useState + `localStorage["remote_background_mode"]`（js@1774900 / js@1772680）；主进程无 store 键 |
| pin | 小窗置顶 | `setRemoteControlAlwaysOnTop` → IPC `remote-control-set-always-on-top`（main:7549-7564）→ `store.set("REMOTE_CONTROL_ALWAYS_ON_TOP", v)`（main:2597/7559）+ `applyRemoteControlAlwaysOnTop`；`!windowAlwaysOnTopSupported` 时返回 false 且按钮 disabled（title=remote.pinUnavailable） |
| close | 关窗 | `closeRemoteControl()` → IPC `remote-control-close`（main:7520-7529）→ `remoteControlWindow.close()` |

盒模型：24×24 圆，gap 2，`top:10px;right:16px`（face 坐标）→ 窗口 x 506..582 / y 14..38；色 rgba(ink,.46)，hover → bg rgba(ink,.12)+rgb(ink)，disabled opacity .45（css:177-181，各 .14s ease-out）。整块带 `remote-lit` → 非 lit 时 opacity 0；`data-notitle=true`（hideTitlebar 或 clickThrough）时 `display:none`（css:178）。

---

## Q8 队列展开

### 8.1 与形态/悬停的关系

- 开关：queue 键 `onClick:s` → `A.toggle()`（gMe 内 `S=useCallback(()=>a(e=>!e),[])` js@1752300）。与 hover 无耦合：`pointerInside` 只参与 `O`（lit）与自动收起计时。
- 显示：`isShown = v && u!==null`（v = isOpen && !isDisplayOnly；u = 已生效 expansion）；根组件 `${A.isShown&&A.direction&&<bMe .../>}`（js@1776450）。
- 自动收起：`v && !pointerInside && !搜索聚焦` 持续 6000ms（`fMe=6e3` js@1750380）→ 关闭；Esc 两级退出（有查询词先清词，否则关队列，js@1751900）。
- clickThrough 强制关：`useEffect(()=>{n&&a(!1)},[n])`（js@1751100）。

### 8.2 方向选择与高度（补 A 的公式之外的判定）

主进程（geo:127-142）：

    room.down = workArea.y+workArea.height-(bounds.y+bounds.height);  room.up = bounds.y-workArea.y;
    least = Math.min(wanted, REMOTE_CONTROL_QUEUE_MIN_HEIGHT);
    side = preferred && room[preferred]>=least ? preferred
         : room.down>=wanted ? "down" : room.up>=wanted ? "up" : room.up>room.down ? "up" : "down";
    height = Math.min(wanted, Math.max(least, room[side]));

wanted（渲染层，js@1750417）：`mMe=e=>46+Math.max(1,Math.min(e,8))*36+C7`，`C7=6` → 8 行时 340；`panelHeight=height-C7=46+36k`；窗口高 = `184+expansion.height`（geo:150）。CSS：`.remote-queue{width:420px;left:calc(50% - 123px);padding-bottom:6px;border-radius:18px}`（css:7）→ 队列 x = 4+297-123 = 178..598；`[data-queue=down] .remote-queue{top:186px}` + `.remote-card{margin-top:0;top:4px}`（css:8/100）；`up` → `bottom:186px` + `bottom:4px`（css:9/101）。

### 8.3 换高时序（gMe 主 effect js@1751150..1752200）

    h(!0)                                   // isShifting=true → 整窗 opacity 0 / 90ms（css:91）
    await T7(x7)                            // x7=90ms（js@1750399）
    await w7(); await w7()                  // 两个 rAF（w7=()=>new Promise(e=>requestAnimationFrame(()=>e()))）
    const n = await setRemoteControlQueueExpansion(...)
    await hMe(n)                            // 轮询直到 window.innerHeight===n（±1）
    g.current===token && h(!1)              // isShifting=false

分支：
- 首次展开：先 `setExpansion(y,{preview:true})` 只取方向，再 2 个 rAF，再 `setExpansion(y,{direction})` 正式落地。
- `direction==="up"`：以 `S7+height`（S7=184）作为"卡片留在原位"的期望内高一次性换高。
- `down` 且新高度更小：先本地 `p({direction:"down",height:y})`，等 `T7(pMe)`（pMe=220ms）再落主进程。
- 收起：`up` → `T7(200-x7/2)=155ms` → 换高；`down` → `T7(200)` → `setExpansion(null)`。
- 挂载/卸载时 `setRemoteControlQueueExpansion(null)` 复位（js@1751050）。

### 8.4 面板动画（bMe js@1753726）

    const _Me=[.32,.72,0,1], vMe=[.4,0,.7,.2], yMe=4;
    E7=(e,t)=>{let n=t?t+yMe:-4;
      return e==="down" ? "inset(-4px -4px "+n+"px -4px round 22px)"
                        : "inset("+n+"px -4px -4px -4px round 22px)";}
    <motion.section className="remote-queue" style={{height:t,...e7}}
      initial={l?{opacity:0}:{opacity:.4,clipPath:E7(e,t),y:e==="down"?-8:8}}
      animate={{opacity:1,clipPath:E7(e,0),y:0,transition:{duration:l?0:.34,ease:_Me}}}
      exit={l?{opacity:0,transition:{duration:0}}
              :{opacity:0,clipPath:E7(e,t),y:e==="down"?-6:6,transition:{duration:.2,ease:vMe}}}>

- 进：340ms cubic-bezier(.32,.72,0,1)，opacity .4→1、y ∓8→0、clip 由 ±(h+4) 收到 -4（22px 圆角）。
- 出：200ms cubic-bezier(.4,0,.7,.2)，opacity→0、y ∓6、clip 回扩；reduceMotion → 0ms。
- CSS 侧另有 `[data-queue=down] .remote-queue{transition:height .22s var(--remote-settle)}`（css:8）。
- 列表淡入：`initial:{opacity:0} animate:{opacity:1,transition:{duration:.22,delay:.08,ease:"easeOut"}}`（js@1756100）。
- 类切换：面板根 `.remote-queue`（无 mode 类），方向由窗口属性 `data-queue=down|up` 决定；虚拟化容器 `.remote-queue-scroll`（滚动条 10px，thumb rgba(ink,.2)+3px 透明边，hover .34，css:21-24）。
- 虚拟化：`rowHeight:36, overscanCount:4`（js@1756200）；首帧后 `scrollToRow({index:current,align:"center",behavior:"instant"})`（js@1755300）；当前行变化 → `align:"smart", behavior:"smooth"`（js@1755500）；搜索词变化 → `align:"start", instant`（js@1755700）。

### 8.5 行内盒模型与当前行高亮

    .remote-queue-row      padding:0 8px 0 6px; height=36（虚拟化 style）                 css:26
    └ button.remote-queue-song   h32 radius9 padding 0 8px 0 4px gap8                    css:27
       ├ span.__position    w26 右对齐 10.5px/500 rgba(ink,.32) tabular-nums             css:29
       ├ span.__text        baseline, gap8                                               css:31
       │   ├ span.__title → span.__name 12.5px/500 rgba(ink,.86) ellipsis                css:33
       │   │                span.__tag 9.5/15 rgba(ink,.6) 底 rgba(ink,.1) pad 0 5px     css:35
       │   └ span.__artist  11px rgba(ink,.45) flex-shrink:2                              css:36
       └ span.remote-queue-actions  display:none；行 hover/focus-within → flex，gap1      css:37/38
           └ 3× .remote-queue-action 26×26 svg14                                         css:39

当前行高亮 = `data-current=true`（`p.key===currentKey`）→ `__name` 与 `__position` 改 `rgb(ink)` + `font-weight:700`（css:30/34），**无底色**；hover 才有 `rgba(ink,.08)`（css:28）。正在播的行（`playingKey`）与显示行（`trackKey`）都参与 `h`，`h` 为真时隐藏"播放下一首/移到末尾"两颗（保留"移到末尾"），原文（dMe js@1749100）：`g=s||c?[]:[...(h?[]:[{type:"queue-play-next",...}]),{type:"queue-move-to-end",...},...(h?[]:[{type:"queue-remove",...})]`。行点击 → `run("queue-play", index)` → `$5({type:"queue-play",key,index})`；↑↓ 跨行移动并 focus（js@1755000）；readOnly/controlsDisabled 时动作数组为空且按钮 disabled。

搜索框：`.remote-queue-search{height:40px;padding:0 14px 0 18px;border-bottom:1px solid rgba(ink,.07);color:rgba(ink,.45)}`（css:13，focus-within → .75 css:14），放大镜 svg13 + `input 12px rgb(ink)`（css:15，placeholder rgba(ink,.38) css:16）+ 清除键 20×20 svg12（css:18）。过滤 `uMe(n,s)`（js@1748900）：查询按空格分词、归一化后要求 `items[i].searchText` **包含全部词**；IME 合成中不响应（`e.nativeEvent.isComposing`）；↓ 跳首个匹配、Enter 播首个匹配（E js@1756000）；清空后重新聚焦 input。无匹配 → `.remote-queue-note`（`remote.queueNoMatch`，11.5px rgba(ink,.45)，css:25）；空队列 → `queue.empty`。

---

## Q9 变量与时长汇总

### 9.1 remote.css 内全部自定义属性（检索词 `--remote-` + `--transition-glow-color`，唯一名 11 个）

| 变量 | 声明处 | 值 | 用途 |
|---|---|---|---|
| --remote-ink | css:90 / css:92 / css:93 | dark `255, 255, 255`；light `17, 17, 22`；cover/transparent 强制 `255,255,255` | 所有前景色三元组基底 |
| --remote-paper | css:90 / css:92 / css:93 | `#111114` / `#fff` / `#111114` | 反色：seg 选中、primary 按钮、switch 滑块字色 |
| --remote-sung | css:90 / css:94 / css:120 / css:164 / css:165 | `rgb(ink)`；paused → `rgba(ink,.62)`；transparent+idle → `#ffffffc7`；pair[data-dim] → `rgba(ink,.62)` | 逐字渐变左端（已唱） |
| --remote-unsung | css:90 / css:94 / css:119 / css:120 / css:164 | `rgba(ink,.4)`；paused → `.3`；transparent+idle → `#ffffff9e` → paused `#ffffff80`；dim → `.3` | 主行基色 + 逐字渐变右端 |
| --remote-second | css:90 / css:119 | `rgba(ink,.64)`；transparent+idle → `#ffffffe6` | 副行（译文/罗马音） |
| --remote-next | css:90 / css:119 | `rgba(ink,.3)`；transparent+idle → `#ffffff9e` | 副行 data-kind=next |
| --remote-settle | css:90 | `cubic-bezier(.32, .72, 0, 1)` | 形态 / 队列 / cover 大过渡（JS 同值 `_Me` js@1753554） |
| --remote-snap | css:90 | `cubic-bezier(.2, .8, .2, 1)` | 快速吸附（deck、line、非 sleeve cover） |
| --remote-progress | css:182 初值 `0%`；JS 每帧覆写 | `(i/r*100).toFixed(2)+"%"`（js@1747350） | fill 宽度 + 游标 left |
| --remote-fill | 每字内联（js@1744900） | `0..100`，步进 0.1（`round(x*1000)/10`） | css:170 逐字渐变位置 |
| --transition-glow-color | css:90 / css:92 / css:93 | `rgba(ink,.8)`（dark）/ `rgba(ink,.45)`（light） | handoff 进度条发光 |

### 9.2 全部动画时长常量

| 常量/位置 | 值 | 作用 |
|---|---|---|
| css:103/104, 108/109, 128/132 | .3s snap；.56s settle+60ms | paper clip / ring left / cover transform+radius+shadow |
| css:141/145/146 | opacity .22s+140ms、.14s；transform .42s cubic-bezier(.2,.8,.2,1) | line↔card 模式 |
| css:148/149 | translate .3s snap / .56s settle+60ms | sleeve 文字位移 |
| css:139 | font-size .42s cubic-bezier(.2,.8,.2,1) | 无封面 F |
| css:161/162 | .26s+140ms / .18s | .remote-lyric 显隐 |
| css:182/183 | .24s snap / .44s settle+120ms | .remote-line translateY(34) |
| css:184/185/193/194 | .32s；lit .14s ease-out | 轨道粗细与配色 |
| css:197/198 | .22s snap+80ms / .18s ease-out | deck 进出 |
| css:122/123/124 | .32s / .22s snap+80ms / .18s ease-out | .remote-lit 组 |
| css:114/115/116/118 | .32s；非 idle .14s ease-out | glass / cloud |
| css:137 | .32s | cover__image filter（paused 压暗） |
| css:8 | height .22s settle | 队列高度 |
| css:91 | opacity 90ms ease-out | 换高隐藏整窗 |
| css:200/201/202 | 底色/色/opacity .14s ease-out；transform .12s ease-out（active scale .94） | remote-key |
| css:179/68/85/42/19/43 | .14s ease-out | tool / btn / action / back / clear |
| css:27/39 | background-color .12s ease-out | 队列行与行内动作 |
| css:64/65 | .16s ease-out；left .16s cubic-bezier(.2,.8,.2,1) | switch 轨道与滑块 |
| css:206/207/208 | .32s；lit/paused .14s ease-out | transport svg opacity |
| css:228 | duration/delay = 0s !important | 减少动效全局 |
| src-BNDXyq_f.css@211373 | animation 2.4s ease-in-out infinite | 进度条 handoff 发光 |
| js@1735808 `u7` | .3s easeInOut | cover/song face 非 incoming 过渡 |
| js@1735814 `d7` | {duration:.55, ease:"linear"} | handoff incoming 面淡入 |
| js@1734428 `l7` | .5s | p7 兜底透明度补间 |
| js@1744515 `v7` / js@1744522 `aMe` | .18s / 20ms | 歌词离场 + 落地等待 |
| js@1746600 | .26s [.2,.8,.2,1]，y 6px | 歌词入场 |
| js@1753554/1753572/1753726 | 340ms [.32,.72,0,1] / 200ms [.4,0,.7,.2] / yMe=4 | 队列面板进/出/clip |
| js@1756100 | .22s delay .08 easeOut | 队列列表淡入 |
| js@1759100 wMe | .18s easeOut，y ±4 | Sheet 进出 |
| js@1739400 `Zje` | 350ms | prev/next hover 预览延迟 |
| js@1747285 `cMe` | 150ms | 非拖拽 seek 提交防抖 |
| js@1763930 `DMe` | 800ms | 松手后不覆盖预览 |
| js@1767541 `LMe/RMe` | 90ms ease-in / 140ms ease-out | 减少动效下 idle 翻转（WAAPI） |
| js@1770764 `YMe` | 800ms | 指针离开延迟（Lje） |
| js@1766300 `FMe` / js@1750380 `fMe` | 6000ms | Sheet / 队列 无悬停自动收起 |
| js@1768000 `GMe` | 5000ms | 导航意图（handoff 方向）有效期 |
| js@1750391 `pMe/x7/S7/C7` | 220 / 90 / 184 / 6 | 队列换高节拍与几何 |
| js@1753589 `yMe` | 4 | 队列 clip 外扩 |
| js@1765465 `jMe` | 3 秒 | 非跟唱时下一句提前显示阈值 |
| js@1744496 `rMe` | 26px | 主行数换算行高 |
| js@1736900 `qje` | 8px | recede 模糊上限（步进 .25px） |
| js@1767990 `k7/BMe/A7/VMe/HMe` | 14 / 40 / 4 / 10 / 22 | 文字底衬 .remote-cloud 量算内距 |
| main:5323 | 80ms | 指针轮询 |
| main:5237 | setInterval（主窗点击穿透解锁 hover 监视） | 非 remote |
| 主窗渲染层 js@1439927 | 500ms | 快照发布节奏 |

---

## H5 WPF 落地映射

| Folia 机制 | WPF 实现 | 能做到 / 需近似 |
|---|---|---|
| data-idle / data-lit / data-sleeve / data-bg / data-theme 属性组合 | 三个 VisualStateManager 状态组：Shape(Idle_Sleeve / Idle_Card / Lit_Line / Lit_Card)、Background(Default / Cover / Transparent)、Theme(Light / Dark)，外加 Shifting / Queue_Down / Queue_Up 触发器 | 能做到。用状态组避免 4 维组合爆炸；data-notitle 用 Visibility.Collapsed 触发器 |
| cover 三态 transform（translate+scale，origin 0 0） | RenderTransformOrigin="0,0" + TransformGroup(TranslateTransform, ScaleTransform)，Storyboard 三条 DoubleAnimation（X/Y/Scale） | 能做到。176×0.125=22、176×0.318182=56；WPF 缩放位图会糊 → 预生成 22/56/176 三套位图，动画期 BitmapScalingMode=HighQuality |
| border-radius 48px 在 scale(.125) 下视觉 6px | thumb 直接 CornerRadius=6（换算等价），sleeve 用 18,0,0,18，card 用 11 | 等价换算，非近似 |
| clip-path inset(… round 18px) 过渡 | Border.Clip 或 RectangleGeometry.Rect 的 RectAnimation（WPF 原生支持 Rect 关键帧），RadiusX/Y 同步动画 | 能做到。sleeve↔lit 的 174px 擦边用 Rect.X/Width 动画复刻 |
| box-shadow inset 描边 + 外阴影 | 外阴影 DropShadowEffect(BlurRadius=3,ShadowDepth=1,Opacity=.45)；inset 用 1px Border + 顶部 1px 高光线（LinearGradientBrush 覆盖层） | **需近似**：WPF 无 inset shadow |
| wash = 拉伸封面 + blur(38) + saturate(1.2) + 竖向黑遮罩 | Image Stretch=Fill + 负 Margin(-6,70.4,168,70.4) 复刻 inset 映射 → BlurEffect → 自定义 ShaderEffect(saturate) → 覆盖 Rectangle（LinearGradientBrush #09090d α .4588→.7020） | ① **需标定**：WPF BlurEffect.Radius 语义 ≠ CSS σ，先试 Radius≈76 再按 §4.3 表回归；② saturate 用 linearRGB 矩阵 PixelShader（feColorMatrix 等价）；③ 三层放 BitmapCache 上避免每帧重算 |
| 逐字 karaoke（background-clip:text + --remote-fill） | 每字一个 TextBlock，前景 = LinearGradientBrush(MappingMode=RelativeToBoundingBox)，两个 GradientStop 分别 sung/unsung，用 Offset 关键帧平移（16% 软边） | 结构等价。**做不到**：box-decoration-break:slice 的跨行切片填充 → 多行逐字需自绘 GlyphRun；padding-bottom:2px 用 LineStackingStrategy 近似 |
| 歌词 pair 换行（180ms easeIn + 20ms + 260ms [.2,.8,.2,1]，y ±6） | 单 ContentControl：Storyboard A（Opacity→0 + TranslateY→-6，180ms CubicEase EaseIn Control(0.4,0,1,1)）→ Completed 里换文本 → Storyboard B（Opacity 0→1 + TranslateY 6→0，260ms CubicEase Control(.2,.8,.2,1)） | 完全等价（含 20ms 空档）。用 BeginAnimation(..., HandoffBehavior=Snapshot) 防跳变；换文本时强制重建元素以复刻 key 变化 |
| AnimatePresence 面（双封面 / 双标题 handoff 交叉淡入 .3s easeInOut / incoming .55s linear） | Grid 两层 ContentControl，进出各一条 Storyboard，旧层 Completed 后再移除 | 需自管退场元素生命周期（WPF 无 AnimatePresence 语义） |
| 队列面板 clip 收展（inset ±(h+4)→-4，round 22；340ms/200ms） | RectangleGeometry.Rect 关键帧 + RadiusX/Y=22，配 Opacity（.4→1）与 TranslateTransform.Y（∓8→0） | 能做到 |
| 窗口换高（isShifting 90ms 隐藏 → 2 rAF → resize → 等 innerHeight 一致 → 取消隐藏） | BeginAnimation(OpacityProperty,0,90ms) → Dispatcher.BeginInvoke(DispatcherPriority.Loaded) ×2 替代 rAF → 改 Window.Height → 轮询 ActualHeight 一致后淡入 | 能做到；WPF AllowsTransparency=true 的 resize 会整窗重排，隐藏 90ms 策略照抄 |
| 指针进入（主进程 80ms 轮询 + 800ms 离开延迟 + focus-visible 或运算） | MouseEnter/MouseLeave + DispatcherTimer(800ms)；Keyboard.FocusedElement 变化补 o；click-through 期间 WS_EX_TRANSPARENT 天然收不到 hover | 能做到，且与 Folia 语义一致（set:28-31 注释明确"故意不转发鼠标事件，避免 hover 态卡住"） |
| 进度条（range input，整秒 step，拖拽 preview / 松手 commit / 150ms 防抖 / 800ms 保护） | Slider（Minimum=0, Maximum=dur, SmallChange=1）+ 自绘 Track/Thumb（9px 圆）；PreviewMouseLeftButtonDown/Up 对应 previewSeek/releaseSeek | 能做到。Folia 游标**无 hover 增长**，别自作主张加 |
| 时间文本（textContent 直写，不进 React 渲染） | CompositionTarget.Rendering 或 DispatcherTimer 里直接写 TextBlock.Text，格式化 m:ss | 能做到 |
| 字体（Inter 20/26 600 -.012em；tabular-nums） | FontFamily="Segoe UI"，FontWeight=SemiBold/Bold，TextOptions.TextFormattingMode=Display | **做不到**：letter-spacing:-.012em（WPF 无 letter-spacing，只能 GlyphRun 自绘或忽略）；-webkit-line-clamp:2 → .NET 4.8 无 MaxLines，需高度裁剪 + TextTrimming=CharacterEllipsis；tabular-nums 在 .NET 9 有 FontNumberWidth=Tabular，旧版用等宽数字近似 |
| 减少动效（uiMicroMotion → 全部 duration/delay=0；idle 翻转改 90/140ms 淡入淡出） | 全局 bool ReduceMotion：VSM 里把 Storyboard.Duration 统一置 0，并改用两条淡入淡出 Storyboard | 能做到 |
| 背景模式 localStorage | Properties.Settings 或 runtime 目录 json | 能做到（Folia 也不进主进程 store） |

---

## 取证方法（检索词 → 命中数，证明非猜测）

| 检索词 | 目标 | 命中 | 用于 |
|---|---|---|---|
| remote-wash | bootstrap-BJsYUc2U.js | 2（js@1773000, 1773058） | Q4 |
| createLinearGradient / getImageData / quantize | bootstrap | 0 / 0 / 0 | Q4 反证 |
| linear-gradient( | bootstrap | 7（均非 remote 卡片底） | Q4 |
| sleeve | bootstrap | 2（js@225537 i18n、js@1772573 data-sleeve） | Q1 |
| data-idle / data-bg | bootstrap | 1 / 1（js@1772554 / 1772525） | Q1 |
| remote-paper | bootstrap | 1（js@1772891） | Q4 |
| loopMode | bootstrap | 14（js@1700019..1742135） | Q7 |
| remote.more / "remote.loop" | bootstrap | 1（js@1742436）/ 0 | Q7 |
| --remote-* / --transition-glow-color | remote.css | 11 个唯一名（css:90-182） | Q9 |
| remote-progress-transition-glow | 全部 15 个 css chunk | 1（src-BNDXyq_f.css@211373） | Q6/Q9 |
| @keyframes | bootstrap-DwFEGzvL.css | 4（status-toast-fill / pd-rise-in / pd-fade-in / pd-cue-in，均非 remote） | Q2/Q3 |
| dominantColor / quantize / palette | main.cjs | 0 / 0 / 3（main:3228 / 4497 / 4514，非 remote） | Q4 反证 |
| expandCover / hideTitlebar / textShade | main.cjs | 7 / 4 / 4 | Q1/Q7 |
| REMOTE_CONTROL_*SETTING_KEY = | extract/electron | main:2597/2598 + set:5-10 | Q7 |
| syncRemoteControlPointerInside | main.cjs | 2（main:5342 定义、main:5369 调用） | Q1 |
| remote-lyric__pair / remote-lyric | bootstrap | 1 / 2 | Q3 反证 |
| 队列常量 fMe/pMe/x7/S7/C7/mMe/T7/w7/hMe | bootstrap | 各 1（js@1750380..1750558） | Q8 |

像素取证：Node + 内置 zlib 自解 PNG（无外部依赖）。folia-753.png 753×230（=602×184 @1.2508）；卡片边界实测 x 6..598 / y 4..179；进度亮行 y 162..165、亮列 x 201..571；wash 采样见 §4.3。

---

## 未决项 / 缺口

1. **放大态无 Folia 原生截图**。folia-753.png 是缩小态（已用于 §5.1 校验）；reference.png（570×252）与 user-shot2.png（886×573）是非 1:1 的网页 mock 截图，其中封面 thumb 目测 ~48px，与 CSS 推出的 **22 DIP**（176×0.125）不符。用户口径"48px 圆角小图"对应 CSS 的 border-radius:48px（176px 源空间，缩放后视觉 6px），不是 48 DIP 的盒子。§5.2/§5.3 坐标全部按 CSS 原文换算，未做像素复核。
2. **wash 模型残差**：R/G 系统性 +5..+18/255（模型偏暗），主因 Blink 模糊核的低分辨率近似；WPF 侧需实测标定 BlurEffect.Radius。
3. **Jl（当前行查找）与 nl（空行判定）未展开**：bootstrap 内检索 "Jl=" / "nl=" 命中 0 → 由其它 chunk（usePlaybackStore / noLyricLines）导入，本次未追。
4. **Kje（handoff 不透明度状态机，js@1734434）只读了前 200 字符**：isIncomingDominant / incomingOpacity / outgoingOpacity / isGlowActive 的分段函数未逐行展开；handoff 时长（.55s linear / .3s easeInOut / 2.4s glow / l7=.5 兜底）已确证，精确 alpha 曲线待补。
5. **i18n 文案原文未取**（只取到 key 生成式 "remote."+lm(mode) → loopOff / loopOne / loopAll）。
6. **Sheet 的 export tab（Hje js@1731636）与 size tab（Vje）未展开**：契约 v1.1 §2 已声明本次不移植导出/尺寸页。
7. **WMe 底衬量算（js@1768072）只在 transparent+textShade 下生效**，本次无该态截图，未做像素校验。
