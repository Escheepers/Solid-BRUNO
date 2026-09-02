-- Enables the btree_gist extension needed by AD-7's Postgres EXCLUDE USING GIST constraint
-- (booking overlap backstop), added ahead of time in Epic 1 so Epic 4 needs no schema change
-- to turn it on. Runs automatically on first container initialization only (files in
-- /docker-entrypoint-initdb.d are ignored once the data directory already exists).
CREATE EXTENSION IF NOT EXISTS btree_gist;
