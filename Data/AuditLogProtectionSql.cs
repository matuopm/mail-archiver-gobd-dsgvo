namespace MailArchiver.Data
{
    /// <summary>
    /// SQL that moves the access log into the write-once schema archive_worm and chains its
    /// entries by SHA-256. Used by migration MigrateV2610_3 and, identically, by the admin
    /// script doc/sql/MigrateV2610_3-audit-log.sql for hardened databases (a test keeps both
    /// in sync). Idempotent: running it again changes nothing.
    ///
    /// Every new entry gets the next "ChainSeq", the "Hash" of the previous entry as
    /// "PrevHash" and its own "Hash" over all its fields plus "PrevHash". Changing, deleting
    /// or inserting an entry afterwards breaks the chain, which
    /// archive_worm.verify_access_log() reports. UPDATE, DELETE and TRUNCATE are rejected.
    /// </summary>
    public static class AuditLogProtectionSql
    {
        public const string Core = """
-- 1) Move the table next to the stored originals
DO $$
BEGIN
    IF to_regclass('mail_archiver."AccessLogs"') IS NOT NULL
       AND to_regclass('archive_worm."AccessLogs"') IS NULL THEN
        ALTER TABLE mail_archiver."AccessLogs" SET SCHEMA archive_worm;
    END IF;
END $$;

ALTER TABLE archive_worm."AccessLogs"
    ADD COLUMN IF NOT EXISTS "ChainSeq" bigint,
    ADD COLUMN IF NOT EXISTS "PrevHash" character(64),
    ADD COLUMN IF NOT EXISTS "Hash" character(64);

CREATE UNIQUE INDEX IF NOT EXISTS "IX_AccessLogs_ChainSeq" ON archive_worm."AccessLogs" ("ChainSeq");

-- 2) Hash of one entry: all fields, NULL kept distinct from empty text, plus the previous hash
CREATE OR REPLACE FUNCTION archive_worm.access_log_hash(r archive_worm."AccessLogs")
RETURNS text AS $f$
    SELECT encode(sha256(convert_to(concat_ws('|',
        r."ChainSeq", r."Id", quote_nullable(r."Username"), r."Type",
        to_char(r."Timestamp", 'YYYY-MM-DD"T"HH24:MI:SS.US'),
        quote_nullable(r."EmailId"), quote_nullable(r."EmailSubject"), quote_nullable(r."EmailFrom"),
        quote_nullable(r."SearchParameters"), quote_nullable(r."MailAccountId"),
        coalesce(r."PrevHash", '')), 'UTF8')), 'hex');
$f$ LANGUAGE sql IMMUTABLE;

CREATE OR REPLACE FUNCTION archive_worm.chain_access_log()
RETURNS TRIGGER AS $f$
DECLARE
    last_seq bigint;
    last_hash text;
BEGIN
    -- One writer at a time, so every entry links to the one committed before it
    PERFORM pg_advisory_xact_lock(hashtext('archive_worm.AccessLogs chain'));
    SELECT "ChainSeq", "Hash" INTO last_seq, last_hash
      FROM archive_worm."AccessLogs" WHERE "ChainSeq" IS NOT NULL
     ORDER BY "ChainSeq" DESC LIMIT 1;
    NEW."ChainSeq" := coalesce(last_seq, 0) + 1;
    NEW."PrevHash" := last_hash;
    NEW."Hash" := archive_worm.access_log_hash(NEW);
    RETURN NEW;
END;
$f$ LANGUAGE plpgsql;

CREATE OR REPLACE FUNCTION archive_worm.prevent_access_log_change()
RETURNS TRIGGER AS $f$
BEGIN
    RAISE EXCEPTION 'Access log entries cannot be changed or deleted (%)', TG_OP;
END;
$f$ LANGUAGE plpgsql;

-- 3) Chain the entries that already exist, in the order they were written
DO $$
DECLARE
    r archive_worm."AccessLogs";
    seq bigint;
    prev text;
BEGIN
    SELECT coalesce(max("ChainSeq"), 0) INTO seq FROM archive_worm."AccessLogs";
    SELECT "Hash" INTO prev FROM archive_worm."AccessLogs" WHERE "ChainSeq" = seq;
    FOR r IN SELECT * FROM archive_worm."AccessLogs" WHERE "ChainSeq" IS NULL ORDER BY "Id" LOOP
        seq := seq + 1;
        r."ChainSeq" := seq;
        r."PrevHash" := prev;
        prev := archive_worm.access_log_hash(r);
        UPDATE archive_worm."AccessLogs"
           SET "ChainSeq" = seq, "PrevHash" = r."PrevHash", "Hash" = prev
         WHERE "Id" = r."Id";
    END LOOP;
END $$;

-- 4) Triggers (after the backfill, which needs UPDATE)
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_trigger WHERE tgname = 'chain_access_log') THEN
        CREATE TRIGGER chain_access_log BEFORE INSERT ON archive_worm."AccessLogs"
            FOR EACH ROW EXECUTE FUNCTION archive_worm.chain_access_log();
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_trigger WHERE tgname = 'prevent_access_log_change') THEN
        CREATE TRIGGER prevent_access_log_change BEFORE UPDATE OR DELETE ON archive_worm."AccessLogs"
            FOR EACH ROW EXECUTE FUNCTION archive_worm.prevent_access_log_change();
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_trigger WHERE tgname = 'prevent_access_log_truncate') THEN
        CREATE TRIGGER prevent_access_log_truncate BEFORE TRUNCATE ON archive_worm."AccessLogs"
            FOR EACH STATEMENT EXECUTE FUNCTION archive_worm.prevent_access_log_change();
    END IF;
END $$;

-- 5) Check the whole chain: number of intact entries, first broken entry (NULL if none)
--    and the hash of the last intact entry
CREATE OR REPLACE FUNCTION archive_worm.verify_access_log(
    OUT checked bigint, OUT first_broken_seq bigint, OUT head_hash text)
AS $f$
DECLARE
    r archive_worm."AccessLogs";
    prev text;
    expected bigint := 0;
BEGIN
    checked := 0;
    FOR r IN SELECT * FROM archive_worm."AccessLogs" ORDER BY "ChainSeq" NULLS FIRST LOOP
        expected := expected + 1;
        IF r."ChainSeq" IS DISTINCT FROM expected
           OR r."PrevHash" IS DISTINCT FROM prev
           OR r."Hash" IS DISTINCT FROM archive_worm.access_log_hash(r) THEN
            first_broken_seq := coalesce(r."ChainSeq", expected);
            head_hash := prev;
            RETURN;
        END IF;
        prev := r."Hash";
        checked := checked + 1;
    END LOOP;
    head_hash := prev;
END;
$f$ LANGUAGE plpgsql STABLE;

-- 6) harden() now covers every table and sequence in archive_worm
CREATE OR REPLACE FUNCTION archive_worm.harden(app_role name, owner_role name DEFAULT 'mailarchiver_worm_owner')
RETURNS void AS $f$
DECLARE
    obj record;
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = owner_role) THEN
        EXECUTE format('CREATE ROLE %I NOLOGIN', owner_role);
    END IF;

    EXECUTE format('ALTER SCHEMA archive_worm OWNER TO %I', owner_role);
    FOR obj IN SELECT tablename FROM pg_tables WHERE schemaname = 'archive_worm' LOOP
        EXECUTE format('ALTER TABLE archive_worm.%I OWNER TO %I', obj.tablename, owner_role);
        EXECUTE format('REVOKE ALL ON archive_worm.%I FROM PUBLIC, %I', obj.tablename, app_role);
        EXECUTE format('GRANT SELECT, INSERT ON archive_worm.%I TO %I', obj.tablename, app_role);
    END LOOP;
    FOR obj IN SELECT sequencename FROM pg_sequences WHERE schemaname = 'archive_worm' LOOP
        EXECUTE format('GRANT USAGE, SELECT ON SEQUENCE archive_worm.%I TO %I', obj.sequencename, app_role);
    END LOOP;
    FOR obj IN
        SELECT p.oid::regprocedure AS sig
        FROM pg_proc p JOIN pg_namespace n ON n.oid = p.pronamespace
        WHERE n.nspname = 'archive_worm'
    LOOP
        EXECUTE format('ALTER FUNCTION %s OWNER TO %I', obj.sig, owner_role);
    END LOOP;

    EXECUTE 'REVOKE ALL ON SCHEMA archive_worm FROM PUBLIC';
    EXECUTE format('GRANT USAGE ON SCHEMA archive_worm TO %I', app_role);
END;
$f$ LANGUAGE plpgsql;
""";
    }
}
