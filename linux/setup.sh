#!/bin/bash
# Build the 32-bit pforth interpreter and generate the derived files
# (screens/, data/) that the native ChipWits+ port runs from.
set -euo pipefail
cd "$(dirname "$0")"

# 1. pforth, built with 32-bit cells (MacForth's cell size is load-bearing:
#    struct layouts and 4*-indexing all assume 4-byte cells).
#    Needs: gcc-multilib (Debian/Ubuntu: apt install gcc-multilib).
if [ ! -x pforth/platforms/unix/pforth ]; then
    [ -d pforth ] || git clone --depth 1 https://github.com/philburk/pforth
    make -C pforth/platforms/unix CC="gcc -m32"
fi
cp pforth/platforms/unix/pforth.dic .

# 2. Split the recovered MacForth source into per-screen files.
python3 split_screens.py

# 3. Work copies of the recovered CW+ data files (missions, robots, art).
#    Copies so the game's save-writes never touch the originals.
mkdir -p data
SRC="../mac/disks/CW+ Copy 1/CW+ Copy 1/CW+"
for f in "CW" "Greedville" "IBOL" "Source Graphics" "Doom Rooms" \
         "Boomtown" "ChipWit Caves" "Memory Lanes" "Mystery Matrix" \
         "Octopus Garden" "Peace Paths"; do
    cp "$SRC/$f" data/
done

echo "setup complete -- try: ./run-test.sh"
