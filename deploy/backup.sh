#!/usr/bin/env bash
# A copy of the sync server's database, taken safely while it runs, kept for 30 days. Run nightly from cron
# (see docs/server-setup.md). What's synced is locked with your workspace key, so a backup can't be read
# without it, but keep the backups private all the same: they hold the account details.
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
database="$here/data/webignite-server.db"
backups="$here/backups"
mkdir -p "$backups"
stamp="$(date -u +%Y-%m-%d-%H%M)"
sqlite3 "$database" ".backup '$backups/webignite-server-$stamp.db'"
gzip -f "$backups/webignite-server-$stamp.db"
find "$backups" -name 'webignite-server-*.db.gz' -mtime +30 -delete
echo "Backed up to $backups/webignite-server-$stamp.db.gz"
