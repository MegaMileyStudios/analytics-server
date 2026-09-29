# Backup sidecar for the analytics stack, see the "Backups & Restore" wiki page.
# Based on the postgres image so pg_dump/pg_restore match the server's major version.
FROM postgres:15-alpine

RUN apk add --no-cache age curl jq supercronic tar tini tzdata

ENV TZ=UTC

COPY common.sh entrypoint.sh backup.sh do-backup.sh notify.sh restore.sh /backup/
RUN chmod +x /backup/*.sh

ENTRYPOINT ["/sbin/tini", "--", "/backup/entrypoint.sh"]
