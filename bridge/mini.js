/*!
 * ncm-mini bridge v2 — 注入网易云音乐 3.1.42.205594 CEF 页面的桥接脚本
 * 契约: netease-mini/docs/CONTRACT.md + docs/BRIDGE-NOTES.md
 * 目标页: orpheus://orpheus/pub/app.html
 * 单文件 / 无 import/require/外链 script / 只读 + 合成事件控制 / 静默降级
 * 挂载: window.__NCM_MINI__ = { version, build, state, cmd, cmdSync, dom, ... }
 *
 * ---- M1 build 指纹(活实例身份证明) ----
 *   build = ncmmini-bridge-5ccdaf2e-060676
 *     XXXXXXXX = sha256(BODY) 的前 8 位十六进制
 *     NNNNNN   = BODY 字节数(6 位, 前补 0)
 *     BODY     = 本文件里 "@@NCMMINI-BODY-BEGIN@@" 标记行之后 到 末行 "@@NCMMINI-FP@@" 之前 的字节序列
 *   三个可读槽(本注释块 / var __NCMMINI_FP__ / 末行 FP)全部在 BODY 之外且定宽 => 指纹是不动点,
 *   把 sha8 写进文件不会改变 sha8。整文件 sha256 无法自指(写进去就变), 故取 BODY 口径。
 *   校验: node ncm-re/probe/verify-build.mjs   (同时打印磁盘侧与页面侧 build 并断言相等)
 */
var __NCMMINI_FP__ = 'ncmmini-bridge-5ccdaf2e-060676';   // M1 指纹槽(不参与 BODY 哈希)
//@@NCMMINI-BODY-BEGIN@@
(function () {
  'use strict';
  /* ---- M1 身份: build 来自 BODY 之外的指纹槽, 与磁盘文件一一对应 ---- */
  var BUILD = (typeof __NCMMINI_FP__ !== 'undefined' && __NCMMINI_FP__) ? String(__NCMMINI_FP__) : 'ncmmini-bridge-unknown-000000';
  function nowMs() { try { return performance.now(); } catch (e) { return Date.now(); } }

  /* ---- M5 旧实例退休: 只要名字被抢走, 旧实例的 live() 守卫下一轮自动退出 ---- */
  var PREV = null, PREV_STOP = null, PREV_BUILD = null, RETIRE_LOG = [];
  try { PREV = window.__NCM_MINI__ || null; PREV_STOP = window.__NCM_MINI_STOP__ || null; } catch (e) {}
  if (PREV) { try { PREV_BUILD = PREV.build ? String(PREV.build) : 'pre-fingerprint(no build)'; } catch (e) { PREV_BUILD = 'unreadable'; } }
  function retirePrev() {
    try { if (typeof PREV_STOP === "function") { PREV_STOP(); RETIRE_LOG.push("old.stop-fn"); } } catch (e) { RETIRE_LOG.push("old.stop-fn-err"); }
    try { if (PREV && typeof PREV.stop === "function") { PREV.stop(); RETIRE_LOG.push("old.api.stop"); } } catch (e) { RETIRE_LOG.push("old.api.stop-err"); }
    try { window.__NCM_MINI__ = API; RETIRE_LOG.push("reassigned"); } catch (e) { RETIRE_LOG.push("reassign-err"); }
    try { if (PREV) PREV.retired = true; } catch (e) {}
    return RETIRE_LOG;
  }

  var CFG = window.__NCM_MINI_CFG__ || {};
  var PLAY_MS = CFG.playMs || 250;              // 播放中轮询
  var PAUSE_MS = CFG.pauseMs || 1500;           // 暂停轮询
  var SWEEP_MS = CFG.sweepMs || 3000;           // M2: 离线全端口重扫周期(原 30000)
  var BOOT_SWEEP_MS = CFG.bootSweepMs === undefined ? 0 : CFG.bootSweepMs;  // M2: boot 首轮扫描延迟, 默认 0ms
  var FIRST_RETRY_MS = CFG.firstRetryMs === undefined ? 1200 : CFG.firstRetryMs; // M2: 首轮未命中的补扫(<=1500ms)
  var WATCHDOG_MS = CFG.watchdogMs || 200;      // 兜底节拍; 主链路由网络回调续期, 规避隐藏页嵌套定时器限流
  var PROBE_TIMEOUT_MS = CFG.probeTimeoutMs || 1200;
  var FETCH_TIMEOUT_MS = CFG.fetchTimeoutMs || 2500;
  var KICK_COOLDOWN = CFG.kickCooldownMs || 60000;
  var SELF_START_MODE = CFG.selfStartMode || 'iframe';  // 'iframe'(默认,安全) | 'href'(协议已注册) | 'off'(禁用)
  var POLL_MS = CFG.pollMs || 40;               // M3: applied 回读采样步长
  var CMD_DEDUPE_MS = CFG.cmdDedupeMs === undefined ? 400 : CFG.cmdDedupeMs;  // 无 id 命令的内容去重窗
  var ACK = CFG.ack === true;                   // 默认关: 小窗侧 JSON 解析确认能吃未知字段后再开
  var VOL_TOL = CFG.volTol === undefined ? 2 : CFG.volTol;
  var SEEK_TOL = CFG.seekTol === undefined ? 1.6 : CFG.seekTol;
  var FB = {
    toggle: CFG.settleToggleMs || 700, next: CFG.settleNavMs || 900, prev: CFG.settleNavMs || 900,
    seek: CFG.settleSeekMs || 900, vol: CFG.settleVolMs || 500, loop: CFG.settleLoopMs || 500,
    like: CFG.settleLikeMs || 1500, openQueue: CFG.settleQueueMs || 500, closeQueue: CFG.settleQueueMs || 500,
    toggleMain: CFG.settleMainMs || 900, showMain: CFG.settleMainMs || 900
  };
  var LS_PORT = 'ncmmini.port';
  var LS_KICK = 'ncmmini.kickAt';
  var LS_PROTO = 'ncmmini.protocol';
  var PORTS = [46210, 46211, 46212, 46213, 46214, 46215, 46216, 46217, 46218, 46219];
  var dlog = CFG.debug ? function () { try { console.log.apply(console, ['[ncmmini]'].concat([].slice.call(arguments))); } catch (e) {} } : function () {};

  /* ------------------------------------------------------------------ utils */
  function lsGet(k) { try { return window.localStorage.getItem(k); } catch (e) { return null; } }
  function lsSet(k, v) { try { window.localStorage.setItem(k, String(v)); } catch (e) {} }
  function num(x) { var n = Number(x); return (n === n && n !== Infinity && n !== -Infinity) ? n : null; }
  function str(x) { return (x === null || x === undefined) ? null : String(x); }
  function clip(s, n) { s = String(s == null ? '' : s); return s.length > n ? s.slice(0, n) : s; }
  function mmss(sec) { sec = Math.max(0, Math.floor(sec || 0)); var m = Math.floor(sec / 60), s = sec % 60; return m + ':' + (s < 10 ? '0' + s : s); }
  function parseTimeText(t) {   // "0:18 / 3:58" -> {pos,dur}
    var m = /(\d{1,3}):(\d{2})\s*\/\s*(\d{1,3}):(\d{2})/.exec(String(t || ''));
    if (!m) return null;
    return { pos: (+m[1]) * 60 + (+m[2]), dur: (+m[3]) * 60 + (+m[4]) };
  }

  /* ------------------------------------------------- redux(store) via fiber */
  var _store = null;
  function fiberKeyOf(el) {
    for (var k in el) {
      if (k.indexOf('__reactInternalInstance$') === 0 || k.indexOf('__reactFiber$') === 0) return k;
    }
    return null;
  }
  function findStore() {
    var seeds = [], sels = ['footer', '.cmd-space.btns', '.play-btn', 'input[type=range]', '#root', 'body'];
    for (var i = 0; i < sels.length; i++) { try { var e = document.querySelector(sels[i]); if (e) seeds.push(e); } catch (err) {} }
    if (document.body && document.body.firstElementChild) seeds.push(document.body.firstElementChild);
    for (var s = 0; s < seeds.length; s++) {
      var el = seeds[s]; if (!el || !el.tagName) continue;
      var fk = fiberKeyOf(el); var f = fk ? el[fk] : null, hops = 0;
      while (f && hops++ < 400) {
        var p = f.memoizedProps;
        if (p) {
          if (p.store && typeof p.store.getState === 'function') return p.store;
          if (p.app && typeof p.app.getState === 'function') return p.app;
        }
        f = f.return;
      }
    }
    return null;
  }
  function store() {
    if (!_store || typeof _store.getState !== 'function') _store = findStore();
    return _store;
  }
  function getSt() { var s = store(); return s ? s.getState() : null; }
  function put(action) {
    var s = store();
    if (!s || typeof s.dispatch !== 'function') return false;
    try { s.dispatch(action); return true; } catch (e) { dlog('dispatch fail', e && e.message); return false; }
  }

  /* ------------------------------------------------------------ DOM finders */
  function hasCls(el, re) {
    var c = (typeof el.className === 'string') ? el.className : String(el.className || '');
    return re.test(c);
  }
  function each(cb) { Array.prototype.forEach.call(cb.list || document.querySelectorAll(cb.sel), function (el, i) { cb.fn(el, i); }); }
  function visible(el) { try { var r = el.getBoundingClientRect(); return r.width > 0 && r.height > 0; } catch (e) { return false; } }
  function selfOk(b, reIcon) {
    var c = typeof b.className === 'string' ? b.className : String(b.className || '');
    if (/playlist-play-button/i.test(c)) return false;          // 卡片里的“播放整个列表”按钮, 不是传输键
    var icon = b.querySelector('[class*=cmd-icon]');
    if (icon && reIcon.test(String(icon.className))) return true;
    return reIcon.test(c);
  }
  function findButton(reIcon) {
    // 只在播放条/底部栏范围内找传输按钮, 且必须可见; 找不到再退到全文档(同样要求可见)
    var scopes = ['footer', '.center-btn-container', '.bottombar-container', '.player-bar', ''];
    for (var s = 0; s < scopes.length; s++) {
      var sel = scopes[s] === '' ? 'button, [role=button]' : scopes[s] + ' button, ' + scopes[s] + ' [role=button]';
      var nodes;
      try { nodes = document.querySelectorAll(sel); } catch (e) { continue; }
      for (var i = 0; i < nodes.length; i++) {
        var b = nodes[i];
        if (!visible(b)) continue;
        if (selfOk(b, reIcon)) return b;
      }
    }
    return null;
  }
  var ICON = {
    play: /cmd-icon-(play|pause)\b/i,
    prev: /cmd-icon-pre\b/i,
    next: /cmd-icon-next\b/i,
    like: /cmd-icon-like/i,
    mode: /cmd-icon-(loop|player-mode|singleloop|shuffle|randomplay|order)/i,
    queue: /cmd-icon-playlist\b/i,
    vol: /cmd-icon-(Volume|Mute)\d*\b/i
  };
  function btnPlay() { return findButton(ICON.play); }
  function btnPrev() { return findButton(ICON.prev); }
  function btnNext() { return findButton(ICON.next); }
  function btnLike() { return findButton(ICON.like); }
  function btnMode() { return findButton(ICON.mode); }
  function btnQueue() { return findButton(ICON.queue); }
  function btnVol() { return findButton(ICON.vol); }

  function progressInput(dur) {
    var list = document.querySelectorAll('input[type=range]'), fallback = null;
    for (var i = 0; i < list.length; i++) {
      var inp = list[i], mx = num(inp.max), v = num(inp.value);
      if (v === null) continue;
      if (dur && mx && Math.abs(mx - dur) <= 1.5) return inp;
      var lab = '';
      try { lab = String(inp.getAttribute('aria-label') || (inp.closest && inp.closest('[aria-label]') ? inp.closest('[aria-label]').getAttribute('aria-label') : '') || '') + String(inp.getAttribute('aria-labelledby') || ''); } catch (e) {}
      if (!fallback && (/进度|播放/.test(lab) || mx > 20)) fallback = inp;
    }
    return fallback;
  }
  function timeTextEl() {
    var nodes = document.querySelectorAll('footer span, footer div, .center-btn-container span');
    for (var i = 0; i < nodes.length; i++) {
      if (nodes[i].children.length > 1) continue;
      var t = (nodes[i].textContent || '').trim();
      if (/^\d{1,3}:\d{2}\s*\/\s*\d{1,3}:\d{2}$/.test(t)) return nodes[i];
    }
    return null;
  }

  /* ---------------------------------------------------------------- state() */
  var MODE_OUT = { playCycle: 'list', PlayOneCycle: 'one', playOneCycle: 'one', PlayRandom: 'shuffle', playRandom: 'shuffle', playAi: 'shuffle', PlayFm: 'shuffle', playFm: 'shuffle', PlayOrder: 'off', playOrder: 'off' };
  var MODE_IN = { off: 'playOrder', list: 'playCycle', one: 'playOneCycle', shuffle: 'playRandom' };
  var STATE_ERR = { ok: false, err: 'no-store', v: 1, src: 'ncm', title: null, artist: null, album: null, cover: null, playing: false, pos: null, dur: null, timeText: null, liked: null, loop: null, vol: null, lyric: { index: null, line: null, next: null, lines: [] }, queue: [], bar: { prev: false, play: false, next: false, like: false, loop: false, queue: false, vol: false, seek: false } };

  function readState() {
    var st = getSt();
    if (!st || !st.playing) return STATE_ERR;
    var p = st.playing, host = st['async:hostResource'] || {}, pl = st.playingList || {}, lyr = st['async:lyric'] || {};
    var rid = str(p.onlineResourceId || p.resourceTrackId);
    var dur = num(p.resourceDuration) || num(p.curPlaying && p.curPlaying.track && p.curPlaying.track.duration / 1000) || null;
    var inp = progressInput(dur), pos = null;
    if (inp) pos = num(inp.value);
    var ttEl = timeTextEl(), timeText = ttEl ? (ttEl.textContent || '').trim() : null, parsed = parseTimeText(timeText);
    if ((pos === null) && parsed) pos = parsed.pos;
    if (!dur && parsed) dur = parsed.dur;
    if (pos === null && p.restoreResource) pos = num(p.restoreResource.current);
    var playing = (p.playingState === 2);
    if (pos !== null && dur) { if (pos > dur) pos = dur; if (pos < 0) pos = 0; }
    // lyric
    var lines = [], index = null, line = null, next = null;
    var src = (lyr.lyricLines && lyr.lyricLines.length && String(lyr.resourceTrackId) === String(rid)) ? lyr.lyricLines : ((p.playingLyric && p.playingLyric.length) ? p.playingLyric : []);
    if (src && src.length) {
      for (var i = 0; i < src.length; i++) lines.push(String((src[i] && (src[i].lyric != null ? src[i].lyric : src[i].txt)) != null ? src[i].lyric : ''));
      index = num(p.playingLyricLineNumber);
      if (pos !== null) {  // 用时间轴定位，比 lineNumber 更可靠
        var best = -1;
        for (var j = 0; j < src.length; j++) { var tm = num(src[j] && src[j].time); if (tm !== null && tm <= pos + 0.35) best = j; }
        if (best >= 0) index = best;
      }
      if (index === null || index < 0 || index >= lines.length) index = (index !== null && index < 0) ? -1 : (lines.length ? 0 : null);
      line = (index !== null && index >= 0 && index < lines.length) ? lines[index] : null;
      next = (index !== null && index + 1 < lines.length) ? lines[index + 1] : null;
    }
    // liked
    var liked = null;
    if (host.likeTracksMap) liked = !!host.likeTracksMap[String(rid)];
    else if (p.curPlaying && typeof p.curPlaying.liked === 'boolean') liked = p.curPlaying.liked;
    if (liked === null) {
      var b = btnLike();
      if (b) { var ic = b.querySelector('[class*=cmd-icon]'); liked = ic ? /lik(ed|ing)?\b/i.test(String(ic.className)) && !/_number/.test(String(ic.className)) : false; } else liked = false;
    }
    // queue
    var q = [], list = pl.curPlayingList || [];
    var qmax = CFG.queueMax || 300;
    for (var k = 0; k < list.length && q.length < qmax; k++) {
      var it = list[k], tk = it && it.track;
      var id = str(it && (it.resourceId || it.id)) || str(tk && tk.id);
      var ar = null;
      if (tk && tk.artists && tk.artists.length) { var names = []; for (var a = 0; a < tk.artists.length; a++) names.push(tk.artists[a].name); ar = names.join(' / '); }
      else if (tk && tk.ar) ar = str(tk.ar);
      q.push({ index: q.length, id: id ? String(id) : null, title: clip(str(tk && tk.name) || str(it && it.text) || '', 120), artist: clip(str(ar) || '', 120), current: !!(id && rid && id === rid) });
    }
    // artist from playing slice
    var artist = null;
    if (p.resourceArtists) {
      if (typeof p.resourceArtists === 'string') artist = p.resourceArtists;
      else if (p.resourceArtists.length) { var ns = []; for (var z = 0; z < p.resourceArtists.length; z++) ns.push(p.resourceArtists[z].name || p.resourceArtists[z].artist); artist = ns.filter(function (x) { return !!x; }).join(' / '); }
    }
    if (!artist && p.curTrack && p.curTrack.artists) artist = String(p.curTrack.artists).replace(/[\/&]/g, ' / ');
    var album = null;
    try { album = str(p.curPlaying.track.album.name) || str(p.curPlaying.track.album.albumName); } catch (e) {}
    var bar = {
      prev: !!btnPrev(), play: !!btnPlay(), next: !!btnNext(), like: !!btnLike(),
      loop: !!btnMode(), queue: !!btnQueue(), vol: !!store(),
      seek: !!(inp || btnPlay())
    };
    return {
      v: 1, src: 'ncm',
      title: clip(str(p.resourceName), 200),
      artist: clip(artist, 200),
      album: clip(album, 200),
      cover: (p.resourceCoverUrl && /^(https?:|data:)/.test(p.resourceCoverUrl)) ? p.resourceCoverUrl : null,
      playing: playing,
      pos: pos === null ? null : Math.round(pos * 100) / 100,
      dur: dur === null ? null : Math.round(dur * 100) / 100,
      timeText: timeText || ((pos !== null && dur) ? mmss(pos) + ' / ' + mmss(dur) : null),
      liked: liked,
      loop: MODE_OUT[String(p.playingMode)] || null,
      vol: (function () { var x = num(p.playingVolume); return x === null ? null : Math.min(100, Math.max(0, Math.round(x * 100))); })(),
      lyric: { index: index, line: line, next: next, lines: lines },
      queue: q,
      bar: bar,
      ok: true, err: null
    };
  }
  function safeState() {
    try { return readState(); } catch (e) {
      stats.err++;
      try { STATE_ERR.ok = false; STATE_ERR.err = 'state:' + (e && e.message); } catch (x) {}
      return STATE_ERR;
    }
  }

  /* ------------------------------------------------------------------ click */
  function fire(el, types) {
    if (!el) return false;
    var r = el.getBoundingClientRect();
    var x = r.left + r.width / 2, y = r.top + r.height / 2;
    for (var i = 0; i < types.length; i++) {
      var name = types[i], ev;
      try {
        if (name.indexOf('pointer') === 0 && window.PointerEvent) {
          ev = new PointerEvent(name, { bubbles: true, cancelable: true, view: window, clientX: x, clientY: y, pointerId: 1, isPrimary: true, button: 0, buttons: (name === 'pointerup' ? 0 : 1) });
        } else {
          ev = new MouseEvent(name, { bubbles: true, cancelable: true, view: window, clientX: x, clientY: y, button: 0, buttons: (name === 'mouseup' ? 0 : 1), detail: (name === 'click' ? 1 : 0) });
        }
      } catch (e2) { ev = document.createEvent('MouseEvents'); ev.initMouseEvent(name, true, true, window, 1, x, y, x, y, false, false, false, false, 0, null); }
      try { el.dispatchEvent(ev); } catch (e3) { return false; }
    }
    return true;
  }
  var CLICK_SEQ = ['pointerdown', 'mousedown', 'pointerup', 'mouseup', 'click'];
  function click(el) { return el ? fire(el, CLICK_SEQ) : false; }

  /* --------------------------------------------------------- seek via DOM   */
  function seekByPointer(ratio) {
    var sl = document.querySelector('[aria-label="播放进度调节"]') || document.querySelector('.slider-default');
    if (!sl) return false;
    var r = sl.getBoundingClientRect();
    if (!r.width) return false;
    var x = r.left + r.width * Math.min(Math.max(ratio, 0), 1), y = r.top + r.height / 2;
    var mk = function (type, extra) {
      var o = { bubbles: true, cancelable: true, view: window, clientX: x, clientY: y, button: 0 };
      for (var k in extra) o[k] = extra[k];
      try { return type.indexOf('pointer') === 0 && window.PointerEvent ? new PointerEvent(type, o) : new MouseEvent(type, o); } catch (e) { return null; }
    };
    var hot = sl.querySelector('.hotzone-overlay') || sl;
    try {
      hot.dispatchEvent(mk('pointerdown', { pointerId: 1, isPrimary: true, buttons: 1 }));
      hot.dispatchEvent(mk('mousedown', { buttons: 1 }));
      hot.dispatchEvent(mk('pointermove', { pointerId: 1, isPrimary: true, buttons: 1 }));
      hot.dispatchEvent(mk('mousemove', { buttons: 1 }));
      hot.dispatchEvent(mk('pointerup', { pointerId: 1, isPrimary: true, buttons: 0 }));
      hot.dispatchEvent(mk('mouseup', { buttons: 0 }));
      sl.dispatchEvent(mk('click', { buttons: 0 }));
      return true;
    } catch (e) { return false; }
  }

  /* ============== M3 统一命令引擎: 发起 -> 窗内轮询回读 applied -> 未落地即 ok:false ============== */
  var TS = { miniBar: 'miniBar', playlist: 'playlist', sysTray: 'sysTray' };
  var BASE = ['playing', 'pos', 'vol', 'loop', 'liked'];
  function curQueueIndex(s) { var q = (s && s.queue) || []; for (var i = 0; i < q.length; i++) if (q[i] && q[i].current) return i; return -1; }
  function snapTo(keys) {
    var s = safeState(), o = {};
    for (var i = 0; i < keys.length; i++) {
      var k = keys[i];
      if (k === 'queueOpen') o.queueOpen = queueOpen();
      else if (k === 'miniMode') { var st0 = getSt(); o.miniMode = !!(st0 && st0.app && st0.app.isMiniMode); }
      else if (k === 'index') o.index = curQueueIndex(s);
      else if (k in s) o[k] = s[k];
    }
    return o;
  }
  function mkRes(name, arg, r) {
    var ok = !!r.ok;
    return {
      ok: ok, err: ok ? null : (r.err || 'not-applied'), via: r.via || null, applied: r.applied || null,
      cmd: name, arg: (arg === undefined ? null : arg), want: r.want || null,
      settledMs: (r.settledMs === undefined ? null : r.settledMs), tried: r.tried || []
    };
  }
  function lastVia(tried, fallback) {
    if (!tried.length) return fallback || null;
    var s = String(tried[tried.length - 1]);
    var i = s.indexOf(':');
    return i < 0 ? s : s.slice(0, i);
  }
  /* spec = { keys, want, check(applied, pre, elapsedMs)->bool, ms, err?, attempts:[{at, via, run(pre)->bool}] }
     attempts 按 at(相对发起时刻, ms) 依次点火; 每次点火后立刻回读, 命中即结束, 到 ms 仍未命中 => ok:false/'not-applied'. */
  function execute(spec, pre) {
    return new Promise(function (resolve) {
      var t0 = nowMs(), keys = spec.keys || BASE, ai = 0, tried = [], done = false;
      function finish(ok, applied) {
        if (done) return; done = true;
        resolve({
          ok: ok, err: ok ? null : (spec.err || 'not-applied'), via: lastVia(tried, spec.via),
          tried: tried.slice(), applied: applied, want: spec.want || null, settledMs: Math.round(nowMs() - t0)
        });
      }
      function pump() {
        if (done) return;
        if (!live()) { finish(false, snapTo(keys)); return; }
        while (ai < spec.attempts.length && (nowMs() - t0) >= spec.attempts[ai].at) {
          var at = spec.attempts[ai++], fired = false;
          try { fired = !!at.run(pre); } catch (e) { stats.err++; fired = false; }
          tried.push(at.via + (fired ? '' : ':fail'));
        }
        var a = snapTo(keys), hit = false;
        try { hit = !!spec.check(a, pre, nowMs() - t0); } catch (e2) { stats.err++; }
        if (hit) { finish(true, a); return; }
        if ((nowMs() - t0) >= spec.ms) { finish(false, a); return; }
        setTimeout(pump, POLL_MS);
      }
      pump();
    });
  }

  /* ---- 队列面板 / seek 的 DOM 路径 ---- */
  function queueSheet() {
    try {
      var el = document.querySelector('.cmd-sidesheet-right');
      if (el && visible(el)) return el;
    } catch (e) {}
    return null;
  }
  function queueOpen() { return !!queueSheet() || (function () { try { var m = document.querySelector('.cmd-sidesheet-mask'); return !!(m && visible(m)); } catch (e) { return false; } })(); }
  function openSheetDispatch() { return put({ type: 'app/openSideSheet', payload: { comName: 'PlayBarPlaylist' } }); }
  function queueRows(sheet) {
    var sel = 'li, [class*=playlist-item], [class*=ListItem], [class*=list-item], tr, [role=row], [class*=row]';
    var out2 = [], want = [];
    try { want = String(CFG.queueRowSel || '').split(',').filter(function (x) { return !!x; }); } catch (e) {}
    for (var w = 0; w < want.length; w++) { try { var n = sheet.querySelectorAll(want[w]); for (var i = 0; i < n.length; i++) out2.push(n[i]); } catch (e2) {} }
    if (out2.length) return out2;
    try { return Array.prototype.slice.call(sheet.querySelectorAll(sel)).filter(function (el) { return visible(el); }); } catch (e3) { return []; }
  }
  function clickQueueRow(i, title) {
    var sheet = queueSheet();
    if (!sheet) return false;
    var rows = queueRows(sheet);
    if (rows.length > i && rows[i]) return click(rows[i]);
    if (title) { for (var k = 0; k < rows.length; k++) { var t = (rows[k].textContent || '').trim(); if (t.indexOf(String(title).slice(0, 12)) === 0) return click(rows[k]); } }
    return false;
  }
  function closeQueueDom() {
    var m = null;
    try { m = document.querySelector('.cmd-sidesheet-mask'); } catch (e) {}
    if (!m || !visible(m)) return false;
    var r = m.getBoundingClientRect();
    var x = Math.max(2, r.left + Math.min(60, r.width / 2)), y = Math.max(2, r.top + r.height / 2);
    var mk = function (type, buttons) {
      var o = { bubbles: true, cancelable: true, view: window, clientX: x, clientY: y, button: 0, buttons: buttons };
      try { return type.indexOf('pointer') === 0 && window.PointerEvent ? new PointerEvent(type, o) : new MouseEvent(type, o); } catch (e) { return null; }
    };
    try {
      ['pointerdown', 'mousedown', 'pointerup', 'mouseup', 'click'].forEach(function (t) { var ev = mk(t, /up|click/.test(t) ? 0 : 1); if (ev) m.dispatchEvent(ev); });
      return true;
    } catch (e2) { return false; }
  }
  function seekByRange(sec) {
    var inp = progressInput(null);
    if (!inp) return false;
    try {
      var setter = Object.getOwnPropertyDescriptor(window.HTMLInputElement.prototype, 'value').set;
      setter.call(inp, String(sec));
      inp.dispatchEvent(new Event('input', { bubbles: true }));
      inp.dispatchEvent(new Event('change', { bubbles: true }));
      return true;
    } catch (e) { return false; }
  }

  var CMD = {
    /* toggle: 两个方向都走"点击真实播放/暂停键", 300ms 内没翻才补一次 dispatch(实测点击落地 60-150ms) */
    toggle: function (arg, pre) {
      var cur = !!pre.playing;
      var want = (typeof arg === 'boolean') ? arg : !cur;
      if (cur === want) {
        return { keys: BASE, want: { playing: want }, check: function (a) { return a.playing === want; }, ms: 200,
          attempts: [{ at: 0, via: 'noop', run: function () { return true; } }] };
      }
      return { keys: BASE, want: { playing: want }, check: function (a) { return a.playing === want; }, ms: FB.toggle,
        attempts: [
          { at: 0, via: 'click', run: function () { var b = btnPlay(); return !!b && click(b); } },
          { at: 400, via: 'dispatch', run: function () { return put({ type: 'playing/switchResumeOrPause', payload: { triggerScene: TS.miniBar } }); } }
        ] };
    },
    next: function (arg, pre) {
      return { keys: BASE.concat(['title', 'index']), want: { title: '!=' + clip(pre.title, 24) },
        check: function (a, p2, el) {
          if (a.title !== p2.title || a.index !== p2.index) return true;
          return el >= 500 && typeof a.pos === 'number' && a.pos <= 3 && typeof p2.pos === 'number' && p2.pos > 3;
        }, ms: Math.max(FB.next, 900),
        attempts: [
          { at: 0, via: 'click', run: function () { var b = btnNext(); return !!b && click(b); } },
          { at: 450, via: 'dispatch', run: function () { return put({ type: 'playingList/jump2Track', payload: { flag: 1, type: 'call', triggerScene: TS.miniBar } }); } }
        ] };
    },
    prev: function (arg, pre) {
      return { keys: BASE.concat(['title', 'index']), want: { title: '!=' + clip(pre.title, 24) },
        check: function (a, p2, el) {
          if (a.title !== p2.title || a.index !== p2.index) return true;
          return el >= 500 && typeof a.pos === 'number' && a.pos <= 3 && typeof p2.pos === 'number' && p2.pos > 3;
        }, ms: Math.max(FB.prev, 900),
        attempts: [
          { at: 0, via: 'click', run: function () { var b = btnPrev(); return !!b && click(b); } },
          { at: 450, via: 'dispatch', run: function () { return put({ type: 'playingList/jump2Track', payload: { flag: -1, type: 'call', triggerScene: TS.miniBar } }); } }
        ] };
    },
    /* seek: 绝对秒. 实测 setPlayingPosition 落地 ~60-340ms(播放中与暂停中都会落地), 未落地依次补 指针/原生 input 两路 */
    seek: function (arg, pre) {
      var sec = num(arg);
      if (sec === null || sec < 0) return { ok: false, err: 'bad arg' };
      if (!pre.dur) return { ok: false, err: 'no-track' };
      var target = Math.min(sec, pre.dur);
      var tol = function (el) { return SEEK_TOL + Math.max(0, el) / 1000; };   // 播放时钟会继续走, 允许误差随等待时间线性放宽
      return { keys: BASE, want: { pos: target },
        check: function (a, p2, el) { return typeof a.pos === 'number' && Math.abs(a.pos - target) <= tol(el); }, ms: FB.seek,
        attempts: [
          { at: 0, via: 'dispatch', run: function () { return put({ type: 'playing/setPlayingPosition', payload: { duration: target } }); } },
          { at: 320, via: 'pointer', run: function () { return seekByPointer(target / pre.dur); } },
          { at: 620, via: 'range', run: function () { return seekByRange(target); } }
        ] };
    },
    /* M4a vol: 契约 0..100 线性; store.playingVolume 是 0..1 浮点(app 音量滑条 minValue0 maxValue1 step .01) */
    vol: function (arg, pre) {
      var v = num(arg);
      if (v === null) return { ok: false, err: 'bad arg' };
      v = Math.min(100, Math.max(0, Math.round(v)));
      var x = v / 100;
      return { keys: BASE, want: { vol: v },
        check: function (a) { return typeof a.vol === 'number' && Math.abs(a.vol - v) <= VOL_TOL; }, ms: FB.vol,
        attempts: [
          { at: 0, via: 'dispatch', run: function () { return put({ type: 'playing/setVolume', payload: { volume: x } }); } },
          { at: 260, via: 'dispatch2', run: function () { return put({ type: 'playing/setVolume', payload: { volume: x } }); } }
        ] };
    },
    loop: function (arg, pre) {
      var mode = MODE_IN[String(arg)];
      if (!mode) return { ok: false, err: 'unknown loop: ' + arg };
      return { keys: BASE, want: { loop: String(arg) }, check: function (a) { return a.loop === String(arg); }, ms: FB.loop,
        attempts: [
          { at: 0, via: 'dispatch', run: function () { return put({ type: 'playing/switchPlayingMode', payload: { playingMode: mode, triggerScene: TS.miniBar, HeartBeatFlage: false } }); } },
          { at: 300, via: 'click', run: function () { var b = btnMode(); return !!b && click(b); } }
        ] };
    },
    like: function (arg, pre) {
      var want = (arg === undefined || arg === null) ? null : !!arg;
      if (want === null) return { ok: false, err: 'arg required (true/false)' };
      if (!!pre.liked === want) {
        return { keys: BASE, want: { liked: want }, check: function (a) { return a.liked === want; }, ms: 200,
          attempts: [{ at: 0, via: 'noop', run: function () { return true; } }] };
      }
      return { keys: BASE, want: { liked: want }, check: function (a) { return a.liked === want; }, ms: FB.like,
        attempts: [{ at: 0, via: 'click', run: function () { var b = btnLike(); return !!b && click(b); } }] };
    },
    openQueue: function (arg, pre) {
      return { keys: BASE.concat(['queueOpen']), want: { queueOpen: true }, check: function (a) { return !!a.queueOpen; }, ms: FB.openQueue,
        attempts: [
          { at: 0, via: 'click', run: function () { var b = btnQueue(); return !!b && click(b); } },
          { at: 300, via: 'dispatch', run: function () { return openSheetDispatch(); } }
        ] };
    },
    closeQueue: function (arg, pre) {
      return { keys: BASE.concat(['queueOpen']), want: { queueOpen: false }, check: function (a) { return !a.queueOpen; }, ms: FB.closeQueue,
        attempts: [
          { at: 0, via: 'mask-click', run: function () { return closeQueueDom(); } },
          { at: 300, via: 'dispatch', run: function () { return put({ type: 'app/closeSideSheet', payload: {} }); } }
        ] };
    },
    /* playIndex: 0 基序号 -> playing/playOneTrackInPlayingList{item:resourceId} (源码里 item 传 string 时按 curPlayingList.resourceId 查回整条) */
    playIndex: function (arg, pre) {
      var i = num(arg);
      if (i === null || i !== Math.floor(i) || i < 0) return { ok: false, err: 'bad arg' };
      var q = pre.queue || [];
      if (i >= q.length) return { ok: false, err: 'index-out-of-range: ' + i + '/' + q.length };
      var itemId = q[i] && q[i].id ? String(q[i].id) : null;
      var itemTitle = q[i] ? q[i].title : null;
      var from = curQueueIndex(pre);
      var step = (i > from) ? 1 : -1, reps = Math.abs(i - from);
      return { keys: BASE.concat(['title', 'index']), want: { index: i },
        check: function (a) { return a.index === i || (!!itemTitle && a.title === itemTitle); }, ms: CFG.settlePlayIndexMs || 2200,
        attempts: [
          { at: 0, via: 'dispatch', run: function () { return !!itemId && put({ type: 'playing/playOneTrackInPlayingList', payload: { item: itemId, flag: 0, switchType: 'call', triggerScene: TS.miniBar } }); } },
          { at: 700, via: 'queue-row', run: function () { if (!queueOpen()) openSheetDispatch(); return clickQueueRow(i, itemTitle); } },
          { at: 1500, via: 'jump-rel', run: function () { if (!reps || reps > 12) return false; for (var k = 0; k < reps; k++) put({ type: 'playingList/jump2Track', payload: { flag: step, type: 'call', triggerScene: TS.miniBar } }); return true; } }
        ] };
    },
    toggleMain: function (arg, pre) {
      var s = getSt();
      if (!s) return { ok: false, err: 'no-store' };
      var mini = !!(s.app && s.app.isMiniMode);
      return { keys: BASE.concat(['miniMode']), want: { miniMode: !mini }, check: function (a) { return !!a.miniMode === !mini; }, ms: FB.toggleMain,
        attempts: [{ at: 0, via: 'dispatch', run: function () { return put({ type: mini ? 'app/exitMiniMode' : 'app/switchToMiniMode', payload: {} }); } }] };
    },
    showMain: function (arg, pre) {
      var s = getSt();
      if (!s) return { ok: false, err: 'no-store' };
      return { keys: BASE.concat(['miniMode']), want: { miniMode: false }, check: function (a) { return !a.miniMode; }, ms: FB.showMain,
        attempts: [{ at: 0, via: 'dispatch', run: function () {
          var ok = false;
          if (s.app && s.app.isMiniMode) ok = put({ type: 'app/exitMiniMode', payload: {} }) || ok;
          ok = put({ type: 'app/restoreWindow', payload: {} }) || ok;
          ok = put({ type: 'app/showWindow', payload: {} }) || ok;
          ok = put({ type: 'app/activeWindow', payload: {} }) || ok;
          return ok;
        } }] };
    }
  };

  /* ---- 执行结果缓存: stats.lastAck / API.lastAck(); CFG.ack=true 时随下一次 POST 回传 ---- */
  var ACKQ = [];
  function noteAck(res, meta) {
    var a = { cmd: res.cmd, arg: res.arg, ok: res.ok, err: res.err, via: res.via, applied: res.applied, settledMs: res.settledMs === undefined ? null : res.settledMs,
      id: (meta && meta.id !== undefined) ? meta.id : null, t: Date.now() };
    stats.lastAck = a;
    if (ACK) { ACKQ.push(a); if (ACKQ.length > 16) ACKQ.splice(0, ACKQ.length - 16); }
    dlog('ack', JSON.stringify(a));
  }
  function cmdAsync(name, arg, meta) {
    var fn = CMD[name];
    if (!fn) return Promise.resolve(mkRes(name, arg, { ok: false, err: 'unknown cmd: ' + name }));
    var pre = safeState(), spec;
    try { spec = fn(arg, pre); } catch (e) { stats.err++; return Promise.resolve(mkRes(name, arg, { ok: false, err: (e && e.name) + ':' + clip(e && e.message, 120) })); }
    if (!spec) spec = {};
    stats.cmd++;
    if (spec.ok === false) { var r0 = mkRes(name, arg, { ok: false, err: spec.err || 'failed', via: spec.via }); noteAck(r0, meta); return Promise.resolve(r0); }
    spec.keys = spec.keys || BASE;
    return execute(spec, pre).then(function (r) { var res = mkRes(name, arg, r); noteAck(res, meta); return res; });
  }
  /* 不能 await 的调用方: 只点火 at=0 的那一路, applied = 立即回读(可能尚未落地), 未落地 err='pending' */
  function cmdSync(name, arg) {
    var fn = CMD[name];
    if (!fn) return { ok: false, err: 'unknown cmd: ' + name, via: null, applied: null, cmd: name, arg: (arg === undefined ? null : arg) };
    var pre = safeState(), spec;
    try { spec = fn(arg, pre); } catch (e) { stats.err++; return { ok: false, err: clip(e && e.message, 120), via: null, applied: null, cmd: name }; }
    if (!spec || spec.ok === false) return { ok: false, err: (spec && spec.err) || 'failed', via: null, applied: null, cmd: name };
    var tried = [];
    for (var i = 0; i < spec.attempts.length; i++) {
      if (spec.attempts[i].at !== 0) continue;
      var fired = false;
      try { fired = !!spec.attempts[i].run(pre); } catch (e2) { stats.err++; fired = false; }
      tried.push(spec.attempts[i].via + (fired ? '' : ':fail'));
    }
    var a = snapTo(spec.keys || BASE), hit = false;
    try { hit = !!spec.check(a, pre, 0); } catch (e3) { stats.err++; }
    var res = mkRes(name, arg, { ok: hit, err: hit ? null : 'pending', via: lastVia(tried, spec.via), tried: tried, applied: a, want: spec.want, settledMs: 0 });
    stats.cmd++;
    noteAck(res, { sync: true });
    return res;
  }
  function runCmd(name, arg) { return cmdAsync(name, arg, { src: 'legacy' }); }

  /* -------------------------------------------------------- lyric 预热(有副作用, 不放 state() 里) */
  var lyricFor = null;
  function ensureLyric() {
    try {
      var s = getSt(); if (!s) return;
      var rid = String(s.playing && s.playing.onlineResourceId || '');
      if (!rid) return;
      var L = s['async:lyric'] || {};
      if (String(L.resourceTrackId) === rid && L.lyricLines && L.lyricLines.length) return;
      if (lyricFor === rid) return;   // 本首只请求一次, 失败不刷屏
      lyricFor = rid;
      put({ type: 'async:lyric/fetchLyric', payload: { force: true } });
    } catch (e) {}
  }

  /* ----------------------------------------------------- 「小窗」按钮 注入 */
  var btnEl = null, toggleCount = 0;
  function hostForButton() {
    // Prefer the native transport group; the right-hand tool group is the fallback for older builds.
    // Never append directly to footer: during early injection that creates a permanent extra row.
    var h = document.querySelector('footer .cmd-space.btns') || document.querySelector('footer .cmd-space.right') ||
            document.querySelector('.cmd-space.btns') || document.querySelector('.cmd-space.right');
    if (h) return h;
    var q = btnQueue();
    return q && q.parentElement ? q.parentElement : null;
  }
  function ensureButtonStyle() {
    if (document.getElementById('ncmmini-inline-style')) return;
    var s = document.createElement('style');
    s.id = 'ncmmini-inline-style';
    s.textContent =
      'button.cmd-button.ncmmini-btn{-webkit-appearance:none;appearance:none;display:inline-flex!important;' +
      '-webkit-box-align:center;-webkit-align-items:center;align-items:center;-webkit-box-pack:center;' +
      '-webkit-justify-content:center;justify-content:center;box-sizing:border-box;width:24px!important;' +
      'min-width:24px!important;max-width:24px!important;height:28px!important;padding:0!important;margin:0;' +
      'flex:0 0 24px!important;border:0;border-radius:6px;background:transparent;color:inherit;cursor:pointer;' +
      'outline:none;vertical-align:middle;opacity:.82;user-select:none;-webkit-user-select:none}' +
      'button.cmd-button.ncmmini-btn:hover{opacity:1;color:#e54d4d;background:rgba(128,128,128,.12)}' +
      'button.cmd-button.ncmmini-btn:active{transform:translateY(1px)}' +
      'button.cmd-button.ncmmini-btn.ncmmini-on{opacity:1;color:#e54d4d;background:rgba(229,77,77,.13)}' +
      'button.cmd-button.ncmmini-btn svg.ncmmini-ico{display:block;width:16px;height:16px;pointer-events:none}';
    (document.head || document.documentElement).appendChild(s);
  }
  function decorateButton(b) {
    b.classList.add('cmd-button-with-icon');
    b.classList.add('cmd-button-with-icon-only');
    b.title = '小窗';
    b.setAttribute('data-testid', 'ncmmini-toggle');
    b.setAttribute('aria-label', '小窗');
    if (!b.querySelector('svg.ncmmini-ico')) {
      b.textContent = '';
      b.innerHTML = '<svg class="ncmmini-ico" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true" focusable="false"><rect x="3" y="5" width="18" height="14" rx="2"></rect><rect x="12" y="11" width="7" height="5" rx="1"></rect></svg>';
    }
    return b;
  }
  function placeButton(b, host) {
    if (host && b.parentElement !== host) { try { host.appendChild(b); } catch (e) {} }
    // Hide a legacy footer fallback until a real native host appears; it must not add a row.
    b.style.display = host && b.parentElement === host ? '' : 'none';
    return decorateButton(b);
  }
  function ensureButton() {
    ensureButtonStyle();
    var host = hostForButton();
    if (btnEl && document.body && document.body.contains(btnEl)) return placeButton(btnEl, host);
    if (!host) return null;       // native controls are not mounted yet; loopBeat retries in 1s
    var old = null;
    try { old = host.querySelector('button.ncmmini-btn') || document.querySelector('button.ncmmini-btn'); } catch (e) {}
    if (old && document.body.contains(old)) { btnEl = old; return placeButton(btnEl, host); }
    var b = document.createElement('button');
    b.type = 'button';
    b.className = 'cmd-button ncmmini-btn';
    decorateButton(b);
    b.addEventListener('click', function (e) { try { e.preventDefault(); e.stopPropagation(); } catch (x) {} API.dom.toggle(); }, false);
    try { host.appendChild(b); } catch (e) { return null; }
    btnEl = b;
    return placeButton(btnEl, host);
  }
  function reflectToggleState(on) {
    try {
      if (btnEl) btnEl.classList[on ? 'add' : 'remove']('ncmmini-on');
      document.documentElement.setAttribute('data-ncmmini-toggles', String(toggleCount));
      document.documentElement.setAttribute('data-ncmmini-online', http.port ? String(http.port) : '0');
    } catch (e) {}
  }

  /* ------------------------------------------------------------ ncmmini:// */
  var lastKick = 0;
  function protocolKnown() {
    if (http.everOk) return true;
    try { return lsGet(LS_PROTO) === '1'; } catch (e) { return false; }
  }
  function selfStart(reason) {
    if (SELF_START_MODE === 'off') return false;   // CFG.selfStartMode='off': 完全禁用协议自启(自测/集成用)
    var now = Date.now();
    if (http.port) return false;                     // 小窗在线, 无需自启
    var prev = num(lsGet(LS_KICK)) || 0;
    if (now - prev < KICK_COOLDOWN || now - lastKick < KICK_COOLDOWN) return false;  // 防抖: 一次/60s
    lastKick = now; lsSet(LS_KICK, now);
    stats.kick++;
    dlog('selfStart', reason);
    if (SELF_START_MODE === 'href' && protocolKnown()) {
      try { window.location.href = 'ncmmini://start'; return true; } catch (e) {}
    }
    // 默认路径: 隐藏 iframe 触发外部协议, 避免未注册时把 SPA 主页面导航成错误页
    try {
      var f = document.createElement('iframe');
      f.src = 'ncmmini://start';
      f.setAttribute('style', 'position:absolute;width:0;height:0;border:0;visibility:hidden;pointer-events:none');
      f.setAttribute('aria-hidden', 'true');
      (document.body || document.documentElement).appendChild(f);
      setTimeout(function () { try { f.parentNode && f.parentNode.removeChild(f); } catch (e) {} }, 5000);
      return true;
    } catch (e) { return false; }
  }

  /* ================== M2 通道: port.json 优先 + 10 端口并行扫 + 网络驱动节拍 ================== */
  var stats = { cmd: 0, err: 0, post: 0, kick: 0, online: 0, sweep: 0, sweepTimeout: 0, dedupe: 0, toggle: 0,
    portFileHit: 0, portFileMiss: 0, healthOk: 0, healthMiss: 0, wake: 0,
    last: null, lastAck: null, lastStateMs: 0, lastErr: null, lastRej: null };
  var http = { port: 0, fails: 0, everOk: false, inFlight: false, beat: 0, timer: 0, nextDue: 0, btnDue: 0,
    lastId: null, lastKey: null, lastKeyAt: 0, sweepAt: -1e9, sweeping: false, sweepPromise: null,
    stopped: false, portFilePort: 0, lastHealth: null, lastSweep: null };
  var DEFAULT_PORT_FILE = 'D:\\dsh工作区\\netease-mini\\runtime\\port.json';
  var LS_PORTFILE = 'ncmmini.portfile';
  function stateUrl(port) { return 'http://127.0.0.1:' + port + '/state'; }
  function healthUrl(port) { return 'http://127.0.0.1:' + port + '/health'; }
  function live() { return !http.stopped && window.__NCM_MINI__ === API; }
  function playGap() { var s = getSt(); return (s && s.playing && s.playing.playingState === 2) ? PLAY_MS : PAUSE_MS; }
  function runNow() {
    if (!live() || http.inFlight) return;
    if (http.port) postOnce();
    else if (nowMs() - http.sweepAt >= SWEEP_MS) sweep('loop');
    else arm(SWEEP_MS - (nowMs() - http.sweepAt));
  }
  /* arm = 精确定时器(250/1500ms 节奏靠它); POST 完成后再排下一次, 定时器嵌套链被网络回调打断 -> 不受隐藏页嵌套定时器限流 */
  function arm(ms) {
    ms = Math.max(0, ms || 0);
    http.nextDue = nowMs() + ms;
    if (http.timer) clearTimeout(http.timer);
    http.timer = setTimeout(function () { http.timer = 0; if (live()) runNow(); }, Math.max(20, ms));
  }
  function stateBody() {
    var o;
    try { o = readState(); } catch (e) { stats.err++; o = { v: 1, src: 'ncm', ok: false, err: 'state' }; }
    if (ACK && ACKQ.length) { o.ack = ACKQ[ACKQ.length - 1]; if (ACKQ.length > 1) o.acks = ACKQ.slice(0); ACKQ.length = 0; }
    try { return JSON.stringify(o); } catch (e2) { stats.err++; return '{}'; }
  }
  function oneShot(url, method, body, timeoutMs) {
    var ctl = null;
    try { if (window.AbortController) ctl = new AbortController(); } catch (e) { ctl = null; }
    var timer = 0;
    if (ctl) timer = setTimeout(function () { try { ctl.abort(); } catch (e) {} }, timeoutMs || 1500);
    var opts = { method: method, headers: body ? { 'Content-Type': 'application/json' } : {}, cache: 'no-store', credentials: 'omit', mode: 'cors' };
    if (body) opts.body = body;
    if (ctl) opts.signal = ctl.signal;
    return fetch(url, opts).then(function (r) {
      if (timer) clearTimeout(timer);
      if (!r || r.status < 200 || r.status >= 300) throw new Error('http ' + (r && r.status));
      return r.text();
    }, function (e) { if (timer) clearTimeout(timer); throw e; });
  }
  function postState(port, body) { return oneShot(stateUrl(port), 'POST', body, FETCH_TIMEOUT_MS); }
  function healthCheck(port) {
    return oneShot(healthUrl(port), 'GET', null, PROBE_TIMEOUT_MS).then(function (t) {
      stats.healthOk++; var o = null; try { o = JSON.parse(t); } catch (e) {}
      http.lastHealth = { port: port, ok: true, body: o }; return { port: port, ok: true, body: o };
    }, function (e) { stats.healthMiss++; http.lastHealth = { port: port, ok: false, err: clip(e && e.message, 60) }; return { port: port, ok: false }; });
  }
  function attach(port, via) {
    var was = http.port;
    http.port = port; http.fails = 0; http.everOk = true; stats.online++;
    lsSet(LS_PORT, port); lsSet(LS_PROTO, '1');
    try { document.documentElement.setAttribute('data-ncmmini-online', String(port)); } catch (e) {}
    if (was !== port) dlog('attach', port, via);
  }
  function parsePortFile(txt) {
    try {
      var o = (typeof txt === 'string') ? JSON.parse(txt) : txt;
      var p = num(o && o.port);
      if (!p || p < 1 || p > 65535) return null;
      return { port: p, pid: num(o.pid), backend: o.backend || null };
    } catch (e) { return null; }
  }
  /* runtime\port.json 由小窗写, 页面读不到本地文件 -> 三种递交方式(优先级从高到低):
     1) window.__NCM_MINI_PORT_FILE__ = '{"port":46211,...}'   (小窗 CDP 注入, 推荐)
     2) CFG.portFileInline = <同上的字符串或对象>
     3) CFG.portFile 给一个 http(s):// 或 file:// 可 fetch 的 URL, 由页面自己拉(默认值是本地路径, 拉不到就记 stats.portFileMiss)
     拉到的内容同时缓存进 localStorage('ncmmini.portfile'). */
  function readPortFile() {
    var cand = [];
    try { if (window.__NCM_MINI_PORT_FILE__) cand.push(window.__NCM_MINI_PORT_FILE__); } catch (e) {}
    try { if (CFG.portFileInline) cand.push(CFG.portFileInline); } catch (e) {}
    try { if (CFG.portFileCache !== false) { var cached = lsGet(LS_PORTFILE); if (cached) cand.push(cached); } } catch (e) {}
    for (var i = 0; i < cand.length; i++) { var p = parsePortFile(cand[i]); if (p) { http.portFilePort = p.port; stats.portFileHit++; return Promise.resolve(p); } }
    var url = null;
    try { var f = CFG.portFile || DEFAULT_PORT_FILE; if (/^(https?:|file:)/i.test(String(f))) url = String(f); } catch (e2) {}
    if (!url) { stats.portFileMiss++; return Promise.resolve(null); }
    return oneShot(url, 'GET', null, PROBE_TIMEOUT_MS).then(function (t) {
      var p = parsePortFile(t);
      if (p) { stats.portFileHit++; http.portFilePort = p.port; try { lsSet(LS_PORTFILE, t); } catch (e) {} }
      else stats.portFileMiss++;
      return p;
    }, function () { stats.portFileMiss++; return null; });
  }
  /* 一次扫描: 全部候选端口并行 POST /state(串行 200ms/口 -> 并行一个 RTT), 首个应答者胜出 */
  function sweep(reason) {
    if (http.sweeping && http.sweepPromise) return http.sweepPromise;
    http.sweeping = true; stats.sweep++;
    var t0 = nowMs(), body = stateBody(), res, settled = false;
    var p = new Promise(function (r) { res = r; });
    http.sweepPromise = p;
    function done(via) {
      if (settled) return; settled = true;
      http.sweeping = false; http.sweepPromise = null; http.sweepAt = nowMs();
      arm(http.port ? playGap() : SWEEP_MS);
      var r = { ok: !!http.port, port: http.port, err: http.port ? null : 'no-listener', via: via || (http.port ? 'sweep' : null),
        ms: Math.round(nowMs() - t0), reason: reason || null };
      http.lastSweep = r; return r;
    }
    readPortFile().then(function (pf) {
      var ports = PORTS.slice();
      if (pf && pf.port) { var i0 = ports.indexOf(pf.port); if (i0 >= 0) ports.splice(i0, 1); ports.unshift(pf.port); healthCheck(pf.port); }
      var pref = num(lsGet(LS_PORT));
      if (pref && ports.indexOf(pref) > 1) { var j = ports.indexOf(pref); if (j > 0) { ports.splice(j, 1); ports.splice(pf ? 1 : 0, 0, pref); } }
      var pending = ports.length, firstOther = 0, hintWaitTimer = 0;
      setTimeout(function () { if (!settled) { stats.sweepTimeout++; done('guard-timeout'); } }, Math.max(2000, PROBE_TIMEOUT_MS + 1800));
      ports.forEach(function (port) {
        postState(port, body).then(function (txt) {
          if (port === http.portFilePort) {                       // hint 端口回话: 立刻定它
            if (hintWaitTimer) { clearTimeout(hintWaitTimer); hintWaitTimer = 0; }
            if (!http.port) attach(port, 'portfile');
            try { handleCommand(txt); } catch (e) { stats.err++; }
            done('portfile'); --pending; return;
          }
          if (!http.port && !firstOther) firstOther = port;
          if (http.portFilePort && !hintWaitTimer && !http.port) {  // 有 hint 且 hint 还没回话: 等 400ms 再落到别的端口
            hintWaitTimer = setTimeout(function () {
              hintWaitTimer = 0;
              if (!http.port && firstOther) { attach(firstOther, 'sweep-hint-timeout'); try { handleCommand(''); } catch (e) {} }
              done(http.port ? (http.port === http.portFilePort ? 'portfile' : 'sweep') : null);
            }, CFG.hintWaitMs === undefined ? 400 : CFG.hintWaitMs);
          } else if (!http.port) {
            attach(port, 'sweep');
            try { handleCommand(txt); } catch (e2) { stats.err++; }
            done('sweep');
          }
          --pending;
        }, function () { http.fails++; if (--pending <= 0 && !settled) done(null); });
      });
    }, function () { stats.err++; done(null); });
    return p;
  }
  function handleCommand(text) {
    if (!text) return;
    var c;
    try { c = JSON.parse(text); } catch (e) { return; }
    if (!c || typeof c !== 'object' || !c.cmd) return;
    var nid = num(c.id);
    if (nid !== null) {                                   // 有 id: 按 id 去重
      if (String(nid) === String(http.lastId)) return;
      http.lastId = nid;
    } else {                                              // 无 id: 按 内容+时间窗 去重(防同一条 toggle 被连发两次而翻回原状)
      var key = c.cmd + '|' + JSON.stringify(c.arg === undefined ? null : c.arg);
      if (key === http.lastKey && (nowMs() - http.lastKeyAt) <= CMD_DEDUPE_MS) { stats.dedupe++; return; }
      http.lastKey = key; http.lastKeyAt = nowMs();
    }
    cmdAsync(c.cmd, c.arg, { id: (c.id === undefined ? null : c.id), src: 'http' }).catch(function () { stats.err++; });
  }
  function postOnce() {
    http.inFlight = true;
    var playing = false;
    try { playing = !!(readState().playing); } catch (e) {}
    postState(http.port, stateBody()).then(function (txt) {
      http.inFlight = false; http.fails = 0; stats.post++;
      try { handleCommand(txt); } catch (e) { stats.err++; }
      arm(playGap());                      // 间隔由"本次采样时的 playing"决定, 且在网络回调里续期(不受隐藏页嵌套定时器限流)
    }, function () {
      http.inFlight = false; http.fails++;
      if (http.fails >= 2) { http.port = 0; lsSet(LS_PORT, 0); arm(0); selfStart('lost'); }
      else arm(200);
    });
  }
  /* 看门狗: 只兜底(比精确定时器晚 250ms 以上才补火), 顺带做按钮/歌词预热 */
  function loopBeat() {
    http.beat = 0;
    if (!live()) return;
    try {
      if (http.btnDue <= nowMs()) { ensureButton(); http.btnDue = nowMs() + 1000; }
      ensureLyric();
      try { document.documentElement.setAttribute('data-ncmmini-online', http.port ? String(http.port) : '0'); } catch (e) {}
      if (nowMs() >= http.nextDue + WATCHDOG_MS + 50) runNow();
    } catch (e) { stats.err++; stats.lastErr = 'beat:' + clip(e && e.message, 100); arm(500); }
    if (live() && !http.stopped) http.beat = setTimeout(loopBeat, WATCHDOG_MS);
  }
  function startBeat() { if (http.beat) clearTimeout(http.beat); http.beat = setTimeout(loopBeat, WATCHDOG_MS); }
  function wake(source) {
    if (!live()) return;
    stats.wake++;
    if (http.port) { if (nowMs() >= http.nextDue) arm(0); }
    else if (nowMs() - http.sweepAt > 600) sweep('wake:' + source);
  }
  var wakeAt = 0;
  function wakeThrottled(source) { if (nowMs() - wakeAt < 1000) return; wakeAt = nowMs(); wake(source); }
  function connect() {
    return new Promise(function (resolve) {
      try {
        if (!live()) { resolve({ ok: false, port: 0, err: 'retired' }); return; }
        http.fails = 0;
        var goSweep = function (why) {
          http.sweepAt = -1e9; http.sweeping = false; http.sweepPromise = null;
          sweep(why).then(function (r) { resolve(r); }, function (e) { stats.err++; resolve({ ok: false, port: 0, err: clip(e && e.message, 100) }); });
        };
        if (http.port) {
          var p = http.port;
          postState(p, stateBody()).then(function (txt) {            // 缓存端口先探一次, 探活才算数
            try { handleCommand(txt); } catch (e) { stats.err++; }
            stats.post++; arm(playGap());
            resolve({ ok: true, port: p, err: null, via: 'cached-probe' });
          }, function () { http.port = 0; http.fails = 0; lsSet(LS_PORT, 0); goSweep('connect:stale-port'); });
          return;
        }
        goSweep('connect');
      } catch (e) { stats.err++; resolve({ ok: false, port: 0, err: clip(e && e.message, 100) }); }
    });
  }

  /* ================================ 公开接口 ================================ */
  var API = {
    version: '1',
    build: BUILD,                       // M1: 与磁盘文件一一对应
    state: function () { var t0 = nowMs(); var s = safeState(); stats.lastStateMs = Math.round(nowMs() - t0); return s; },
    cmd: function (name, arg) { return cmdAsync(name, arg, { src: 'api' }); },   // -> Promise<Result>
    cmdSync: function (name, arg) { return cmdSync(name, arg); },                 // 同步版: 未落地 err='pending'
    dom: {
      toggle: function () {
        toggleCount++; stats.toggle = toggleCount;
        var b = ensureButton();
        var on2 = (toggleCount % 2) === 1;
        try { if (b) b.classList[on2 ? 'add' : 'remove']('ncmmini-on'); } catch (e) {}
        try { document.documentElement.setAttribute('data-ncmmini-toggles', String(toggleCount)); } catch (e) {}
        try { window.dispatchEvent(new CustomEvent('ncmmini:toggle', { detail: { count: toggleCount, online: !!http.port, build: BUILD } })); } catch (e2) {}
        // M1b: 「小窗」按钮 = overlay 开关。fire-and-forget POST /ctl/toggle(show<->hide)，失败静默；
        // 结果写进 stats.lastCtlToggle 供 CDP 回读取证。不触碰 like/命令链路，仅此一处分叉。
        if (http.port) { try { fetch('http://127.0.0.1:' + http.port + '/ctl/toggle', { method: 'POST', cache: 'no-store', credentials: 'omit' }).then(function (r) { stats.lastCtlToggle = 'ok:' + (r && r.status) + ' n=' + toggleCount + ' @' + Math.round(nowMs()); dlog('ctl/toggle', r && r.status); }, function (e3) { stats.lastCtlToggle = 'err:' + clip(e3 && e3.message, 40) + ' n=' + toggleCount; }); } catch (e4) { stats.lastCtlToggle = 'throw:' + clip(e4 && e4.message, 40) + ' n=' + toggleCount; } }
        if (!http.port) { http.sweepAt = -1e9; sweep('dom.toggle'); selfStart('dom.toggle'); }
        return undefined;
      }
    },
    /* ---- 契约之外的诊断附加项 ---- */
    stats: stats,
    info: function () {
      return { build: BUILD, prevBuild: PREV_BUILD, retired: RETIRE_LOG.slice(), port: http.port, portFilePort: http.portFilePort,
        lastHealth: http.lastHealth, lastSweep: http.lastSweep, everOk: http.everOk, fails: http.fails, inFlight: http.inFlight,
        nextDueIn: Math.round(http.nextDue - nowMs()), sweepAge: Math.round(nowMs() - http.sweepAt), toggles: toggleCount,
        kick: stats.kick, store: !!store(), btn: !!(btnEl && document.body && document.body.contains(btnEl)),
        hidden: !!document.hidden, vis: document.visibilityState };
    },
    last: function () { return stats.last; },
    lastAck: function () { return stats.lastAck; },
    connect: function () { return connect(); },              // -> Promise<{ok,port,err,via,ms,reason}>
    selfStart: function () { return selfStart('manual'); },
    queueOpen: function () { return queueOpen(); },
    rehost: function () {
      try { if (btnEl && btnEl.parentNode) btnEl.parentNode.removeChild(btnEl); } catch (e) {}
      btnEl = null;
      return !!ensureButton();
    },
    /* 打包后可用: 页面自己拉 orpheus://orpheus/pub/hybrid/mini.js 算整文件 sha256, 与磁盘侧对照 */
    selfHash: function (url) {
      var u = url || './hybrid/mini.js';
      if (!(window.crypto && window.crypto.subtle && window.crypto.subtle.digest)) return Promise.resolve({ ok: false, err: 'no SubtleCrypto' });
      try {
        return fetch(u, { cache: 'no-store', credentials: 'omit' }).then(function (r) {
          if (!r || r.status < 200 || r.status >= 300) return { ok: false, err: 'http ' + (r && r.status), url: String((r && r.url) || u) };
          return r.arrayBuffer().then(function (buf) {
            return window.crypto.subtle.digest('SHA-256', buf).then(function (d) {
              var h = '', a = new Uint8Array(d);
              for (var i = 0; i < a.length; i++) h += (a[i] < 16 ? '0' : '') + a[i].toString(16);
              return { ok: true, sha256: h.toUpperCase(), sha8: h.slice(0, 8).toUpperCase(), bytes: buf.byteLength, url: u };
            });
          });
        }, function (e) { return { ok: false, err: clip(e && e.message, 100), url: u }; });
      } catch (e2) { return Promise.resolve({ ok: false, err: clip(e2 && e2.message, 100) }); }
    }
  };

  /* ---------------------------------------------------- 自身异常计数(可观测) */
  try {
    window.addEventListener('error', function (e) { stats.err++; stats.lastErr = String((e && e.message) || '') + ' @' + clip(e && e.filename, 60); }, true);
    window.addEventListener('unhandledrejection', function (e) { stats.err++; stats.lastRej = String(e && e.reason && (e.reason.message || e.reason)); }, true);
  } catch (e) {}

  /* ============================ 安装 / 启动 / 停止 ============================ */
  var EV = [];
  function on(target, type, fn) { try { target.addEventListener(type, fn, true); EV.push([target, type, fn]); } catch (e) {} }
  function offAll() { for (var i = 0; i < EV.length; i++) { try { EV[i][0].removeEventListener(EV[i][1], EV[i][2], true); } catch (e) {} } EV.length = 0; }
  function stopAll() {
    http.stopped = true;
    if (http.beat) { clearTimeout(http.beat); http.beat = 0; }
    if (http.timer) { clearTimeout(http.timer); http.timer = 0; }
    http.inFlight = false; http.sweeping = false; http.sweepPromise = null;
    offAll();
    try { var extra = document.querySelectorAll('button.ncmmini-btn'); for (var i = 0; i < extra.length; i++) extra[i].parentNode.removeChild(extra[i]); } catch (e) {}
    btnEl = null;
    try { if (window.__NCM_MINI__ === API) delete window.__NCM_MINI__; } catch (e) { if (window.__NCM_MINI__ === API) window.__NCM_MINI__ = undefined; }
    try { if (window.__NCM_MINI_STOP__ === stopAll) delete window.__NCM_MINI_STOP__; } catch (e) { if (window.__NCM_MINI_STOP__ === stopAll) window.__NCM_MINI_STOP__ = null; }
  }
  /* M5 顺序: 先退休旧实例(旧 stop + 抢走全局名 -> 旧 live() 守卫失效) -> 再登记新 stop -> 再挂事件/首轮扫描 */
  retirePrev();
  window.__NCM_MINI_STOP__ = stopAll;
  API.stop = stopAll;
  API.prevBuild = PREV_BUILD;
  try {
    _store = findStore();
    ensureButton();
    http.btnDue = nowMs() + 1000;
    arm(BOOT_SWEEP_MS);
    runNow();
    startBeat();                                                       // M2: boot 当帧就扫, 首轮扫描不晚于 0ms(<1500ms 要求)
    setTimeout(function () { if (live() && !http.port) sweep('firstRetry'); }, FIRST_RETRY_MS);
    on(document, 'visibilitychange', function () { if (document.visibilityState === 'visible') wake('visible'); });
    on(window, 'focus', function () { wake('focus'); });
    on(window, 'pointerdown', function () { wakeThrottled('pointer'); });
    on(window, 'keydown', function () { wakeThrottled('key'); });
    setTimeout(function () { if (live() && !http.port) selfStart('boot'); }, CFG.kickDelayMs || 3000);
  } catch (e) { stats.err++; stats.lastErr = 'boot:' + clip(e && e.message, 120); }
  if (CFG.debug) dlog('boot', BUILD, 'prev=' + PREV_BUILD, 'retire=' + RETIRE_LOG.join(','));
})();
//@@NCMMINI-FP@@ ncmmini-bridge-5ccdaf2e-060676
