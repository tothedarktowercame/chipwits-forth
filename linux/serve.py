#!/usr/bin/env python3
"""Browser front-end for the native ChipWits+ port.

GET  /       the page: canvas + menu bar
GET  /frame  the 512x342 1-bit framebuffer (21888 raw bytes)
POST /input  "type x y" -> appended to live/input.bin as 4x uint16 LE

Run via play.sh; stdlib only.
"""
import http.server, os, struct, sys

ROOT = os.path.dirname(os.path.abspath(__file__))
LIVE = os.path.join(ROOT, "live")
FRAME = os.path.join(LIVE, "frame.raw")
INPUT = os.path.join(LIVE, "input.bin")
PORT = int(sys.argv[1]) if len(sys.argv) > 1 else 8047

INDEX = """<!DOCTYPE html>
<meta charset="utf-8"><title>ChipWits</title>
<style>
 body { background:#333; color:#ddd; font:14px monospace; margin:12px; }
 #bar button { font:13px monospace; margin:1px; }
 details { display:inline-block; margin-right:10px; vertical-align:top; }
 summary { cursor:pointer; background:#555; padding:2px 10px; }
 details[open] { background:#444; }
 details div { position:absolute; background:#444; padding:4px; z-index:2; }
 details div button { display:block; width:100%; text-align:left; }
 canvas { image-rendering:pixelated; width:1024px; height:684px;
          background:#fff; display:block; margin-top:8px; }
 #status { margin-top:6px; color:#8c8; }
</style>
<div id="bar"></div>
<canvas id="c" width="512" height="342" tabindex="0"></canvas>
<div id="status">connecting...</div>
<script>
const MENUS = [
 ["Warehouse", 7, Array.from({length:16},(_,i)=>[i+1, "ChipWit "+(i+1)])],
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
    b.onclick = () => { send(5, num, item); d.open = false; };
    box.appendChild(b);
  }
  d.appendChild(box); bar.appendChild(d);
}
function send(t, x, y) {
  fetch("/input", {method:"POST", body: t + " " + Math.round(x) + " " + Math.round(y)});
}
const cv = document.getElementById("c"), cx = cv.getContext("2d");
const img = cx.createImageData(512, 342);
img.data.fill(255);
async function poll() {
  try {
    const r = await fetch("/frame", {cache:"no-store"});
    const buf = new Uint8Array(await r.arrayBuffer());
    if (buf.length === 21888) {
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
      document.getElementById("status").textContent =
        "live -- click the board, use the menus (Games > Start Mission)";
    }
  } catch (e) {
    document.getElementById("status").textContent = "waiting for game: " + e;
  }
  setTimeout(poll, 90);
}
poll();
function coords(e) {
  const r = cv.getBoundingClientRect();
  return [ (e.clientX - r.left) * 512 / r.width,
           (e.clientY - r.top) * 342 / r.height ];
}
let lastMove = 0;
cv.addEventListener("mousedown", e => { cv.focus(); send(1, ...coords(e)); });
cv.addEventListener("mouseup",   e => send(2, ...coords(e)));
cv.addEventListener("mousemove", e => {
  const now = Date.now();
  if (now - lastMove > 40) { lastMove = now; send(3, ...coords(e)); }
});
cv.addEventListener("keydown", e => {
  let c = 0;
  if (e.key.length === 1) c = e.key.charCodeAt(0);
  else if (e.key === "Enter") c = 13;
  else if (e.key === "Backspace") c = 8;
  if (c) { e.preventDefault(); send(4, c, 0); }
});
</script>
"""

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
        elif self.path.startswith("/frame"):
            try:
                data = open(FRAME, "rb").read()
            except OSError:
                data = b""
            if len(data) != 21888:
                data = b"\x00" * 21888
            self._send(200, "application/octet-stream", data)
        else:
            self._send(404, "text/plain", b"not found")
    def do_POST(self):
        if self.path == "/input":
            n = int(self.headers.get("Content-Length", 0))
            try:
                t, x, y = (int(v) for v in self.rfile.read(n).split())
                with open(INPUT, "ab") as f:
                    f.write(struct.pack("<HHHH", t & 0xffff, x & 0xffff, y & 0xffff, 0))
                self._send(204, "text/plain", b"")
            except Exception as e:
                self._send(400, "text/plain", str(e).encode())
        else:
            self._send(404, "text/plain", b"not found")

if __name__ == "__main__":
    os.makedirs(LIVE, exist_ok=True)
    srv = http.server.ThreadingHTTPServer(("0.0.0.0", PORT), H)
    print(f"ChipWits front-end on http://localhost:{PORT}")
    srv.serve_forever()
