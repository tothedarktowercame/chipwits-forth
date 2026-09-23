#!/usr/bin/env python3
"""Extract the ChipWits+ art into reusable PNGs.

Sources (recovered data-fork files, raw 1-bit bitmaps, MSB = leftmost pixel):
  data/IBOL             160x160, rowbytes 20 -- the chip-icon sheet
  data/Source Graphics  368x232, rowbytes 46 -- robot, creatures, objects, tiles

Icon rectangles are transcribed from the original source:
  screens/090.fs  Action.s.rect(  -- operator chips (IR: 16*n+1 grid, 15 wide,
                                     16 tall, or 32 tall for size-2 entries)
  screens/091.fs  Thing.s.rect(   -- argument chips (all 15x16)
  screens/083.fs  cw.init/cw.point -- robot frames: 40px column per orientation,
                                      base column is W at x=1

Output: assets/  (sheets, plus one alpha PNG per icon: ink opaque, paper clear)
Requires ImageMagick (`convert`) for the PNG encode.
"""
import os, subprocess, sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.join(HERE, "..")
DATA = os.path.join(ROOT, "data")
OUT = os.path.join(ROOT, "assets")

def load(path, w, h):
    data = open(path, "rb").read()
    rb = w // 8
    px = [[(data[y*rb + (x >> 3)] >> (7 - (x & 7))) & 1 for x in range(w)]
          for y in range(h)]
    return px

def write_pbm(px, x1, y1, x2, y2, path):
    y2 = min(y2, len(px))          # screens/091.fs applies the same clamp
    x2 = min(x2, len(px[0]))       # to drop-stack's bottom edge
    w, h = x2 - x1, y2 - y1
    with open(path, "w") as f:
        f.write(f"P1\n{w} {h}\n")
        for y in range(y1, y2):
            f.write(" ".join(str(px[y][x]) for x in range(x1, x2)) + "\n")

def to_png(pbm, png, alpha=True, scale=1):
    cmd = ["convert", pbm]
    if scale != 1:
        cmd += ["-scale", f"{scale*100}%"]
    if alpha:
        cmd += ["-transparent", "white"]
    cmd += [png]
    subprocess.run(cmd, check=True)
    os.remove(pbm)

# ---- operator chips: screens/090.fs, IR xpos ypos size ----
ACTIONS = [
    (0, 4, 2, "go"), (0, 6, 2, "goto"), (1, 4, 2, "subpanel"),
    (1, 6, 2, "boomerang"),                      # wire is special-cased below
    (3, 8, 1, "move"), (3, 4, 2, "pickup"), (2, 6, 2, "qray"),
    (9, 1, 1, "sing"),
    (2, 8, 1, "feel-for"), (0, 8, 1, "look-for"), (8, 1, 1, "smell-for"),
    (2, 4, 2, "flip-coin"), (1, 8, 1, "keypress"),
    (7, 8, 1, "num-equal"), (7, 7, 1, "num-less"), (5, 2, 1, "obj-equal"),
    (4, 6, 1, "move-equal"), (4, 7, 1, "on-number"), (6, 7, 1, "on-object"),
    (5, 7, 1, "on-move"), (7, 9, 1, "drop-stack"),
    (4, 4, 2, "plus"), (3, 6, 2, "minus"),
]
# ---- argument chips: screens/091.fs, all 15x16 ----
THINGS = [
    (2, 0, "turn-right"), (3, 0, "turn-left"), (0, 0, "forward"), (1, 0, "reverse"),
    (0, 2, "pie"), (1, 2, "coffee"), (3, 2, "disk"), (4, 2, "oil"), (4, 3, "floor"),
    (0, 3, "bomb"), (1, 3, "bouncer"), (2, 3, "creep"), (3, 3, "wall"), (6, 3, "door"),
    (4, 0, "damage-reg"), (5, 0, "fuel-reg"), (6, 0, "range-reg"),
    (8, 0, "mov-stack"), (7, 0, "num-stack"), (9, 0, "obj-stack"),
    (2, 2, "stack-empty"),
    (0, 1, "digit-0"), (1, 1, "digit-1"), (2, 1, "digit-2"), (3, 1, "digit-3"),
    (4, 1, "digit-4"), (5, 1, "digit-5"), (6, 1, "digit-6"), (7, 1, "digit-7"),
]
# ---- robot: screens/083.fs; columns W SW S SE E NE N NW from x=1 ----
ORIENT = ["west", "southwest", "south", "southeast", "east", "northeast",
          "north", "northwest"]  # column k holds orientation 7-k
BANDS = [("robot", 1, 56), ("robot-mask", 57, 112),
         ("mouth", 113, 136), ("mouth-mask", 137, 160)]

def main():
    os.makedirs(f"{OUT}/ibol", exist_ok=True)
    os.makedirs(f"{OUT}/robot", exist_ok=True)

    ibol = load(f"{DATA}/IBOL", 160, 160)
    src = load(f"{DATA}/Source Graphics", 368, 232)

    # full sheets (opaque, 1x)
    write_pbm(ibol, 0, 0, 160, 160, f"{OUT}/_i.pbm")
    to_png(f"{OUT}/_i.pbm", f"{OUT}/ibol-sheet.png", alpha=False)
    write_pbm(src, 0, 0, 368, 232, f"{OUT}/_s.pbm")
    to_png(f"{OUT}/_s.pbm", f"{OUT}/source-graphics-sheet.png", alpha=False)

    for xp, yp, size, name in ACTIONS:
        x, y = 16*xp + 1, 16*yp + 1
        write_pbm(ibol, x, y, x + 15, y + 16*size, f"{OUT}/_t.pbm")
        to_png(f"{OUT}/_t.pbm", f"{OUT}/ibol/op-{name}.png")
    # wire: literal rect from the last line of screens/090.fs (t l b r 121 137 153 153)
    write_pbm(ibol, 137, 121, 153, 153, f"{OUT}/_t.pbm")
    to_png(f"{OUT}/_t.pbm", f"{OUT}/ibol/op-wire.png")

    for xp, yp, name in THINGS:
        x, y = 16*xp + 1, 16*yp + 1
        write_pbm(ibol, x, y, x + 15, y + 16, f"{OUT}/_t.pbm")
        to_png(f"{OUT}/_t.pbm", f"{OUT}/ibol/arg-{name}.png")

    for col in range(8):
        x = 1 + 40*col
        orient = ORIENT[7 - col]
        for band, y1, y2 in BANDS:
            write_pbm(src, x, y1, x + 39, y2 + 1, f"{OUT}/_t.pbm")
            to_png(f"{OUT}/_t.pbm", f"{OUT}/robot/{band}-{orient}.png")

    n = len(os.listdir(f'{OUT}/ibol')) + len(os.listdir(f'{OUT}/robot'))
    print(f"assets written to {os.path.normpath(OUT)}: {n} icons + 2 sheets")

if __name__ == "__main__":
    main()
