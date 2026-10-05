using System.Security.Cryptography;
using System.Text;
using MailArchiver.Data;
using MailArchiver.Models;
using MailArchiver.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace MailArchiver.Tests.Services;

/// <summary>
/// Integration tests for the write-once archive_worm."ArchivedEmailSources" table
/// (migration MigrateV2610_1): hash verification on insert, rejected UPDATE/TRUNCATE,
/// DELETE only after RetainUntil (independent of IsLocked), and the archive_worm.harden()
/// role split.
/// Every test runs in a transaction that is rolled back.
/// </summary>
[Collection(TestDbFixture.CollectionName)]
public class ArchivedEmailSourceWormTests
{
    private const string InsufficientPrivilege = "42501";
    private const string RaiseException = "P0001";

    private static readonly byte[] SampleMime = Encoding.ASCII.GetBytes(
        "From: a@x.com\r\nTo: b@x.com\r\nSubject: worm\r\nMessage-ID: <worm@test.local>\r\n\r\nBody\r\n");

    private readonly TestDbFixture _fixture;
    public ArchivedEmailSourceWormTests(TestDbFixture fixture) => _fixture = fixture;

    private static string Sha256Hex(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static async Task<ArchivedEmail> SeedEmailAsync(MailArchiverDbContext ctx, bool isLocked)
    {
        var account = new MailAccount
        {
            Name = $"worm-{Guid.NewGuid():N}".Substring(0, 25),
            EmailAddress = $"{Guid.NewGuid():N}@test.local",
            Provider = ProviderType.IMAP,
            IsEnabled = true,
            LastSync = DateTime.UtcNow
        };
        ctx.MailAccounts.Add(account);
        await ctx.SaveChangesAsync();

        var email = new ArchivedEmail
        {
            MailAccountId = account.Id,
            MessageId = Guid.NewGuid().ToString(),
            Subject = "worm-test",
            From = "a@x.com",
            To = "b@x.com",
            Cc = string.Empty,
            Bcc = string.Empty,
            Body = "Body",
            HtmlBody = string.Empty,
            SentDate = DateTime.UtcNow.AddDays(-1),
            ReceivedDate = DateTime.UtcNow,
            IsOutgoing = false,
            HasAttachments = false,
            FolderName = "INBOX",
            IsLocked = isLocked
        };
        ctx.ArchivedEmails.Add(email);
        await ctx.SaveChangesAsync();

        // The column default may lock new rows (DeletionPolicy); force the requested state.
        await ctx.Database.ExecuteSqlRawAsync(
            "UPDATE mail_archiver.\"ArchivedEmails\" SET \"IsLocked\" = {0} WHERE \"Id\" = {1}", isLocked, email.Id);
        return email;
    }

    private static Task InsertSourceAsync(MailArchiverDbContext ctx, int emailId, byte[] mime, string sha256) =>
        ctx.Database.ExecuteSqlRawAsync(
            "INSERT INTO archive_worm.\"ArchivedEmailSources\" (\"ArchivedEmailId\", \"RawMime\", \"Size\", \"Sha256\", \"Source\") " +
            "VALUES ({0}, {1}, 0, {2}, 'imap')", emailId, mime, sha256);

    /// <summary>
    /// Inserts a source whose retention has already expired. The insert trigger always sets
    /// RetainUntil to the future, so it is bypassed here (test user is a superuser).
    /// </summary>
    private static async Task InsertExpiredSourceAsync(MailArchiverDbContext ctx, int emailId)
    {
        await ctx.Database.ExecuteSqlRawAsync(
            "ALTER TABLE archive_worm.\"ArchivedEmailSources\" DISABLE TRIGGER verify_source_insert");
        await ctx.Database.ExecuteSqlRawAsync(
            "INSERT INTO archive_worm.\"ArchivedEmailSources\" (\"ArchivedEmailId\", \"RawMime\", \"Size\", \"Sha256\", \"Source\", \"RetainUntil\") " +
            "VALUES ({0}, {1}, {2}, {3}, 'imap', now() - interval '1 day')",
            emailId, SampleMime, (long)SampleMime.Length, Sha256Hex(SampleMime));
        await ctx.Database.ExecuteSqlRawAsync(
            "ALTER TABLE archive_worm.\"ArchivedEmailSources\" ENABLE TRIGGER verify_source_insert");
    }

    private static Task<int> CountSourcesAsync(MailArchiverDbContext ctx, int emailId) =>
        ctx.ArchivedEmailSources.CountAsync(s => s.ArchivedEmailId == emailId);

    /// <summary>
    /// Runs <paramref name="action"/> inside a savepoint and returns the PostgreSQL error code
    /// it failed with, so the surrounding transaction stays usable after the expected error.
    /// </summary>
    private static async Task<string?> SqlStateOfAsync(MailArchiverDbContext ctx, Func<Task> action)
    {
        await ctx.Database.ExecuteSqlRawAsync("SAVEPOINT worm_test");
        try
        {
            await action();
            await ctx.Database.ExecuteSqlRawAsync("RELEASE SAVEPOINT worm_test");
            return null;
        }
        catch (Exception ex)
        {
            await ctx.Database.ExecuteSqlRawAsync("ROLLBACK TO SAVEPOINT worm_test");
            for (var e = ex; e != null; e = e.InnerException)
            {
                if (e is PostgresException pg) return pg.SqlState;
            }
            throw;
        }
    }

    [Fact]
    public async Task Insert_SetsSizeHashAndCaptureTimeOnTheDatabaseSide()
    {
        await using var scope = await _fixture.CreateTransactionalContextAsync();
        var ctx = scope.Context;
        var email = await SeedEmailAsync(ctx, isLocked: false);

        // Upper-case hash and a backdated capture time: the database normalizes the
        // hash and replaces the time with now().
        var source = new ArchivedEmailSource
        {
            ArchivedEmailId = email.Id,
            RawMime = SampleMime,
            Sha256 = Sha256Hex(SampleMime).ToUpperInvariant(),
            CapturedAt = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            RetainUntil = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            Size = 1,
            Source = ArchivedEmailSourceKinds.Imap
        };
        ctx.ArchivedEmailSources.Add(source);
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();

        var stored = await ctx.ArchivedEmailSources.SingleAsync(s => s.ArchivedEmailId == email.Id);
        Assert.Equal(SampleMime, stored.RawMime);
        Assert.Equal(Sha256Hex(SampleMime), stored.Sha256);
        Assert.Equal(SampleMime.Length, stored.Size);
        Assert.True(stored.CapturedAt > DateTime.UtcNow.AddDays(-1));
        // End of the 8th year after the capture year.
        Assert.Equal(new DateTime(stored.CapturedAt.ToUniversalTime().Year + 9, 1, 1), stored.RetainUntil.ToUniversalTime().Date);

        await scope.RollbackAsync();
    }

    [Fact]
    public async Task Insert_WithWrongHash_IsRejected()
    {
        await using var scope = await _fixture.CreateTransactionalContextAsync();
        var ctx = scope.Context;
        var email = await SeedEmailAsync(ctx, isLocked: false);

        var state = await SqlStateOfAsync(ctx, () => InsertSourceAsync(ctx, email.Id, SampleMime, new string('0', 64)));

        Assert.Equal(RaiseException, state);
        Assert.Equal(0, await CountSourcesAsync(ctx, email.Id));
        await scope.RollbackAsync();
    }

    [Fact]
    public async Task Update_IsRejected_EvenForUnlockedEmails()
    {
        await using var scope = await _fixture.CreateTransactionalContextAsync();
        var ctx = scope.Context;
        var email = await SeedEmailAsync(ctx, isLocked: false);
        await InsertSourceAsync(ctx, email.Id, SampleMime, Sha256Hex(SampleMime));

        var state = await SqlStateOfAsync(ctx, () => ctx.Database.ExecuteSqlRawAsync(
            "UPDATE archive_worm.\"ArchivedEmailSources\" SET \"Source\" = 'graph' WHERE \"ArchivedEmailId\" = {0}", email.Id));

        Assert.Equal(RaiseException, state);
        await scope.RollbackAsync();
    }

    [Fact]
    public async Task Delete_IsRejectedWhileRetained_EvenWhenTheEmailIsUnlocked()
    {
        await using var scope = await _fixture.CreateTransactionalContextAsync();
        var ctx = scope.Context;
        var email = await SeedEmailAsync(ctx, isLocked: false);
        await InsertSourceAsync(ctx, email.Id, SampleMime, Sha256Hex(SampleMime));

        // Neither directly nor through the cascade of the (unlocked) email.
        Assert.Equal(RaiseException, await SqlStateOfAsync(ctx, () => ctx.Database.ExecuteSqlRawAsync(
            "DELETE FROM archive_worm.\"ArchivedEmailSources\" WHERE \"ArchivedEmailId\" = {0}", email.Id)));
        Assert.Equal(RaiseException, await SqlStateOfAsync(ctx, () => ctx.Database.ExecuteSqlRawAsync(
            "DELETE FROM mail_archiver.\"ArchivedEmails\" WHERE \"Id\" = {0}", email.Id)));

        Assert.Equal(1, await CountSourcesAsync(ctx, email.Id));
        Assert.Equal(1, await ctx.ArchivedEmails.CountAsync(e => e.Id == email.Id));
        await scope.RollbackAsync();
    }

    [Fact]
    public async Task Truncate_IsRejected()
    {
        await using var scope = await _fixture.CreateTransactionalContextAsync();
        var ctx = scope.Context;

        var state = await SqlStateOfAsync(ctx, () => ctx.Database.ExecuteSqlRawAsync(
            "TRUNCATE archive_worm.\"ArchivedEmailSources\""));

        Assert.Equal(RaiseException, state);
        await scope.RollbackAsync();
    }

    [Fact]
    public async Task DeletingAnEmailWhoseRetentionExpired_CascadesToItsSource()
    {
        await using var scope = await _fixture.CreateTransactionalContextAsync();
        var ctx = scope.Context;
        var email = await SeedEmailAsync(ctx, isLocked: false);
        await InsertExpiredSourceAsync(ctx, email.Id);

        Assert.Null(await SqlStateOfAsync(ctx, () => ctx.Database.ExecuteSqlRawAsync(
            "DELETE FROM mail_archiver.\"ArchivedEmails\" WHERE \"Id\" = {0}", email.Id)));
        Assert.Equal(0, await CountSourcesAsync(ctx, email.Id));
        await scope.RollbackAsync();
    }

    [Fact]
    public async Task Harden_LeavesTheAppRoleOnlySelectAndInsert()
    {
        await using var scope = await _fixture.CreateTransactionalContextAsync();
        var ctx = scope.Context;
        var suffix = Guid.NewGuid().ToString("N").Substring(0, 8);
        var appRole = $"worm_app_{suffix}";
        var ownerRole = $"worm_owner_{suffix}";

        var email = await SeedEmailAsync(ctx, isLocked: false);
        var expired = await SeedEmailAsync(ctx, isLocked: false);
        await InsertExpiredSourceAsync(ctx, expired.Id);

        // A non-superuser application role with full rights on the regular schema.
        await ctx.Database.ExecuteSqlRawAsync($"CREATE ROLE {appRole} NOLOGIN");
        await ctx.Database.ExecuteSqlRawAsync($"GRANT USAGE ON SCHEMA mail_archiver TO {appRole}");
        await ctx.Database.ExecuteSqlRawAsync(
            $"GRANT SELECT, INSERT, UPDATE, DELETE ON mail_archiver.\"ArchivedEmails\" TO {appRole}");
        await ctx.Database.ExecuteSqlRawAsync("SELECT archive_worm.harden({0}, {1})", appRole, ownerRole);

        await ctx.Database.ExecuteSqlRawAsync($"SET LOCAL ROLE {appRole}");

        // Allowed: write once, read.
        Assert.Null(await SqlStateOfAsync(ctx, () => InsertSourceAsync(ctx, email.Id, SampleMime, Sha256Hex(SampleMime))));
        Assert.Equal(1, await CountSourcesAsync(ctx, email.Id));

        // Not allowed: change, delete directly, or tamper with the protection itself.
        Assert.Equal(InsufficientPrivilege, await SqlStateOfAsync(ctx, () => ctx.Database.ExecuteSqlRawAsync(
            "UPDATE archive_worm.\"ArchivedEmailSources\" SET \"Source\" = 'graph' WHERE \"ArchivedEmailId\" = {0}", email.Id)));
        Assert.Equal(InsufficientPrivilege, await SqlStateOfAsync(ctx, () => ctx.Database.ExecuteSqlRawAsync(
            "DELETE FROM archive_worm.\"ArchivedEmailSources\" WHERE \"ArchivedEmailId\" = {0}", email.Id)));
        Assert.Equal(InsufficientPrivilege, await SqlStateOfAsync(ctx, () => ctx.Database.ExecuteSqlRawAsync(
            "DROP TRIGGER prevent_source_update ON archive_worm.\"ArchivedEmailSources\"")));
        Assert.Equal(InsufficientPrivilege, await SqlStateOfAsync(ctx, () => ctx.Database.ExecuteSqlRawAsync(
            "CREATE OR REPLACE FUNCTION archive_worm.prevent_source_update() RETURNS trigger AS 'BEGIN RETURN NEW; END;' LANGUAGE plpgsql")));

        // Unlocking the email does not help while the original is retained.
        Assert.Equal(RaiseException, await SqlStateOfAsync(ctx, () => ctx.Database.ExecuteSqlRawAsync(
            "DELETE FROM mail_archiver.\"ArchivedEmails\" WHERE \"Id\" = {0}", email.Id)));
        Assert.Equal(1, await CountSourcesAsync(ctx, email.Id));

        // After retention, deleting the email removes its source through the cascade.
        Assert.Null(await SqlStateOfAsync(ctx, () => ctx.Database.ExecuteSqlRawAsync(
            "DELETE FROM mail_archiver.\"ArchivedEmails\" WHERE \"Id\" = {0}", expired.Id)));
        Assert.Equal(0, await CountSourcesAsync(ctx, expired.Id));

        await scope.RollbackAsync();
    }
}
