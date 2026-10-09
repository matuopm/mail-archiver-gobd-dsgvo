using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MailArchiver.Migrations
{
    /// <inheritdoc />
    public partial class MigrateV2610_5 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Optional audit period of an auditor (dates inclusive)
            migrationBuilder.Sql(@"
                ALTER TABLE mail_archiver.""Users""
                    ADD COLUMN IF NOT EXISTS ""AuditFromDate"" timestamp without time zone NULL,
                    ADD COLUMN IF NOT EXISTS ""AuditToDate"" timestamp without time zone NULL;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                ALTER TABLE mail_archiver.""Users""
                    DROP COLUMN IF EXISTS ""AuditFromDate"",
                    DROP COLUMN IF EXISTS ""AuditToDate"";
            ");
        }
    }
}
