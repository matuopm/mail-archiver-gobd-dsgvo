using MailArchiver.Data;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MailArchiver.Migrations
{
    /// <inheritdoc />
    public partial class MigrateV2610_3 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ============================================================
            // Access log: write-once with a SHA-256 hash chain
            // ============================================================
            // The table moves from mail_archiver to archive_worm, every entry
            // is chained to the previous one, and UPDATE, DELETE and TRUNCATE
            // are rejected (see Data/AuditLogProtectionSql.cs).
            //
            // In a hardened setup (doc/WormStorage.md) the application role
            // does not own archive_worm. The administrator then runs
            // doc/sql/MigrateV2610_3-audit-log.sql first; this block sees the
            // protected table and skips. If neither has happened, the migration
            // stops with a message instead of leaving the log unprotected.
            migrationBuilder.Sql(@"
                DO $mig$
                DECLARE
                    may_run boolean;
                BEGIN
                    IF to_regclass('archive_worm.""AccessLogs""') IS NOT NULL
                       AND EXISTS (SELECT 1 FROM pg_trigger WHERE tgname = 'chain_access_log') THEN
                        RETURN;
                    END IF;

                    SELECT r.rolsuper OR pg_has_role(current_user, n.nspowner, 'USAGE')
                      INTO may_run
                      FROM pg_namespace n, pg_roles r
                     WHERE n.nspname = 'archive_worm' AND r.rolname = current_user;

                    IF NOT may_run THEN
                        RAISE EXCEPTION 'archive_worm is owned by another role. Run doc/sql/MigrateV2610_3-audit-log.sql as database superuser, then start the application again.';
                    END IF;

                    EXECUTE $core$" + AuditLogProtectionSql.Core + @"$core$;
                END $mig$;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Not rolled back: removing the protection of the access log is an
            // administrator decision.
        }
    }
}
