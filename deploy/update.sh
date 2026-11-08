#!/usr/bin/env bash
# Brings the sync server up to date with the repository: fetches the latest code, backs up, rebuilds and restarts.
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
cd "$here/.."
git pull --ff-only
sudo "$here/backup.sh"
cd "$here"
docker compose up -d --build
docker image prune -f > /dev/null
sleep 5
curl -fsS http://127.0.0.1:5080/health && echo " The sync server is up."
