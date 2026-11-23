#!/usr/bin/env bash
# Starts MKAsh PMS on port 5173 (the dashboard screens and the API together), then waits until it answers.
# Log: /tmp/mkash.log
cd "$(dirname "$0")/.."
ROOT=$(pwd)

pkill -f "dotnet.*MKAsh.Api" 2>/dev/null || true
pkill -f "MKAsh.Api" 2>/dev/null || true
pkill -f "node.*vite" 2>/dev/null || true

# Codespaces: keep the data in a SQLite file (no separate database server needed)
if [ -n "$CODESPACES" ]; then
  export Database__Provider=Sqlite
  export ConnectionStrings__Pms="Data Source=/tmp/mkash-pms-v2.db"
fi

cd "$ROOT/src/MKAsh.Api" && ASPNETCORE_ENVIRONMENT=Development setsid nohup dotnet run --launch-profile MKAsh.Api > /tmp/mkash.log 2>&1 < /dev/null &
cd "$ROOT"

echo "Starting MKAsh PMS… (the first start also builds it and loads the 50 projects)"
for i in $(seq 1 90); do
  code=$(curl -s -o /dev/null -w "%{http_code}" http://localhost:5173/api/health || true)
  if [ "$code" = "200" ]; then
    echo "✓ Ready. Open port 5173 (Ports tab → globe icon) and refresh the page."
    exit 0
  fi
  sleep 3
done
echo "✗ Not ready after 4–5 minutes."
tail -n 40 /tmp/mkash.log
