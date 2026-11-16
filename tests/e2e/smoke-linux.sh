#!/usr/bin/env bash
# Real X11/Secret Service session, with no access to the invoking user's keyring or vault.
set -euo pipefail
package=$(realpath "$1")
output=$(realpath -m "$2")
script_dir=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
test_home=$(mktemp -d)
trap 'rm -rf -- "$test_home"' EXIT
mkdir -p "$test_home/config" "$test_home/data" "$test_home/cache" "$test_home/run"
chmod 700 "$test_home/run"
export HOME="$test_home"
export XDG_CONFIG_HOME="$test_home/config"
export XDG_DATA_HOME="$test_home/data"
export XDG_CACHE_HOME="$test_home/cache"
export XDG_RUNTIME_DIR="$test_home/run"
export LIBGL_ALWAYS_SOFTWARE=1
export PASSKEEPER_SMOKE_PIN=1
unset WAYLAND_DISPLAY GNOME_KEYRING_CONTROL DBUS_SESSION_BUS_ADDRESS

dbus-run-session -- xvfb-run -a -s '-screen 0 1440x1000x24' bash -euo pipefail -c '
  openbox >/dev/null 2>&1 &
  wm=$!
  trap '\''kill "$wm" 2>/dev/null || true'\'' EXIT
  printf %s smoke-only-keyring-password | gnome-keyring-daemon --unlock --components=secrets
  printf %s smoke-probe | timeout 20s secret-tool store --label=smoke-probe smoke-test probe
  test "$(timeout 20s secret-tool lookup smoke-test probe)" = smoke-probe
  secret-tool clear smoke-test probe
  python3 -I "$1/smoke-package.py" "$2" "$3"
' bash "$script_dir" "$package" "$output"
