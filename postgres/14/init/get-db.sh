#!/bin/bash
set -e

echo "--> All existing Databases:"

psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" <<-EOSQL
	SELECT datname FROM pg_database;
EOSQL