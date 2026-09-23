#!/usr/bin/env python3
"""Pull the resources the game needs out of the recovered CW+ disk image.

The shipped application's data fork is empty; its PICTs live in the resource
fork, which survives only inside the disk image.  So do the system fonts.

  PICT 101-108  per-adventure wall/floor tiles (new.interior, screen 191)
  PICT 110      the "Ready when you are" panel (Init.ChipWits, screen 073)
  FONT          Chicago 12 (the system font, textfont 0), Geneva 9/12,
                Monaco 9/12

Outputs, into data/ (all little-endian int16):
  pict-NNN.bin  t l b r rowBytes 0, then the picture rendered 1-bit at its
                picFrame, MSB = leftmost pixel, 1 = black
  font-NNN.bin  first last ascent descent leading height rowBytes kernMax,
                loc[last-first+3], ow[last-first+3] (0xFFFF = missing glyph,
                else offset<<8 | width), then the strike (height rows)
                NNN is the Mac FONT id: family*128 + size.

PICT decoding covers the version-1 opcodes these pictures use (clip region,
bit/pixel rects, comments) and refuses anything else rather than guess.
"""
import os, struct, sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
from macdisk import HFS, dc42_volume, resources

ROOT = os.path.join(HERE, "..")
DISK = os.path.join(ROOT, "..", "mac", "disks", "CW+ Copy 1", "CW+ Copy 1.dc42")
OUT = os.path.join(ROOT, "data")

PICTS = [101, 102, 103, 104, 105, 106, 107, 108, 110]
FONTS = {("System", 12): "Chicago 12", ("System", 393): "Geneva 9",
         ("System", 396): "Geneva 12", ("System", 521): "Monaco 9",
         ("ChipWits", 524): "Monaco 12"}


def unpackbits(src, pos, n):
    out = bytearray()
    while len(out) < n:
        c = src[pos]; pos += 1
        if c < 128:
            out += src[pos:pos + c + 1]; pos += c + 1
        elif c > 128:
            out += bytes([src[pos]]) * (257 - c); pos += 1
    return bytes(out[:n]), pos


def rect(d, p):
    return struct.unpack(">4h", d[p:p + 8])


def region_len(d, p):
    return struct.unpack(">H", d[p:p + 2])[0]


def render_pict(d):
    t, l, b, r = rect(d, 2)
    w, h = r - l, b - t
    canvas = [[0] * w for _ in range(h)]
    p = 10
    if d[p:p + 2] != b"\x11\x01":
        raise ValueError("not a version-1 PICT")
    p += 2
    while True:
        op = d[p]; p += 1
        if op == 0xFF:
            break
        elif op == 0x00:
            pass
        elif op == 0x01:                                   # clipRgn
            p += region_len(d, p)
        elif op == 0xA0:                                   # shortComment
            p += 2
        elif op == 0xA1:                                   # longComment
            p += 4 + struct.unpack(">H", d[p + 2:p + 4])[0]
        elif op in (0x90, 0x91, 0x98, 0x99):               # (Pack)Bits(Rect|Rgn)
            rb = struct.unpack(">H", d[p:p + 2])[0]
            if rb & 0x8000:
                raise ValueError("PixMap in a v1 PICT")
            bt, bl, bb, br = rect(d, p + 2)
            st, sl, sb, sr = rect(d, p + 10)
            dt, dl, db, dr = rect(d, p + 18)
            mode = struct.unpack(">h", d[p + 26:p + 28])[0]
            p += 28
            if op & 1:
                p += region_len(d, p)                      # mask region: ignored
            rows = []
            for _ in range(bb - bt):
                if op & 8 and rb >= 8:
                    n = d[p] if rb <= 250 else struct.unpack(">H", d[p:p + 2])[0]
                    p += 1 if rb <= 250 else 2
                    row, _ = unpackbits(d[p:p + n], 0, rb)
                    p += n
                else:
                    row = d[p:p + rb]; p += rb
                rows.append(row)
            if mode & 7 not in (0, 1):
                raise ValueError(f"transfer mode {mode} not handled")
            for y in range(db - dt):
                sy = st - bt + (y * (sb - st)) // (db - dt)
                for x in range(dr - dl):
                    sx = sl - bl + (x * (sr - sl)) // (dr - dl)
                    v = (rows[sy][sx >> 3] >> (7 - (sx & 7))) & 1
                    cy, cx = dt - t + y, dl - l + x
                    if 0 <= cy < h and 0 <= cx < w:
                        canvas[cy][cx] = v if mode & 7 == 0 else canvas[cy][cx] | v
        else:
            raise ValueError(f"PICT opcode {op:#04x} not handled")
    rbo = (w + 15) // 16 * 2
    out = bytearray(struct.pack("<6h", t, l, b, r, rbo, 0))
    for row in canvas:
        bits = bytearray(rbo)
        for x, v in enumerate(row):
            if v:
                bits[x >> 3] |= 0x80 >> (x & 7)
        out += bits
    return bytes(out)


def convert_font(d):
    (_, first, last, _, kern, _, _, height, owtloc, ascent, descent, leading,
     rowwords) = struct.unpack(">13h", d[:26])
    n = last - first + 3
    strike = d[26:26 + rowwords * 2 * height]
    loc = struct.unpack(f">{n}H", d[26 + len(strike):26 + len(strike) + 2 * n])
    ow_at = 16 + owtloc * 2                                  # owTLoc: words from itself
    # these fonts omit the owTable's closing -1 entry; supply it
    ow = struct.unpack(f">{n - 1}H", d[ow_at:ow_at + 2 * (n - 1)]) + (0xFFFF,)
    return (struct.pack("<8h", first, last, ascent, descent, leading, height,
                        rowwords * 2, kern)
            + struct.pack(f"<{n}H", *loc) + struct.pack(f"<{n}H", *ow) + strike)


def main():
    hfs = HFS(dc42_volume(DISK))
    forks = {name: resources(hfs.rsrc(name)) for name in ("ChipWits", "System")}
    os.makedirs(OUT, exist_ok=True)
    for pid in PICTS:
        with open(os.path.join(OUT, f"pict-{pid}.bin"), "wb") as f:
            f.write(render_pict(forks["ChipWits"][("PICT", pid)][1]))
    for (fork, fid), label in FONTS.items():
        with open(os.path.join(OUT, f"font-{fid}.bin"), "wb") as f:
            f.write(convert_font(forks[fork][("FONT", fid)][1]))
    print(f"extracted {len(PICTS)} PICTs and {len(FONTS)} fonts into data/")


if __name__ == "__main__":
    main()
