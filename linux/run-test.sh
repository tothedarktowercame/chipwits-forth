#!/bin/bash
# Run the headless IBOL virtual-machine smoke test.
set -euo pipefail
cd "$(dirname "$0")"
exec pforth/platforms/unix/pforth test-vm.fs
