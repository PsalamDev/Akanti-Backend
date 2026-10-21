-- Case-insensitive email uniqueness.
--
-- The original unique index on "Users"."Email" is case-sensitive, because PostgreSQL
-- compares text exactly. That let two accounts exist for one address: registering
-- ADMIN@AKANTI.COM alongside admin@akanti.com succeeded, because the duplicate check
-- ("Email" = @email) did not match, while the admin grant check compared
-- case-insensitively. The second account therefore received the Admin role and full
-- access to the admin API.
--
-- This script replaces the exact-match index with one over LOWER("Email"), so the
-- database itself rejects any address that differs only by case.
--
-- Pre-check: the query below must return zero rows, otherwise the index cannot be
-- created and the duplicates must be merged or removed first.
--
--   SELECT LOWER("Email") AS norm, count(*) AS n, array_agg("Email") AS variants
--   FROM "Users" GROUP BY LOWER("Email") HAVING count(*) > 1;

BEGIN;

-- Normalise any stored value that is not already lowercase, so the new index and the
-- application agree on the canonical form. New registrations are lowercased before they
-- are written, so this only affects rows created before the fix.
UPDATE "Users"
SET "Email" = LOWER("Email")
WHERE "Email" <> LOWER("Email");

-- Replace the case-sensitive unique constraint with a case-insensitive one.
-- The index name matches the EF model, so a database created fresh by EnsureCreated()
-- and one migrated by this script end up with the same schema.
DROP INDEX IF EXISTS "IX_Users_Email";
DROP INDEX IF EXISTS "IX_Users_Email_Lower";
CREATE UNIQUE INDEX "IX_Users_Email_Lower" ON "Users" (LOWER("Email"));

COMMIT;

-- Verify: the index must exist and be unique over the lowercased column.
SELECT indexname, indexdef FROM pg_indexes
WHERE tablename = 'Users' AND indexname LIKE 'IX_Users_Email%';
