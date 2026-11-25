#!/usr/bin/env bash
# Starts Tamkeen on port 5173 (the dashboard screens and the API together), then waits until it answers.
# Log: /tmp/mkash.log
cd "$(dirname "$0")/.."
ROOT=$(pwd)

pkill -f "dotnet.*MKAsh.Api" 2>/dev/null || true
pkill -f "MKAsh.Api" 2>/dev/null || true
pkill -f "node.*vite" 2>/dev/null || true

# Codespaces: make sure a released .NET 10 SDK is installed (a cached Codespace image can carry a preview build)
if [ -n "$CODESPACES" ] && ! dotnet --list-sdks 2>/dev/null | grep -qE '^10\.0\.[0-9]+ '; then
  echo "Installing the .NET 10 SDK (one time, about a minute)…"
  curl -sSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh \
    && sudo bash /tmp/dotnet-install.sh --channel 10.0 --install-dir /usr/share/dotnet > /tmp/dotnet-install.log 2>&1
fi

# Codespaces: keep the data in a SQLite file (no separate database server needed)
# (kept under /workspaces so it survives the Codespace stopping, restarting or being rebuilt)
if [ -n "$CODESPACES" ]; then
  mkdir -p /workspaces/.tamkeen
  DB=/workspaces/.tamkeen/tamkeen.db
  [ -f "$DB" ] || [ ! -f /tmp/mkash-pms-v2.db ] || cp /tmp/mkash-pms-v2.db "$DB"   # keep data from earlier test runs
  export Database__Provider=Sqlite
  export ConnectionStrings__Pms="Data Source=$DB"
fi

cd "$ROOT/src/MKAsh.Api" && ASPNETCORE_ENVIRONMENT=Development setsid nohup dotnet run --launch-profile MKAsh.Api > /tmp/mkash.log 2>&1 < /dev/null &
cd "$ROOT"

echo "Starting Tamkeen… (the first start also builds it and loads the 50 projects)"
for i in $(seq 1 90); do
  code=$(curl -s -o /dev/null -w "%{http_code}" http://localhost:5173/api/health || true)
  if [ "$code" = "200" ]; then
    # Keep the link open to colleagues on any device (sign-in still limits who gets data).
    if [ -n "$CODESPACE_NAME" ] && command -v gh >/dev/null; then
      gh codespace ports visibility 5173:public -c "$CODESPACE_NAME" >/dev/null 2>&1 \
        && echo "✓ Link is public: https://$CODESPACE_NAME-5173.app.github.dev/" \
        || echo "! Could not make the link public automatically: Ports tab → right-click 5173 → Port Visibility → Public."
    fi
    echo "✓ Ready. Open port 5173 (Ports tab → globe icon) and refresh the page."
    exit 0
  fi
  sleep 3
done
echo "✗ Not ready after 4–5 minutes."
tail -n 40 /tmp/mkash.log
