#!/bin/bash
# Creates the databases listed in POSTGRES_MULTIPLE_DATABASES (comma-separated).
set -e

if [ -n "$POSTGRES_MULTIPLE_DATABASES" ]; then
  echo "Creating databases: $POSTGRES_MULTIPLE_DATABASES"
  for db in $(echo "$POSTGRES_MULTIPLE_DATABASES" | tr ',' ' '); do
    echo "  -> $db"
    psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" <<-SQL
      SELECT 'CREATE DATABASE "$db"'
      WHERE NOT EXISTS (SELECT FROM pg_database WHERE datname = '$db')\gexec
SQL
  done
fi
