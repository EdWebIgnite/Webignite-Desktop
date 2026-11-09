#!/usr/bin/env bash
# A copy of the sync server's database, taken safely while it runs, kept for 30 days. Run nightly from cron
# (see docs/server-setup.md). What's synced is locked with your workspace key, so a backup can't be read
# without it, but keep the backups private all the same: they hold the account details.
#
# Cron runs a root-owned copy (/usr/local/sbin/webignite-backup), so nothing in the repository's folder can change
# what root runs; WEBIGNITE_DEPLOY says where the deploy folder is. Backups are readable by root only.
set -euo pipefail
umask 077
here="${WEBIGNITE_DEPLOY:-$(cd "$(dirname "$0")" && pwd)}"
database="$here/data/webignite-server.db"
backups="$here/backups"
mkdir -p "$backups"
chmod 700 "$backups"
stamp="$(date -u +%Y-%m-%d-%H%M)"
sqlite3 "$database" ".backup '$backups/webignite-server-$stamp.db'"
gzip -f "$backups/webignite-server-$stamp.db"
find "$backups" -name 'webignite-server-*.db.gz' -mtime +30 -delete
echo "Backed up to $backups/webignite-server-$stamp.db.gz"
