#!/bin/zsh
set -e
cd "$(dirname "$0")"
node server.mjs --open
