#!/usr/bin/env bash
# Starts the API (port 5080) and the web app (port 5173) in the background, then waits until both answer.
# Logs: /tmp/mkash-api.log and /tmp/mkash-web.log
cd "$(dirname "$0")/.."
ROOT=$(pwd)

pkill -f "dotnet.*MKAsh.Api" 2>/dev/null || true
pkill -f "node.*vite" 2>/dev/null || true

# First start: make sure packages are there (in case setup was interrupted)
[ -f web/.env ] || cp web/.env.example web/.env
[ -d web/node_modules ] || (cd web && npm install --no-audit --no-fund)

# setsid + nohup keep both running after this script (and the Codespace start-up step) ends
cd "$ROOT/src/MKAsh.Api" && ASPNETCORE_ENVIRONMENT=Development setsid nohup dotnet run --launch-profile MKAsh.Api > /tmp/mkash-api.log 2>&1 < /dev/null &
cd "$ROOT/web" && setsid nohup npm run dev -- --host > /tmp/mkash-web.log 2>&1 < /dev/null &
cd "$ROOT"

echo "Starting MKAsh PMS… (the first start also builds the API and fills the database)"
for i in $(seq 1 90); do
  web=$(curl -s -o /dev/null -w "%{http_code}" http://localhost:5173/ || true)
  api=$(curl -s -o /dev/null -w "%{http_code}" http://localhost:5080/api/health || true)
  if [ "$web" = "200" ] && [ "$api" = "200" ]; then
    echo "✓ Ready. Open port 5173 (Ports tab → globe icon) and refresh the page."
    exit 0
  fi
  sleep 3
done
echo "✗ Not ready after 4–5 minutes. Web: $web  API: $api"
echo "---- web log ----"; tail -n 20 /tmp/mkash-web.log
echo "---- api log ----"; tail -n 40 /tmp/mkash-api.log
