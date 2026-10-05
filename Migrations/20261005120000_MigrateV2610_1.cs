using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MailArchiver.Migrations
{
    /// <inheritdoc />
    public partial class MigrateV2610_1 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ============================================================
            // Write-once storage for the original MIME message
            // ============================================================
            // ArchivedEmails holds the parsed, searchable form of a mail.
            // The original bytes as received (needed to prove that the
            // archived copy matches what was delivered) go into a separate
            // schema, archive_worm, so that it can be owned by a different
            // database role than the application (see doc/WormStorage.md).
            //
            // The table is write-once, enforced by triggers:
            //   * INSERT   - the hash is recomputed and must match, Size and
            //                CapturedAt are set by the database
            //   * UPDATE   - always rejected
            //   * DELETE   - only once the parent ArchivedEmails row is gone
            //                or unlocked (the existing lock decides)
            //   * TRUNCATE - always rejected
            //
            // Deleting a mail cascades to its source. Idempotent: only creates
            // what is missing.

            migrationBuilder.Sql(@"CREATE SCHEMA IF NOT EXISTS archive_worm;");

            migrationBuilder.Sql(@"
                DO $$
                BEGIN
                    IF NOT EXISTS (
                        SELECT 1 FROM information_schema.tables
                        WHERE table_schema = 'archive_worm'
                          AND table_name = 'ArchivedEmailSources'
                    ) THEN
                        CREATE TABLE archive_worm.""ArchivedEmailSources"" (
                            ""ArchivedEmailId"" integer NOT NULL,
                            ""RawMime"" bytea NOT NULL,
                            ""Size"" bigint NOT NULL,
                            ""Sha256"" character(64) NOT NULL,
                            ""CapturedAt"" timestamp with time zone NOT NULL DEFAULT now(),
                            ""Source"" character varying(20) NOT NULL,
                            CONSTRAINT ""PK_ArchivedEmailSources"" PRIMARY KEY (""ArchivedEmailId""),
                            CONSTRAINT ""FK_ArchivedEmailSources_ArchivedEmails_ArchivedEmailId""
                                FOREIGN KEY (""ArchivedEmailId"")
                                REFERENCES mail_archiver.""ArchivedEmails"" (""Id"") ON DELETE CASCADE,
                            CONSTRAINT ""CK_ArchivedEmailSources_Source""
                                CHECK (""Source"" IN ('imap', 'eml-import', 'mbox-import', 'graph', 'reconstructed'))
                        );

                        COMMENT ON TABLE archive_worm.""ArchivedEmailSources""
                            IS 'Write-once original MIME message per archived email (GoBD: original form, integrity)';
                        COMMENT ON COLUMN archive_worm.""ArchivedEmailSources"".""ArchivedEmailId""
                            IS 'ArchivedEmails.Id this source belongs to (1:1)';
                        COMMENT ON COLUMN archive_worm.""ArchivedEmailSources"".""RawMime""
                            IS 'Raw RFC 5322 message bytes exactly as received';
                        COMMENT ON COLUMN archive_worm.""ArchivedEmailSources"".""Size""
                            IS 'octet_length(RawMime), set by trigger on insert';
                        COMMENT ON COLUMN archive_worm.""ArchivedEmailSources"".""Sha256""
                            IS 'SHA-256 of RawMime as lowercase hex, verified by trigger on insert';
                        COMMENT ON COLUMN archive_worm.""ArchivedEmailSources"".""CapturedAt""
                            IS 'Capture time, set by trigger on insert (cannot be backdated)';
                        COMMENT ON COLUMN archive_worm.""ArchivedEmailSources"".""Source""
                            IS 'Origin of the bytes: imap, eml-import, mbox-import, graph or reconstructed';
                    END IF;
                END $$;
            ");

            migrationBuilder.Sql(@"
                CREATE OR REPLACE FUNCTION archive_worm.verify_source_insert()
                RETURNS TRIGGER AS $$
                DECLARE
                    actual_hash text;
                BEGIN
                    actual_hash := encode(sha256(NEW.""RawMime""), 'hex');
                    IF NEW.""Sha256"" IS NULL OR lower(NEW.""Sha256"") <> actual_hash THEN
                        RAISE EXCEPTION 'Original message hash mismatch for email % (given %, actual %)',
                            NEW.""ArchivedEmailId"", NEW.""Sha256"", actual_hash;
                    END IF;
                    NEW.""Sha256"" := actual_hash;
                    NEW.""Size"" := octet_length(NEW.""RawMime"");
                    NEW.""CapturedAt"" := now();
                    RETURN NEW;
                END;
                $$ LANGUAGE plpgsql;

                CREATE OR REPLACE FUNCTION archive_worm.prevent_source_update()
                RETURNS TRIGGER AS $$
                BEGIN
                    RAISE EXCEPTION 'Original message of email % is write-once and cannot be modified (compliance requirement)',
                        OLD.""ArchivedEmailId"";
                END;
                $$ LANGUAGE plpgsql;

                CREATE OR REPLACE FUNCTION archive_worm.prevent_locked_source_deletion()
                RETURNS TRIGGER AS $$
                BEGIN
                    IF EXISTS (
                        SELECT 1 FROM mail_archiver.""ArchivedEmails""
                        WHERE ""Id"" = OLD.""ArchivedEmailId"" AND ""IsLocked"" = true
                    ) THEN
                        RAISE EXCEPTION 'Original message of email % is locked and cannot be deleted (compliance requirement - retention period active)',
                            OLD.""ArchivedEmailId"";
                    END IF;
                    RETURN OLD;
                END;
                $$ LANGUAGE plpgsql;

                CREATE OR REPLACE FUNCTION archive_worm.prevent_source_truncate()
                RETURNS TRIGGER AS $$
                BEGIN
                    RAISE EXCEPTION 'Original messages cannot be truncated (compliance requirement)';
                END;
                $$ LANGUAGE plpgsql;
            ");

            migrationBuilder.Sql(@"
                DO $$
                BEGIN
                    IF NOT EXISTS (SELECT 1 FROM pg_trigger WHERE tgname = 'verify_source_insert') THEN
                        CREATE TRIGGER verify_source_insert
                            BEFORE INSERT ON archive_worm.""ArchivedEmailSources""
                            FOR EACH ROW EXECUTE FUNCTION archive_worm.verify_source_insert();
                    END IF;
                    IF NOT EXISTS (SELECT 1 FROM pg_trigger WHERE tgname = 'prevent_source_update') THEN
                        CREATE TRIGGER prevent_source_update
                            BEFORE UPDATE ON archive_worm.""ArchivedEmailSources""
                            FOR EACH ROW EXECUTE FUNCTION archive_worm.prevent_source_update();
                    END IF;
                    IF NOT EXISTS (SELECT 1 FROM pg_trigger WHERE tgname = 'prevent_locked_source_deletion') THEN
                        CREATE TRIGGER prevent_locked_source_deletion
                            BEFORE DELETE ON archive_worm.""ArchivedEmailSources""
                            FOR EACH ROW EXECUTE FUNCTION archive_worm.prevent_locked_source_deletion();
                    END IF;
                    IF NOT EXISTS (SELECT 1 FROM pg_trigger WHERE tgname = 'prevent_source_truncate') THEN
                        CREATE TRIGGER prevent_source_truncate
                            BEFORE TRUNCATE ON archive_worm.""ArchivedEmailSources""
                            FOR EACH STATEMENT EXECUTE FUNCTION archive_worm.prevent_source_truncate();
                    END IF;
                END $$;
            ");

            // Optional hardening, run once by a database administrator (not by the
            // application): hands the archive_worm schema, its table and all its
            // functions to a separate NOLOGIN owner role and leaves the application
            // role with nothing but SELECT and INSERT. The application can then no
            // longer drop or replace the triggers. Only effective when the
            // application role is not a superuser. See doc/WormStorage.md.
            migrationBuilder.Sql(@"
                CREATE OR REPLACE FUNCTION archive_worm.harden(app_role name, owner_role name DEFAULT 'mailarchiver_worm_owner')
                RETURNS void AS $$
                DECLARE
                    fn record;
                BEGIN
                    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = owner_role) THEN
                        EXECUTE format('CREATE ROLE %I NOLOGIN', owner_role);
                    END IF;

                    -- The owner runs the cascade delete and the delete trigger's lookup.
                    EXECUTE format('GRANT USAGE ON SCHEMA mail_archiver TO %I', owner_role);
                    EXECUTE format('GRANT SELECT ON mail_archiver.""ArchivedEmails"" TO %I', owner_role);

                    EXECUTE format('ALTER SCHEMA archive_worm OWNER TO %I', owner_role);
                    EXECUTE format('ALTER TABLE archive_worm.""ArchivedEmailSources"" OWNER TO %I', owner_role);
                    FOR fn IN
                        SELECT p.oid::regprocedure AS sig
                        FROM pg_proc p JOIN pg_namespace n ON n.oid = p.pronamespace
                        WHERE n.nspname = 'archive_worm'
                    LOOP
                        EXECUTE format('ALTER FUNCTION %s OWNER TO %I', fn.sig, owner_role);
                    END LOOP;

                    EXECUTE 'REVOKE ALL ON SCHEMA archive_worm FROM PUBLIC';
                    EXECUTE format('REVOKE ALL ON archive_worm.""ArchivedEmailSources"" FROM PUBLIC, %I', app_role);
                    EXECUTE format('GRANT USAGE ON SCHEMA archive_worm TO %I', app_role);
                    EXECUTE format('GRANT SELECT, INSERT ON archive_worm.""ArchivedEmailSources"" TO %I', app_role);
                END;
                $$ LANGUAGE plpgsql;

                COMMENT ON FUNCTION archive_worm.harden(name, name)
                    IS 'Admin-only: move archive_worm to a separate owner role and restrict the app role to SELECT/INSERT';
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Dropping the table discards every stored original. Only the
            // schema owner can do this, which is intended.
            migrationBuilder.Sql(@"
                DROP TABLE IF EXISTS archive_worm.""ArchivedEmailSources"";
                DROP FUNCTION IF EXISTS archive_worm.verify_source_insert();
                DROP FUNCTION IF EXISTS archive_worm.prevent_source_update();
                DROP FUNCTION IF EXISTS archive_worm.prevent_locked_source_deletion();
                DROP FUNCTION IF EXISTS archive_worm.prevent_source_truncate();
                DROP FUNCTION IF EXISTS archive_worm.harden(name, name);
                DROP SCHEMA IF EXISTS archive_worm;
            ");
        }
    }
}
