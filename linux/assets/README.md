# ChipWits+ extracted art

Reusable PNGs sliced from the recovered ChipWits+ data files (`../data/`,
originally `mac/disks/CW+ Copy 1`). Rectangles are transcribed from the
original source tables, so the names are authoritative:

- `ibol/op-*.png` — the 25 IBOL **operator** chips (Action.s.rect(,
  `screens/090.fs`): go, goto, subpanel, boomerang, wire, move, pickup,
  qray, sing, feel-for, look-for, smell-for, flip-coin, keypress,
  num-equal, num-less, obj-equal, move-equal, on-number, on-object,
  on-move, drop-stack, plus, minus. 15×16 px, branching/large ops 15×32.
- `ibol/arg-*.png` — the 29 IBOL **argument** chips (Thing.s.rect(,
  `screens/091.fs`): movement arrows, pie/coffee/disk/oil/floor,
  bomb/bouncer/creep/wall/door, the damage/fuel/range registers, the
  three stacks + empty, digits 0–7. 15×16 px.
- `robot/robot-*.png` (39×56), `robot/mouth-*.png` (39×24) and their
  `-mask-` companions — the ChipWit in all 8 orientations
  (`screens/083.fs`: 40 px columns, W at x=1).
- `ibol-sheet.png`, `source-graphics-sheet.png` — the full 1-bit sheets.

Icons are 1× original pixels, black ink with transparent background
(the sheets are opaque). Regenerate everything with
`python3 tools/extract_assets.py` (needs ImageMagick).

**License**: CC BY-SA 4.0, same as the rest of this repository. The art is
by Doug Sharp (ChipWits, 1984–86; open-sourced by ChipWits, Inc. for the
40th anniversary) — credit accordingly.
