#!/usr/bin/env sh
set -eu
cd "$(dirname "$0")/../.."
task="${1:-verify}"
case "$task" in restore|build|test|pack|verify|format-check) ;; *) echo 'Unknown task' >&2; exit 2 ;; esac
if [ "$task" = format-check ]; then
	dotnet format NuntivusRedact.slnx --verify-no-changes --no-restore
  exit
fi
dotnet restore NuntivusRedact.slnx
[ "$task" != restore ] || exit 0
dotnet build NuntivusRedact.slnx -c Release --no-restore
if [ "$task" = test ] || [ "$task" = verify ]; then
	dotnet test NuntivusRedact.slnx -c Release --no-build --logger trx --results-directory artifacts/test-results
fi
if [ "$task" = pack ] || [ "$task" = verify ]; then
	dotnet pack NuntivusRedact.slnx -c Release --no-build -o artifacts/packages
fi
