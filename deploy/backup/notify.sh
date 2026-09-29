#!/bin/sh
# Sends a failure e-mail to BACKUP_ALERT_EMAIL using the app's SMTP settings.
# Usage: notify.sh "<subject>" <file with the message body>
# Never fails the caller: problems sending the mail are only logged.

. /backup/common.sh

subject="$1"
body_file="$2"

if [ -z "$BACKUP_ALERT_EMAIL" ]; then
    log "BACKUP_ALERT_EMAIL is not set, not sending an alert for: $subject"
    exit 0
fi

if [ -z "$SMTP_HOST" ] || [ -z "$SMTP_FROM_ADDRESS" ]; then
    log "SMTP_HOST or SMTP_FROM_ADDRESS is not set, cannot send an alert for: $subject"
    exit 0
fi

port="${SMTP_PORT:-25}"

# Same TLS rule as SmtpEmailClient.IsTLSPort: 465 is implicit TLS, 587 is STARTTLS.
case "$port" in
    465) url="smtps://$SMTP_HOST:$port"; tls="" ;;
    587) url="smtp://$SMTP_HOST:$port"; tls="--ssl-reqd" ;;
    *)   url="smtp://$SMTP_HOST:$port"; tls="" ;;
esac

mail_file="$(mktemp)"
{
    echo "From: Analytics backups <$SMTP_FROM_ADDRESS>"
    echo "To: <$BACKUP_ALERT_EMAIL>"
    echo "Subject: [analytics backup] $subject"
    echo "Date: $(date -R)"
    echo "Content-Type: text/plain; charset=utf-8"
    echo
    cat "$body_file"
} > "$mail_file"

set -- --url "$url" --mail-from "$SMTP_FROM_ADDRESS" --mail-rcpt "$BACKUP_ALERT_EMAIL" --upload-file "$mail_file"
[ -n "$tls" ] && set -- "$@" "$tls"
[ -n "$SMTP_USERNAME" ] && set -- "$@" --user "$SMTP_USERNAME:$SMTP_PASSWORD"

if curl -sS "$@"; then
    log "Sent alert e-mail to $BACKUP_ALERT_EMAIL: $subject"
else
    log "Failed to send alert e-mail to $BACKUP_ALERT_EMAIL: $subject"
fi

rm -f "$mail_file"
exit 0
