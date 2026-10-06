#!/bin/sh
# Legt beim ersten Start die Rolle der Anwendung an (kein Superuser) und gibt ihr,
# was sie zum Einrichten ihrer eigenen Tabellen braucht. Laeuft als Datenbank-Verwalter.
set -e
psql -v ON_ERROR_STOP=1 -v app_pw="$APP_DB_PASSWORD" --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" <<'SQL'
CREATE ROLE mailarchiver_app LOGIN PASSWORD :'app_pw';
SELECT format('GRANT CONNECT, CREATE ON DATABASE %I TO mailarchiver_app', current_database()) \gexec
-- Ab PostgreSQL 15 nicht mehr automatisch erlaubt; die App legt dort ihre Migrations-Tabelle an
GRANT CREATE ON SCHEMA public TO mailarchiver_app;
-- Erweiterungen, die die App erwartet, legt der Verwalter an
CREATE EXTENSION IF NOT EXISTS citext;
CREATE EXTENSION IF NOT EXISTS pgcrypto;
SQL
