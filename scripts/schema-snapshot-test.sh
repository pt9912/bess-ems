#!/usr/bin/env bash
# RM-M2-MIG-03 snapshot-test: verify the committed 0001_initial.sql
# produces a database semantically equivalent to the M1
# BessDbInitializer + BessDbSchema.CreateScript path.
#
# Equivalence is established via three property comparisons (columns,
# constraints, indexes) read from information_schema / pg_indexes —
# pg_dump itself reorders columns alphabetically by name and includes
# random restrict-keys, both of which would create cosmetic diffs.
# The property queries return the structural truth only.
#
# Usage:
#   scripts/schema-snapshot-test.sh
#
# The script spins up an ephemeral Postgres container on a random
# free port, applies both schemas to two fresh databases, runs the
# property queries, and tears the container down on exit. Override
# the image with PG_IMAGE=postgres:17 etc.

set -euo pipefail

REPO_ROOT="$(cd "$(dirname "$0")/.." && pwd)"
INITIAL_SQL="$REPO_ROOT/src/adapters/driven/BatteryEms.Adapters.Persistence/Migrations/RunOnce/0001_initial.sql"
SCHEMA_CS="$REPO_ROOT/src/adapters/driven/BatteryEms.Adapters.Persistence/BessDbSchema.cs"

PG_IMAGE="${PG_IMAGE:-postgres:16}"
CONTAINER_NAME="${CONTAINER_NAME:-bess-ems-schema-snapshot}"

# Workdir for extracted SQL + diff artefacts. Survives outside the
# script for post-mortem inspection on failure.
WORKDIR="$(mktemp -d -t bess-schema-snapshot.XXXXXX)"
echo "[snapshot] workdir: $WORKDIR"

cleanup() {
  local exit_code=$?
  docker rm -f "$CONTAINER_NAME" >/dev/null 2>&1 || true
  if [ "$exit_code" -eq 0 ]; then
    rm -rf "$WORKDIR"
  else
    echo "[snapshot] FAILED — workdir kept at: $WORKDIR" >&2
  fi
  exit $exit_code
}
trap cleanup EXIT

# --- 1. Extract the BessDbSchema.CreateScript C# raw-string body ----------
# The C# source uses a triple-quoted raw string literal. sed extracts
# everything between `CreateScript = """` and the closing `""";`.
sed -n '/CreateScript = """/,/""";/p' "$SCHEMA_CS" | sed '1d;$d' > "$WORKDIR/initial-from-cs.sql"
init_lines=$(wc -l < "$WORKDIR/initial-from-cs.sql")
if [ "$init_lines" -lt 50 ]; then
  echo "[snapshot] FATAL: extracted $init_lines lines from $SCHEMA_CS — expected ≥50; raw-string format may have changed" >&2
  exit 2
fi
echo "[snapshot] extracted $init_lines lines of M1-initializer DDL"

# --- 2. Confirm the migration SQL exists -----------------------------------
if [ ! -f "$INITIAL_SQL" ]; then
  echo "[snapshot] FATAL: $INITIAL_SQL not found — run \`make schema-generate\` first" >&2
  exit 2
fi
mig_lines=$(wc -l < "$INITIAL_SQL")
echo "[snapshot] using $mig_lines lines of generated migration DDL"

# --- 3. Spin up Postgres ---------------------------------------------------
docker rm -f "$CONTAINER_NAME" >/dev/null 2>&1 || true
docker run --rm -d --name "$CONTAINER_NAME" \
  -e POSTGRES_PASSWORD=postgres -e POSTGRES_DB=postgres \
  "$PG_IMAGE" >/dev/null
echo "[snapshot] container started: $CONTAINER_NAME ($PG_IMAGE)"

for _ in $(seq 1 30); do
  if docker exec "$CONTAINER_NAME" pg_isready -U postgres >/dev/null 2>&1; then
    break
  fi
  sleep 1
done
docker exec "$CONTAINER_NAME" pg_isready -U postgres >/dev/null

# --- 4. Apply both DDLs to separate databases ------------------------------
docker exec "$CONTAINER_NAME" createdb -U postgres bessems_init
docker exec "$CONTAINER_NAME" createdb -U postgres bessems_mig

docker cp "$WORKDIR/initial-from-cs.sql" "$CONTAINER_NAME:/tmp/init.sql" >/dev/null
docker cp "$INITIAL_SQL" "$CONTAINER_NAME:/tmp/mig.sql" >/dev/null

if ! docker exec "$CONTAINER_NAME" psql -U postgres -d bessems_init -v ON_ERROR_STOP=1 -f /tmp/init.sql > "$WORKDIR/init-apply.log" 2>&1; then
  echo "[snapshot] FAILED to apply BessDbSchema.CreateScript:" >&2
  cat "$WORKDIR/init-apply.log" >&2
  exit 3
fi
if ! docker exec "$CONTAINER_NAME" psql -U postgres -d bessems_mig -v ON_ERROR_STOP=1 -f /tmp/mig.sql > "$WORKDIR/mig-apply.log" 2>&1; then
  echo "[snapshot] FAILED to apply 0001_initial.sql:" >&2
  cat "$WORKDIR/mig-apply.log" >&2
  exit 3
fi
echo "[snapshot] both schemas applied"

# --- 5. Property comparisons ----------------------------------------------
# Each query is sorted deterministically; the diff between the two
# DBs must be empty. The migrator's own __schema_versions journal
# table is excluded — it lives only on the migration side once
# BessDbMigrator (RM-M2-MIG-04) wraps DbUp; for this MIG-03 test we
# apply 0001_initial.sql directly via psql, so the journal table is
# not present. The exclusion stays in the queries so the same script
# also runs cleanly against a BessDbMigrator-loaded DB later.
COL_QUERY="
  SELECT table_name, column_name, data_type,
         character_maximum_length, is_nullable, column_default
  FROM information_schema.columns
  WHERE table_schema='public' AND table_name <> '__schema_versions'
  ORDER BY table_name, column_name;"

CON_QUERY="
  SELECT tc.table_name, tc.constraint_name, tc.constraint_type,
         string_agg(kcu.column_name, ',' ORDER BY kcu.ordinal_position)
  FROM information_schema.table_constraints tc
  LEFT JOIN information_schema.key_column_usage kcu
    ON tc.constraint_name = kcu.constraint_name
   AND tc.table_name = kcu.table_name
  WHERE tc.table_schema='public'
    AND tc.table_name <> '__schema_versions'
    AND tc.constraint_type IN ('PRIMARY KEY','UNIQUE','FOREIGN KEY','CHECK')
    AND tc.constraint_name NOT LIKE '%_not_null'
  GROUP BY tc.table_name, tc.constraint_name, tc.constraint_type
  ORDER BY tc.table_name, tc.constraint_name;"

IDX_QUERY="
  SELECT tablename, indexname, indexdef FROM pg_indexes
  WHERE schemaname='public' AND tablename <> '__schema_versions'
  ORDER BY tablename, indexname;"

dump_property() {
  local db=$1 name=$2 query=$3
  docker exec "$CONTAINER_NAME" psql -U postgres -d "$db" -t -A -F '|' -c "$query" \
    > "$WORKDIR/$name-$db.txt"
}

dump_property bessems_init columns "$COL_QUERY"
dump_property bessems_mig  columns "$COL_QUERY"
dump_property bessems_init constraints "$CON_QUERY"
dump_property bessems_mig  constraints "$CON_QUERY"
dump_property bessems_init indexes "$IDX_QUERY"
dump_property bessems_mig  indexes "$IDX_QUERY"

# --- 6. Diff and report ---------------------------------------------------
diff_failed=0
for prop in columns constraints indexes; do
  if ! diff -u "$WORKDIR/$prop-bessems_init.txt" "$WORKDIR/$prop-bessems_mig.txt" > "$WORKDIR/$prop.diff"; then
    echo "[snapshot] $prop differ:" >&2
    cat "$WORKDIR/$prop.diff" >&2
    diff_failed=1
  fi
done

if [ "$diff_failed" -ne 0 ]; then
  echo "[snapshot] FAILED — schema drift between BessDbSchema.CreateScript and 0001_initial.sql" >&2
  exit 4
fi

echo "[snapshot] PASS — columns, constraints and indexes match across both schemas"
