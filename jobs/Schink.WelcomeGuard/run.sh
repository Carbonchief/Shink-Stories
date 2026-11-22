#!/bin/sh
set -eu
exec dotnet "$(dirname "$0")/Schink.WelcomeGuard.dll"
