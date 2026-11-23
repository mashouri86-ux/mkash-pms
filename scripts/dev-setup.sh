#!/usr/bin/env bash
# One-time setup of a Codespace / dev container.
set -e
cd "$(dirname "$0")/.."
dotnet restore MKAsh.sln
[ -f web/.env ] || cp web/.env.example web/.env
(cd web && npm install --no-audit --no-fund)
