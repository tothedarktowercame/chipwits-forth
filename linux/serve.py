#!/usr/bin/env python3
"""Browser front-end for the native ChipWits+ port.

GET  /       the page: canvas + menu bar
GET  /ws     websocket: the server pushes binary messages --
               0x01 + 21888 bytes   a new 512x342 1-bit frame
               0x02 + n*8 bytes     notes (duration volume freq*10 pad, u16 LE)
               0x03 + JSON          the 16 robot names (Warehouse menu)
             and the page sends text "type x y" events
GET  /frame  the framebuffer (polling fallback when websockets are blocked)
POST /input  "type x y" (polling fallback)

Events go to live/input.bin as 4x uint16 LE; frames come from
live/frame.raw, notes from live/sound.bin (both written by live.fs).
Run via play.sh; stdlib only.
"""
import base64, hashlib, http.server, json, os, socket, struct, sys, threading, time

ROOT = os.path.dirname(os.path.abspath(__file__))
LIVE = os.path.join(ROOT, "live")
FRAME = os.path.join(LIVE, "frame.raw")
INPUT = os.path.join(LIVE, "input.bin")
SOUND = os.path.join(LIVE, "sound.bin")
ROBOTS = os.path.join(ROOT, "data", "CW")
FRAME_LEN = 21888
PORT = int(sys.argv[1]) if len(sys.argv) > 1 else 8047

INDEX = """<!DOCTYPE html>
<meta charset="utf-8"><title>ChipWits</title>
<meta name="viewport" content="width=device-width, initial-scale=1">
<style>
 body { background:#333; color:#ddd; font:14px monospace; margin:12px; }
 #bar button { font:13px monospace; margin:1px; }
 details { display:inline-block; margin-right:10px; vertical-align:top; }
 summary { cursor:pointer; background:#555; padding:2px 10px; }
 details[open] { background:#444; }
 details div { position:absolute; background:#444; padding:4px; z-index:2; }
 details div button { display:block; width:100%; text-align:left; }
 canvas { image-rendering:pixelated; width:100%; max-width:1024px; height:auto;
          aspect-ratio:512/342; background:#fff; display:block; margin-top:8px;
          touch-action:none; }
 #status { margin-top:6px; color:#8c8; }
 #help { margin-top:10px; max-width:1024px; line-height:1.45; }
 #help summary { display:inline-block; }
 #help div { position:static; background:none; padding:6px 2px; }
 #help b { color:#fff; }
</style>
<div id="bar"></div>
<canvas id="c" width="512" height="342" tabindex="0"></canvas>
<div id="status">connecting...</div>
<details id="help" open><summary>How to play</summary><div>
<p>ChipWits is a programming game: you don't steer the robot, you
<b>program</b> it, then watch it run a mission on its own.</p>
<p><b>Watch first.</b> Games &gt; pick an adventure (Greedville is the
easiest), then Games &gt; Start / End Mission.  The robot runs the program
shown in the Debug window, scoring by eating food and collecting things
until it runs out of fuel or cycles, or takes too much damage.  Options &gt;
Debug/Stats swaps the program trace for your robot's statistics.</p>
<p><b>Pick a robot.</b> The Warehouse menu holds 16 saved ChipWits (Doug
Sharp's originals are Greedy, Mr. CW and Buddy; the rest are blank slots).</p>
<p><b>Program it.</b> End any running mission, then Workshop &gt; Enter.  The
robot's main panel (A) is on the left; letters above it switch to subpanels.
To burn a chip: click an empty socket, click an <b>operator</b> in the
Operators window, then, if it needs one, an <b>argument</b> in the Arguments
window.  Click an output wire, then a new spot, to re-route it.  Drag a
chip to move it; drag it off the panel to delete it.  The traffic light
(GO) is where the panel starts.</p>
<p><b>Save.</b> Workshop &gt; Save ChipWit, type a name, tick the adventures
it's meant for, then OK (or Return).  Workshop &gt; Enter / Leave goes back
to the game.</p>
<p>Sound starts after your first click or key.  Options &gt; Quit ends the
game process.</p>
</div></details>
<script>
const MENUS = [
 ["Warehouse", 7, Array.from({length:16},(_,i)=>[i+1, "ChipWit "+(i+1)])],   // renamed live
 ["Workshop", 6, [[1,"Enter / Leave"],[3,"Save ChipWit"],[5,"Cut Panel"],
                  [6,"Copy Panel"],[7,"Paste Panel"],[8,"Clear Panel"]]],
 ["Games", 8, [[1,"Start / End Mission"],[2,"Series"],
   [4,"Greedville"],[5,"ChipWit Caves"],[6,"Doom Rooms"],[7,"Peace Paths"],
   [8,"Memory Lanes"],[9,"Octopus Garden"],[10,"Mystery Matrix"],[11,"Boomtown"]]],
 ["Options", 5, [[3,"Sound On/Off"],[4,"Debug/Stats"],[5,"Quit"]]],
];
const bar = document.getElementById("bar");
for (const [name, num, items] of MENUS) {
  const d = document.createElement("details");
  d.innerHTML = "<summary>" + name + "</summary>";
  const box = document.createElement("div");
  for (const [item, label] of items) {
    const b = document.createElement("button");
    b.textContent = label;
    b.onclick = () => { wakeAudio(); send(5, num, item); d.open = false; };
    if (num === 7) b.id = "wh" + item;
    box.appendChild(b);
  }
  d.appendChild(box); bar.appendChild(d);
}
const cv = document.getElementById("c"), cx = cv.getContext("2d");
const img = cx.createImageData(512, 342);
img.data.fill(255);
function show(buf) {
  const d = img.data;
  for (let i = 0; i < 21888; i++) {
    const byte = buf[i];
    for (let b = 0; b < 8; b++) {
      const v = (byte >> (7 - b)) & 1 ? 0 : 255;
      const o = (i * 8 + b) * 4;
      d[o] = d[o+1] = d[o+2] = v;
    }
  }
  cx.putImageData(img, 0, 0);
}
const status = t => document.getElementById("status").textContent = t;
const LIVE = "live -- click the board, use the menus (Games > Start / End Mission)";

// sound: the game's square-wave notes, played back to back as queued.
// Browsers only allow audio after a click or key, so it starts on the first.
let actx = null, nextAt = 0;
function wakeAudio() {
  if (!actx) actx = new (window.AudioContext || window.webkitAudioContext)();
  if (actx.state === "suspended") actx.resume();
}
function play(notes) {
  if (!actx || actx.state !== "running") return;
  const dv = new DataView(notes.buffer, notes.byteOffset, notes.byteLength);
  for (let o = 0; o + 8 <= notes.byteLength; o += 8) {
    const dur = dv.getUint16(o, true) / 60, vol = dv.getUint16(o + 2, true),
          hz = dv.getUint16(o + 4, true) / 10;
    const t = Math.max(actx.currentTime, nextAt);
    nextAt = t + dur;
    if (hz < 1 || vol === 0) continue;
    const osc = actx.createOscillator(), g = actx.createGain();
    osc.type = "square"; osc.frequency.value = hz;
    g.gain.value = 0.12 * Math.min(vol, 255) / 255;
    osc.connect(g).connect(actx.destination);
    osc.start(t); osc.stop(t + dur);
  }
}

// transport: websocket push, or HTTP polling if the socket can't be had
let ws = null;
function send(t, x, y) {
  const msg = t + " " + Math.round(x) + " " + Math.round(y);
  if (ws && ws.readyState === 1) ws.send(msg);
  else fetch("input", {method:"POST", body: msg});
}
function connect() {
  const url = (location.protocol === "https:" ? "wss://" : "ws://") +
              location.host + location.pathname.replace(/[^/]*$/, "") + "ws";
  let opened = false;
  ws = new WebSocket(url);
  ws.binaryType = "arraybuffer";
  ws.onopen = () => { opened = true; status(LIVE); };
  ws.onmessage = e => {
    const m = new Uint8Array(e.data);
    if (m[0] === 1 && m.length === 21889) show(m.subarray(1));
    else if (m[0] === 2) play(m.subarray(1));
    else if (m[0] === 3) {                  // robot names from the saved-robots file
      JSON.parse(new TextDecoder().decode(m.subarray(1))).forEach((n, i) => {
        const b = document.getElementById("wh" + (i + 1));
        if (b) b.textContent = (i + 1) + "  " + (n || "(empty)");
      });
    }
  };
  ws.onclose = () => {
    ws = null;
    if (opened) { status("reconnecting..."); setTimeout(connect, 1000); }
    else poll();                      // never opened: fall back for good
  };
}
async function poll() {
  try {
    const r = await fetch("frame", {cache:"no-store"});
    const buf = new Uint8Array(await r.arrayBuffer());
    if (buf.length === 21888) { show(buf); status(LIVE + " [polling, no sound]"); }
  } catch (e) {
    status("waiting for game: " + e);
  }
  setTimeout(poll, 90);
}
connect();
function coords(e) {
  const r = cv.getBoundingClientRect();
  return [ (e.clientX - r.left) * 512 / r.width,
           (e.clientY - r.top) * 342 / r.height ];
}
let lastMove = 0;
// pointer events: mouse, pen and touch alike (a finger can drag chips)
cv.addEventListener("pointerdown", e => {
  e.preventDefault(); cv.focus(); wakeAudio();
  cv.setPointerCapture(e.pointerId); send(1, ...coords(e));
});
cv.addEventListener("pointerup",   e => send(2, ...coords(e)));
cv.addEventListener("pointermove", e => {
  const now = Date.now();
  if (now - lastMove > 40) { lastMove = now; send(3, ...coords(e)); }
});
cv.addEventListener("keydown", e => {
  let c = 0;
  if (e.key.length === 1) c = e.key.charCodeAt(0);
  else if (e.key === "Enter") c = 13;
  else if (e.key === "Backspace") c = 8;
  if (c) { e.preventDefault(); wakeAudio(); send(4, c, 0); }
});
</script>
"""

INPUT_LOCK = threading.Lock()

def post_event(text):
    t, x, y = (int(v) for v in text.split())
    with INPUT_LOCK, open(INPUT, "ab") as f:
        f.write(struct.pack("<HHHH", t & 0xffff, x & 0xffff, y & 0xffff, 0))

def robot_names():
    """The 16 names from the saved-robots file: stats records of 108 bytes
    after the 17 program slots (960 bytes each), each starting with a
    big-endian length and the characters (screens 160-162)."""
    try:
        b = open(ROBOTS, "rb").read()
    except OSError:
        return None
    names = []
    for i in range(16):
        rec = 960 * 17 + 108 * i
        n = min(struct.unpack(">H", b[rec:rec + 2])[0], 18)
        names.append(b[rec + 2:rec + 2 + n].decode("mac_roman", "replace"))
    return names

def read_frame():
    try:
        data = open(FRAME, "rb").read()
    except OSError:
        return None
    return data if len(data) == FRAME_LEN else None

# ---------- minimal RFC 6455 server side: enough for one page ----------
WS_GUID = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11"

def ws_frame(payload, opcode=0x2):
    n = len(payload)
    if n < 126:
        head = struct.pack("!BB", 0x80 | opcode, n)
    elif n < 65536:
        head = struct.pack("!BBH", 0x80 | opcode, 126, n)
    else:
        head = struct.pack("!BBQ", 0x80 | opcode, 127, n)
    return head + payload

def ws_recv(sock):
    """(opcode, payload) of the next client frame, or None when closed."""
    def exact(n):
        buf = b""
        while len(buf) < n:
            chunk = sock.recv(n - len(buf))
            if not chunk:
                raise ConnectionError
            buf += chunk
        return buf
    try:
        b0, b1 = exact(2)
        n = b1 & 0x7f
        if n == 126:
            n = struct.unpack("!H", exact(2))[0]
        elif n == 127:
            n = struct.unpack("!Q", exact(8))[0]
        mask = exact(4) if b1 & 0x80 else b"\0\0\0\0"
        data = bytes(c ^ mask[i & 3] for i, c in enumerate(exact(n)))
        return b0 & 0x0f, data
    except (ConnectionError, OSError):
        return None


class H(http.server.BaseHTTPRequestHandler):
    def log_message(self, *a): pass
    def _send(self, code, ctype, body):
        self.send_response(code)
        self.send_header("Content-Type", ctype)
        self.send_header("Content-Length", str(len(body)))
        self.send_header("Cache-Control", "no-store")
        self.end_headers()
        self.wfile.write(body)
    def do_GET(self):
        if self.path == "/" or self.path.startswith("/index"):
            self._send(200, "text/html; charset=utf-8", INDEX.encode())
        elif self.path == "/ws" and "websocket" in self.headers.get("Upgrade", "").lower():
            self.websocket()
        elif self.path.startswith("/frame"):
            self._send(200, "application/octet-stream", read_frame() or b"\x00" * FRAME_LEN)
        else:
            self._send(404, "text/plain", b"not found")
    def do_POST(self):
        if self.path == "/input":
            n = int(self.headers.get("Content-Length", 0))
            try:
                post_event(self.rfile.read(n).decode())
                self._send(204, "text/plain", b"")
            except Exception as e:
                self._send(400, "text/plain", str(e).encode())
        else:
            self._send(404, "text/plain", b"not found")

    def websocket(self):
        key = self.headers.get("Sec-WebSocket-Key", "")
        accept = base64.b64encode(hashlib.sha1((key + WS_GUID).encode()).digest()).decode()
        self.send_response(101)
        self.send_header("Upgrade", "websocket")
        self.send_header("Connection", "Upgrade")
        self.send_header("Sec-WebSocket-Accept", accept)
        self.end_headers()
        self.close_connection = True
        sock, alive, send_lock = self.connection, [True], threading.Lock()

        def send(payload, opcode=0x2):
            with send_lock:
                sock.sendall(ws_frame(payload, opcode))

        def reader():                       # page -> game events
            while alive[0]:
                msg = ws_recv(sock)
                if msg is None or msg[0] == 0x8:
                    break
                op, data = msg
                if op == 0x9:
                    send(data, 0xA)
                elif op == 0x1:
                    try:
                        post_event(data.decode())
                    except ValueError:
                        pass
            alive[0] = False
        threading.Thread(target=reader, daemon=True).start()

        # game -> page: a frame whenever it changes, notes as they are queued
        # (only new ones: a page that connects late hears nothing stale)
        last, names = None, None
        try:
            snd_off = os.path.getsize(SOUND)
        except OSError:
            snd_off = 0
        try:
            while alive[0]:
                try:
                    size = os.path.getsize(SOUND)
                except OSError:
                    size = 0
                if size < snd_off:          # a new game truncated it
                    snd_off = 0
                if size - snd_off >= 8:
                    with open(SOUND, "rb") as f:
                        f.seek(snd_off)
                        notes = f.read((size - snd_off) // 8 * 8)
                    snd_off += len(notes)
                    send(b"\x02" + notes)
                n = robot_names()
                if n is not None and n != names:
                    names = n
                    send(b"\x03" + json.dumps(n).encode())
                frame = read_frame()
                if frame is not None and frame != last:
                    last = frame
                    send(b"\x01" + frame)
                time.sleep(0.02)
        except OSError:
            pass
        alive[0] = False

if __name__ == "__main__":
    os.makedirs(LIVE, exist_ok=True)
    # loopback by default: the page has no auth of its own.  Put a proxy
    # with auth in front, tunnel with ssh -L, or CHIPWITS_BIND=0.0.0.0.
    bind = os.environ.get("CHIPWITS_BIND", "127.0.0.1")
    srv = http.server.ThreadingHTTPServer((bind, PORT), H)
    srv.daemon_threads = True
    print(f"ChipWits front-end on http://localhost:{PORT}")
    srv.serve_forever()
