#!/usr/bin/env bash
set -euo pipefail

if [ "$#" -ne 3 ]; then
  echo "Usage: deploy-webapp-quiesced.sh RESOURCE_GROUP APP_NAME PACKAGE_ZIP" >&2
  exit 2
fi

resource_group="$1"
app_name="$2"
package_zip="$3"
if [ ! -f "$package_zip" ]; then
  echo "Deployment package does not exist: $package_zip" >&2
  exit 2
fi

restore_running=false
deployment_attempted=false
restore_app_state() {
  local deployment_status=$?
  trap - EXIT INT TERM
  if [ "$deployment_attempted" = true ] && [ "$deployment_status" -ne 0 ]; then
    echo "::error::Deployment failed or was interrupted. The web app remains stopped; restore a known-good package before starting it." >&2
  elif [ "$restore_running" = true ]; then
    if ! az webapp start --resource-group "$resource_group" --name "$app_name" --output none; then
      echo "::error::Unable to restore the previously running web app after deployment." >&2
      exit 1
    fi
  fi
  exit "$deployment_status"
}
trap restore_app_state EXIT
trap 'exit 130' INT
trap 'exit 143' TERM

# Kudu must remain available while the main app is stopped. Refuse unknown or
# changed configuration before touching the running app.
scm_site_also_stopped="$(az webapp show --resource-group "$resource_group" --name "$app_name" --query scmSiteAlsoStopped --output tsv)"
if [ "$scm_site_also_stopped" != false ]; then
  echo "::error::Quiesced deployment requires scmSiteAlsoStopped=false; no app stop or deployment was attempted." >&2
  exit 1
fi

original_state="$(az webapp show --resource-group "$resource_group" --name "$app_name" --query state --output tsv)"
case "$original_state" in
  Running)
    # Set this before stop, so an uncertain/failed stop is also restored.
    restore_running=true
    az webapp stop --resource-group "$resource_group" --name "$app_name" --output none
    ;;
  Stopped) ;;
  *)
    echo "::error::Refusing deployment while web app state is $original_state." >&2
    exit 1
    ;;
esac

stopped=false
for attempt in {1..20}; do
  current_state="$(az webapp show --resource-group "$resource_group" --name "$app_name" --query state --output tsv)"
  if [ "$current_state" = Stopped ]; then
    stopped=true
    break
  fi
  sleep 3
done
if [ "$stopped" != true ]; then
  echo "::error::Web app did not stop; no deployment was attempted." >&2
  exit 1
fi

# Keep the process stopped until all assets/assemblies have been copied. Preserve
# runtime-uploaded files and restore the original running state only after success.
deployment_attempted=true
deployment_result="$(az webapp deploy --resource-group "$resource_group" --name "$app_name" \
  --src-path "$package_zip" --type zip --clean false --async false --restart false --track-status false \
  --enable-kudu-warmup false --timeout 600000 --query status --output tsv)"
if [ "$deployment_result" != 4 ]; then
  echo "::error::Deployment did not confirm completed status=4; web app will not be started." >&2
  exit 1
fi
