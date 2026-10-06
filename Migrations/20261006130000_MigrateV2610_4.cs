using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MailArchiver.Migrations
{
    /// <inheritdoc />
    public partial class MigrateV2610_4 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Auditor role: reads all mail accounts, changes nothing
            migrationBuilder.Sql(@"
                ALTER TABLE mail_archiver.""Users""
                    ADD COLUMN IF NOT EXISTS ""IsAuditor"" boolean NOT NULL DEFAULT FALSE;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"ALTER TABLE mail_archiver.""Users"" DROP COLUMN IF EXISTS ""IsAuditor"";");
        }
    }
}
