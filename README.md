# 网易云音乐小窗（MiniPlayer Overlay）

给网易云音乐 PC 客户端加一个独立的小播放器窗口：**不修改客户端任何文件**，通过 CDP 连接客户端已渲染的页面，注入一层轻量 bridge 脚本，小窗用独立的 WPF 窗口渲染，状态双向同步。

适用于「客户端最小化到托盘、还要看歌名歌词和切歌」的场景。

---

## 它做什么

- 桌面上一个 600×180 左右的无边框小窗（尺寸随 DPI 自适应，运行时读 `physPerDip`）
- 显示封面 / 歌名 / 歌手 / 歌词（当前行 + 上一行 + 下一行）/ 进度条 / 播放队列
- 上一首、播放暂停、下一首、循环模式、喜欢、音量、置顶、换主题、展开/收起
- 悬停亮起（lit）与自动隐藏，跟随网易云主窗口状态
- 状态回写：小窗上的点击直接作用到客户端

## 工作原理

```
网易云客户端（带 --remote-debugging-port=9222 启动）
        │  orpheus://orpheus/pub/app.html
        ▼
  CDP WebSocket  ──────────────┐
        │                      │
   MiniPlayer.exe              │  注入 bridge/mini.js（fetch + eval，绕 CSP）
        │                      ▼
        │              页面内 window.__NCM_MINI__
        │                      │
        └──── HTTP 127.0.0.1:46210 ──── POST /state 上报状态
                                    GET  /cmd   下发命令
```

要点：

- **不走包内注入**：改 `orpheus.ntpk` 这条路已实测封死，项目不改客户端包体
- 客户端页面 CSP 允许 `unsafe-eval` + `connect-src http:`，所以脚本用「取本文件 + 间接 eval」注入，`<script src>` 只是首选尝试、失败自动回退
- bridge 掉线自愈：overlay 每 3s 自检 `window.__NCM_MINI__`，缺失即重新分发（实测 1.7–3.7s 恢复）
- 全部写入范围：当前用户 `HKCU:\Software\Classes\ncmmini` + 本项目目录，不写系统目录、不写用户配置目录

## 目录

| 路径 | 内容 |
|---|---|
| `overlay/MiniPlayer.exe` | 程序本体 |
| `overlay/src/*.cs` | 源码（`WindowUi.cs` 界面 / `Channels.cs` HTTP 与命令通道 / `Native.cs` Win32 / `Program.cs` 入口） |
| `overlay/build.ps1` | 编译脚本（`csc`，编译前自动留档上一版 exe） |
| `bridge/mini.js`、`bridge/mini.css` | 注入到页面的 bridge |
| `install/install.ps1` / `uninstall.ps1` | 注册/移除 `ncmmini://` 协议处理器 |
| `install/build-package.ps1` | 打包脚本 |
| `install/README.md` | **详细安装说明与写入范围清单（先读这个）** |
| `docs/` | 契约、状态记录、bridge 说明、patch 留档 |

## 快速开始

```powershell
# 1. 以调试端口启动网易云（只需一次，之后用快捷方式即可）
& "C:\Program Files\CloudMusic\cloudmusic.exe" --remote-debugging-port=9222 --disable-background-timer-throttling --disable-backgrounding-occluded-windows

# 2. 启动小窗
.\overlay\MiniPlayer.exe

# 3. 注册 ncmmini:// 协议（可选，用于从别处唤起）
powershell -NoProfile -ExecutionPolicy Bypass -File .\install\install.ps1
```

卸载：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\install\uninstall.ps1
```

## 诊断接口

小窗自带本地 HTTP，仅监听 `127.0.0.1`：

| 接口 | 用途 |
|---|---|
| `GET /state` | 当前播放状态（18 个字段：`v,src,title,artist,album,cover,playing,pos,dur,timeText,liked,loop,vol,lyric,queue,bar,ok,err`） |
| `GET /info` | 运行信息：`backend`、`cdpConnected`、`bridgeInject`、`bridgeBuild` |
| `GET /ctl/dbg-layout` | 20 个交互节点的运行时几何（DIP） |
| `GET /ctl/dbg-pointer` | 当前 DPI / `physPerDip` / 物理窗口尺寸 |

## 已知约束

- 依赖客户端以 `--remote-debugging-port` 启动，否则 CDP 连不上
- 只支持 Windows（WPF + Win32 分层窗口）
- 尺寸/坐标一律运行时读 DPI，不要写死像素值
- 与某些游戏全屏独占/自动切换分辨率共存时需要重新抓一次几何

## 状态

当前开发状态、逐条验收证据与已知问题见 [docs/STATUS.md](docs/STATUS.md)，协议与行为契约见 [docs/CONTRACT.md](docs/CONTRACT.md)。

## 免责

本项目通过客户端自身的调试接口工作，不修改网易云音乐的任何文件，也不重新分发其任何资源。仅供个人学习与自用。
