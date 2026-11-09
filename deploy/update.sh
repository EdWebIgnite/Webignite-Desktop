#!/usr/bin/env bash
# Brings the sync server up to date with the repository: fetches the latest code, backs up, rebuilds and restarts.
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
cd "$here/.."
git pull --ff-only
# The backup that runs as root is the installed copy, never the one just pulled; refresh it after a look.
sudo WEBIGNITE_DEPLOY="$here" /usr/local/sbin/webignite-backup
cd "$here"
docker compose up -d --build
docker image prune -f > /dev/null
sleep 5
curl -fsS http://127.0.0.1:5080/health && echo " The sync server is up."
