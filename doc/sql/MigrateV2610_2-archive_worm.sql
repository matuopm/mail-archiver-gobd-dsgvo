-- MigrateV2610_2: retention period of stored originals 10 -> 8 years
--
-- Only needed when archive_worm was hardened (doc/WormStorage.md, step 2), because the
-- application role can then no longer replace functions in that schema.
-- Run as database superuser BEFORE starting the new application version, e.g.:
--   docker compose exec -T postgres psql -U mailuser -d MailArchiver < MigrateV2610_2-archive_worm.sql
--
-- What it does: new originals are retained until the end of the 8th year after the year
-- they were captured. Originals that already exist keep their RetainUntil.
-- Running it twice does no harm.

BEGIN;

CREATE OR REPLACE FUNCTION archive_worm.verify_source_insert()
RETURNS TRIGGER AS $$
DECLARE
    actual_hash text;
BEGIN
    actual_hash := encode(sha256(NEW."RawMime"), 'hex');
    IF NEW."Sha256" IS NULL OR lower(NEW."Sha256") <> actual_hash THEN
        RAISE EXCEPTION 'Original message hash mismatch for email % (given %, actual %)',
            NEW."ArchivedEmailId", NEW."Sha256", actual_hash;
    END IF;
    NEW."Sha256" := actual_hash;
    NEW."Size" := octet_length(NEW."RawMime");
    NEW."CapturedAt" := now();
    -- 8 years, counted from the end of the capture year
    NEW."RetainUntil" := date_trunc('year', now()) + interval '9 years';
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

-- CREATE OR REPLACE keeps the existing owner (mailarchiver_worm_owner) and grants.

COMMENT ON COLUMN archive_worm."ArchivedEmailSources"."RetainUntil"
    IS 'No deletion before this time; set by trigger on insert to the end of the 8th year after the capture year';

COMMIT;
