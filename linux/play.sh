#!/bin/bash
# Play ChipWits+ natively: game in this terminal, UI in the browser.
set -euo pipefail
cd "$(dirname "$0")"
[ -x pforth/platforms/unix/pforth ] || { echo "run ./setup.sh first"; exit 1; }

PORT="${1:-8047}"
mkdir -p live
: > live/input.bin
: > live/frame.raw

python3 serve.py "$PORT" &
SERVER=$!
trap 'kill $SERVER 2>/dev/null' EXIT

echo "browse to http://localhost:$PORT then Games > Start Mission"
pforth/platforms/unix/pforth live.fs
