#!/usr/bin/env bash
set -euo pipefail

# Stage and validate away from wwwroot before stopping the app. The detached
# worker owns the file transaction. The runner owns startup verification.
script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
exec python3 "$script_dir/deploy_webapp.py" "$@"
