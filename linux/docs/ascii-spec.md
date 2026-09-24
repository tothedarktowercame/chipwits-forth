# ChipWits in the terminal — initial spec

A text-mode ChipWits in the spirit of Dwarf Fortress: a dense glyph map, the
robot's internals laid open, keyboard only.  It runs in a terminal (tmux,
mosh, a phone), next to the browser port and sharing its saved robots.

Status: **spec, nothing built yet.**  Phase 1 is the next step.

## Principles

- **Doug's engine, our interface.**  The original Forth game logic runs
  unmodified (`screens/` untouched, as in the browser port).  The text
  front end never scrapes pixels: it reads the engine's *state* — room grid,
  robot, registers, stacks, current chip — and draws that.  This is the
  deliberate difference from the browser port, which also runs Doug's UI.
- **Show what the Mac hid.**  The mission trace, register changes, stack
  contents and branch outcomes are first-class, like DF's announcements.
- **Keyboard only.**  Every action has a key; nothing needs a pointer.
- **One robot file.**  The terminal Workshop reads and writes the same `CW`
  file, byte-compatible, so a robot built in one front end runs in the other.
- **Reproducible.**  A mission is a function of robot, adventure, RNG seed
  and keypresses; the seed is always shown and can be set.

## Screen design

80×24 minimum; wider terminals get a longer log and wider panels.

```
 Greedy in Greedville · room 3 · cyc 5743 · fuel 6911 · dmg 150 · score 400  seed 123457
┌────────────────┐┌─M─a─b─c─d─e─f─g─────────────────────────┐┌ stacks ──────┐
│# # # + + # # # ││ GO →Fl% →Mv↑ →Lp                          ││ obj  % ~     │
│# . . . . . . # ││          f                                ││ mov  ↑ ↑ ↻   │
│# . ! . b . . # ││          ↓                                ││ num  3       │
│+ . . # # . . + ││         Mv↻ →Lp                           ││              │
│+ . ↗ # # % . + ││                                           ││ range 2      │
│# . . . . . ~ # ││                                           ││ key   -      │
│# . . . . . . # ││                                           │└──────────────┘
│# # # + + # # # ││                                           │
└────────────────┘└───────────────────────────────────────────┘
 5760 feel-for pie → FALSE              5751 move ↑ → bump wall  dmg +50
 5757 move ↻                            5743 room 3 → 4 by the W door
 [space] pause  [.] step  [+/-] speed  [tab] panel  [k] key  [q] quit
```

- **Status line:** robot name, adventure, room, cycles left, fuel, damage,
  score, seed.
- **Map** (left): the 8×8 room, one glyph per square, spaced for legibility.
  The robot is an arrow showing its facing, in reverse video.
- **Panel** (middle): the program panel being executed, 10×6 sockets.  The
  executing chip is in reverse video.  The header names the eight panels —
  `M` for the main panel (the Mac shows a head icon), `a`–`g` for the
  subpanels — with the one shown in capitals; `tab` flips through them
  (following execution is the default).
- **Stacks** (right): the object, move and number stacks, top at the left,
  plus the range register and the last key pressed.
- **Log** (bottom): one line per instruction or event, prefixed with the
  cycle count, newest at the bottom, in two columns when the width allows.
- **Key hints:** the last line.

### Map glyphs

Object codes are the argument constants of screen 005.  Colour is optional;
everything must read in monochrome (reverse video/bold only).

| code | thing   | glyph | `--ascii` | note |
|-----:|---------|:-----:|:---------:|------|
|    8 | floor   | `.`   | `.`       | |
|   12 | wall    | `#`   | `#`       | |
|   13 | door    | `+`   | `+`       | |
|    4 | pie     | `%`   | `%`       | food |
|    5 | coffee  | `!`   | `!`       | fuel |
|    6 | disk    | `=`   | `=`       | |
|    7 | oil     | `~`   | `~`       | |
|    9 | bomb    | `*`   | `*`       | bold |
|   10 | bouncer | `b`   | `b`       | bold: creatures are letters, as in DF |
|   11 | creep   | `c`   | `c`       | bold |
|  —   | robot   | `↖↑↗→↘↓↙←` | `@` | facing = orientation 0–7 (NW…W); `--ascii` shows the facing in the status line |

### Chip notation

A socket is 3 characters: a 2-character operator mnemonic and a 1-character
argument (map glyphs for objects).  Wires sit in the 1-character gaps: the
true (or only) wire as an arrow `→ ← ↑ ↓`, the false wire of a branching
chip as `f` beside the arrow it takes.

| op | IBOL         | mn | | op | IBOL        | mn |
|---:|--------------|----|-|---:|-------------|----|
|  0 | go (start)   | GO | | 13 | keypress    | Ky |
|  1 | goto / loop  | Lp | | 14 | number =    | N= |
|  2 | subpanel     | Sb | | 15 | number <    | N< |
|  3 | boomerang    | Bm | | 16 | object =    | O= |
|  4 | wire         | ── | | 17 | move =      | M= |
|  5 | move         | Mv | | 18 | on number   | N+ |
|  6 | pick up      | Pk | | 19 | on object   | O+ |
|  7 | Q-ray (zap)  | Qr | | 20 | on move     | M+ |
|  8 | sing         | Sg | | 21 | drop stack  | Dr |
|  9 | feel for     | Fl | | 22 | plus        | +  |
| 10 | look for     | Lk | | 23 | minus       | −  |
| 11 | smell for    | Sm | | 24 | (empty socket) | `···` |
| 12 | flip coin    | Fc | |    |             |    |

Arguments (screen 005): move `↻ ↺ ↑ ↓` (turn right, turn left, forward,
reverse); objects as on the map; registers `D F R` (damage, fuel, range);
stacks `m n o` and their empties `∅`; digits `0`–`7`; subpanels `A`–`G`.

## Architecture

```
tui.py (curses, stdlib)  ⇄  two FIFOs  ⇄  pforth tui.fs (engine driver)
        │                                        │
        └── reads/writes data/CW (phase 2) ──────┘ loader.fs + the shim
```

- **`tui.fs`** replaces `live.fs` for this front end: it loads the game as
  usual, then drives the VM itself — `init.game`/`start.game`, then
  `execute.robot.instruction` in a loop — instead of running the `CHIPWITS`
  master word.  After every instruction it writes one **state record**;
  between instructions it reads **commands**.  The game's own drawing still
  runs into the off-screen framebuffer (cheap; startup is ~0.1 s) and its
  console text is discarded.
- **`tui.py`** starts `tui.fs`, owns the terminal, renders records and sends
  commands.  Python stdlib only (`curses`), like `serve.py`.
- **Transport:** two FIFOs in a temp directory.  Commands reuse the
  `live/input.bin` record (4× u16 LE: type, x, y, pad); new types: pause,
  step, set delay (x = ms), keypress (x = char), quit.
- **The state record** is fixed-size binary (native little-endian, since
  it is our format): magic and sequence number; `prog.status`; room number;
  `Robot.Square`, `Robot.Orientation`; `Damage.Reg`, `fuel.Reg`,
  `Range.reg`, `Points`, `Cycle.ct`, `Key.pressed`; `Current.Panel^`,
  `Current.Instruction^`, `Op.byte`, `Arg.Byte`, `Flow.code`; the three
  stack pointers and the top 8 entries of each stack; `Room.Data(` (80
  bytes); the current panel's 120 program bytes.  About 330 bytes, once
  per instruction.
- **The event log is derived in Python** by diffing consecutive records:
  score up and an object gone from the target square = "ate %"; damage up =
  bump / zap / creep; fuel up = refuel; room number changed = room change
  (with the door's side); stack pointer moved = push/pop; branch outcome
  from `Flow.code` against the chip's true/false wires.  No hooks in game
  code.
- **Room number:** the game declares `This.Room@` but never sets it, and
  layouts can't identify rooms (all 100 in Mystery Matrix share one), so the
  shim records it at the file boundary: `Load.room` reads through
  `read.fixed` into `Room.Data(`, and the record number *is* the room.
  (Same technique as the big-endian stats conversion; `loader.fs`
  registers the buffer.)
- **Seed:** `rnd-seed` (the shim's LCG) is set from the command line before
  the mission; the default is today's fixed 123457.

## Data reference (verified against the source and the recovered files)

- **Mission file** (`data/<adventure>`): 80-byte records, record *n* =
  room *n* (record 0 unused).  Bytes 0–63: the 8×8 grid, row-major, one
  object code each.  Bytes 64–71: for the *k*-th door square in grid
  order, the room it leads to; bytes 72–79: the square you enter at.
  On disk rooms hold only walls, floor and doors: `Furnish.room` scatters
  food, disks, oil and baddies at random on load (screen 020, counts in
  `Scenario.play(` screen 011), so **the viewer reads `Room.Data(` from
  memory, never the file**.  The start room is random too (screen 069).
- **Robot program:** 8 panels × 60 sockets (10 wide × 6 high) × 2 bytes =
  960 bytes.  Byte 0: operator (low 6 bits) + true/only wire (high 2 bits:
  0 up, 64 down, 128 left, 192 right).  Byte 1: argument (low 6 bits) +
  false wire (high 2 bits).  Operator 24 is an empty socket.  Panel 0 is
  the main panel, 1–7 the subpanels A–G.  (Doug's manual says 10×8 sockets
  and panels A–J; the CW+ source says 10×6 and a main panel plus A–G — the
  source wins.)
- **Costs per operator** (screens 003–004), cycles / fuel:
  go 0/0, goto 1/1, subpanel 1/1, boomerang 1/1, wire 0/0, move 3/5,
  pick up 1/5, Q-ray 1/7, sing 2/2, feel 4/4, look 4/2, smell 4/2,
  flip coin 3/1, keypress 3/1, the stack operators 1–2/1.
- **Registers:** damage and fuel 0–10000, range 0–8; bump = 50 damage.
- **Stacks:** three 256-byte stacks (`mov.st( obj.st( num.st(`) with top
  pointers `mov.st^` etc. (screen 150).
- **`CW` file:** records 1–16 are the robots (960 bytes each; record 0 is
  not a robot slot); from byte 16320, 16 stats records of 108 bytes: name (2-byte
  **big-endian** length + 18 chars), per adventure {2-byte missions, 4-byte
  total, 4-byte high score} **big-endian**, then 8 environment flags; the
  last-used robot and adventure follow.  Doug's robots: 1 Greedy (36 chips),
  2 Mr. CW (91), 3 Buddy (9); 4–16 hold only a GO chip per panel.

## Phases

### Phase 1 — mission viewer

`./tui.sh [robot] [adventure] [--seed N] [--ascii]` runs one mission in the
terminal and exits to a summary (score, cycles, cause of death, seed).

Keys: `space` pause/resume · `.` single-step (while paused) · `+`/`-` speed
(delay per instruction, 0–1000 ms; 0 = as fast as it goes) · `tab` panel
(follow / fixed) · `k` then a character: the next keypress, which the
**Keypress** operator reads — the one way to influence a running robot ·
`q` quit.

Done when:
- it runs every adventure with each of Doug's three robots to the end;
- for a given seed, robot square, facing and room contents match the
  browser port's `test-render` screenshot at the same instruction count;
- the log reports eating, bumps, refuels, room changes (with door side)
  and branch outcomes correctly on a hand-checked mission;
- it works at 80×24 over mosh with Unicode, and with `--ascii`;
- `screens/` is untouched and `test-mission.fs` still passes.

### Phase 2 — keyboard Workshop

Editing entirely in Python on the `CW` bytes; the engine isn't needed until
you run the robot.

- Cursor over the 10×6 sockets; `[`/`]`, or `M` and `A`–`G`, switch panels.
- `o` opens the operator palette (mnemonic + name + cost), then the
  argument palette with **only the legal arguments** for that operator.
- `w` + arrow sets the true wire, `W` + arrow the false wire; illegal
  directions (off the panel edge) are refused as in the Mac Workshop.
- `m` picks up a chip and puts it down elsewhere; `x` deletes it.
- `s` saves: name (≤10 characters, as `Stuff.name` keeps), the adventure
  flags, then the program and big-endian name/flags written exactly as
  `CW.save`/`Save.name` do.  `r` saves and runs Phase 1 on it.
- Cross-check: a robot saved here, opened in the browser Workshop, looks
  identical (Playwright screenshot), and vice versa.

To extract from the source first: the per-operator argument sets (the
`action.type(` table and the `ws.thing.icons` family, screens 116–133) and
the wire rules (`wire.ok?`, `legal.wire`, `test.legal.wire`).

### Phase 3 — things the Mac couldn't do

- **Replay:** seed + keypress log → the identical mission, scrub back and
  forth; share a replay as a line of text.
- **Branch history:** per chip, how often each wire was taken.
- **Leagues:** headless batch runs of robots × adventures × seeds, with a
  table of scores (the engine runs thousands of instructions a second
  headless).

## Open questions

1. **Baddies:** do creeps and bouncers move by rewriting `Room.Data(`, or
   only in `Creeps(`/`Bouncer.sq` (screen 019)?  Phase 1 must show them where
   they really are; include the baddie arrays in the state record if
   needed.
2. **Series** (multi-mission) runs: out of scope for Phase 1.
3. **Sound:** none by default; maybe the terminal bell on a bump or death,
   behind a flag.
4. **Glyphs:** `!` for coffee and `=` for disk follow DF's feel but aren't
   sacred.  Settle after playing a few missions.
