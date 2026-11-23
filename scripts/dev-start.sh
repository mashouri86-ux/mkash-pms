#!/usr/bin/env bash
# Starts the API (port 5080) and the web app (port 5173) in the background.
# Logs: /tmp/mkash-api.log and /tmp/mkash-web.log
cd "$(dirname "$0")/.."
pkill -f "MKAsh.Api" 2>/dev/null || true
pkill -f "vite" 2>/dev/null || true
(cd src/MKAsh.Api && ASPNETCORE_ENVIRONMENT=Development nohup dotnet run --launch-profile MKAsh.Api > /tmp/mkash-api.log 2>&1 &)
(cd web && nohup npm run dev -- --host > /tmp/mkash-web.log 2>&1 &)
echo "Starting… the app opens on port 5173 in about a minute (first start also creates and fills the database)."
