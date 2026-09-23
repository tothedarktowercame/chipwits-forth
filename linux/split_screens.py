#!/usr/bin/env python3
"""Split ChipWits MacForth screen files into per-screen .fs files.

- Primary source: CW+ Work Final Src (final version)
- Missing screens filled from CW Game + Backup Source
- MacForth interpret-level EXIT semantics: everything after a top-level
  (non-compiling) `exit` token in a screen is dead code -> stripped.
"""
import re, os

HERE = os.path.dirname(os.path.abspath(__file__))
BASE = os.path.join(HERE, "..", "mac", "forth")
PRIMARY = os.path.join(BASE, "CW+ Work Final Src", "ChipWits.forth")
BACKUP = os.path.join(BASE, "CW Game + Backup Source", "ChipWits.forth")
OUT = os.path.join(HERE, "screens")

STRING_WORDS = {'."', ',"', '"', 'error"', 'abort"'}

def screens_of(path):
    src = open(path, errors="replace").read()
    # the re-encoding wrote high-ASCII (MacRoman TM, (c), ...) as {$XX};
    # put the single byte back so string lengths match the original
    src = re.sub(r'\{\$([0-9A-Fa-f]{2})\}', lambda m: chr(int(m.group(1), 16)), src)
    parts = re.split(r'═+\s+SCREEN\s+(\d+)[^\n]*\n', src)
    it = iter(parts[1:])
    return {int(n): t for n, t in zip(it, it)}

def strip_dead(text):
    """Truncate at a top-level `exit` token (outside : ... ; and comments)."""
    i, n = 0, len(text)
    in_colon = False
    while i < n:
        while i < n and text[i].isspace():
            i += 1
        if i >= n:
            break
        j = i
        while j < n and not text[j].isspace():
            j += 1
        tok = text[i:j]
        low = tok.lower()
        if tok == '(':                       # comment: skip to next ')'
            k = text.find(')', j)
            j = n if k < 0 else k + 1
        elif low in STRING_WORDS:            # string literal: skip to next '"'
            k = text.find('"', j + 1)
            j = n if k < 0 else k + 1
        elif tok == ':':
            in_colon = True
        elif tok == ';':
            in_colon = False
        elif low == 'exit' and not in_colon:
            return text[:i] + '\n( --- rest of screen disabled by interpret-level EXIT --- )\n'
        i = j
    return text

prim = screens_of(PRIMARY)
back = screens_of(BACKUP)

os.makedirs(OUT, exist_ok=True)
for f in os.listdir(OUT):
    os.remove(os.path.join(OUT, f))

top = max(max(prim), max(back))
filled, missing = [], []
for num in range(1, top + 1):
    if num in prim:
        text = prim[num]
    elif num in back:
        text = back[num]
        filled.append(num)
    else:
        missing.append(num)
        text = f"( screen {num} missing from recovered disks -- stub )\n"
        open(f"{OUT}/{num:03d}.fs", "w", encoding="latin-1").write(text)
        continue
    open(f"{OUT}/{num:03d}.fs", "w", encoding="latin-1").write(strip_dead(text))

print("screens:", len(prim), "+", len(filled), "from backup", filled)
print("missing (stubbed):", missing)
