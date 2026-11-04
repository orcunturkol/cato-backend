# The nightly OGT backup (task cato-ogt-backup, ogt_backup.tf). pg_dump must have the RDS
# server's major version or a newer one: RDS runs PostgreSQL 16. Raise both together.
FROM postgres:16-alpine

RUN apk add --no-cache aws-cli

COPY backup.sh /usr/local/bin/ogt-backup
RUN chmod 0755 /usr/local/bin/ogt-backup

USER postgres
ENTRYPOINT ["/usr/local/bin/ogt-backup"]
