#!/usr/bin/env bash
# One-time setup of a Codespace / dev container.
set -e
cd "$(dirname "$0")/.."
dotnet restore MKAsh.sln
