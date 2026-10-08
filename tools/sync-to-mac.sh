#!/bin/bash
# Copies the source tree to a Mac for building and testing, for when you develop on another machine.
# usage: tools/sync-to-mac.sh user@host [remote-folder]
set -euo pipefail
HOST="${1:?usage: tools/sync-to-mac.sh user@host [remote-folder]}"
DEST="${2:-uwuvci-mac}"
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
cd "$ROOT"
tar --exclude='bin' --exclude='obj' --exclude='dist' --exclude='.git' -czf - . |
  ssh "$HOST" "mkdir -p '$DEST' && cd '$DEST' && tar -xzf - 2>/dev/null && echo synced to \$(pwd)"
