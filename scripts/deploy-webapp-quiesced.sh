#!/usr/bin/env bash
set -euo pipefail

# Stage and validate away from wwwroot before stopping the app. The detached
# Azure-side transaction owns activation and rollback if this runner exits.
script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
exec python3 "$script_dir/deploy_webapp.py" "$@"
