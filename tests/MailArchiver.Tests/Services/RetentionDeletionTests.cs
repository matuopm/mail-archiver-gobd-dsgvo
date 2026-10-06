using System.Security.Cryptography;
using System.Text;
using MailArchiver.Data;
using MailArchiver.Models;
using MailArchiver.Services;
using MailArchiver.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace MailArchiver.Tests.Services;

/// <summary>
/// Automatic deletion after the retention period (RetentionDeletionService.RunAsync) and the
/// administrator pause (RetentionHold). Every test rolls back, because retained originals
/// cannot be removed by the usual cleanup.
/// </summary>
[Collection(TestDbFixture.CollectionName)]
public class RetentionDeletionTests
{
    private readonly TestDbFixture _fixture;
    public RetentionDeletionTests(TestDbFixture fixture) => _fixture = fixture;

    private static string Sha256Hex(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static async Task<MailAccount> SeedAccountAsync(MailArchiverDbContext ctx, int? localRetentionDays = null)
    {
        var account = new MailAccount
        {
            Name = $"ret-{Guid.NewGuid():N}".Substring(0, 25),
            EmailAddress = $"{Guid.NewGuid():N}@test.local",
            Provider = ProviderType.IMAP,
            IsEnabled = true,
            LastSync = DateTime.UtcNow,
            LocalRetentionDays = localRetentionDays
        };
        ctx.MailAccounts.Add(account);
        await ctx.SaveChangesAsync();
        return account;
    }

    private static async Task<ArchivedEmail> SeedEmailAsync(MailArchiverDbContext ctx, MailAccount account, DateTime? sentDate = null)
    {
        var email = new ArchivedEmail
        {
            MailAccountId = account.Id,
            MessageId = Guid.NewGuid().ToString(),
            Subject = "retention",
            From = "a@x.com",
            To = "b@x.com",
            Cc = string.Empty,
            Bcc = string.Empty,
            Body = "Body",
            HtmlBody = string.Empty,
            SentDate = sentDate ?? DateTime.UtcNow,
            ReceivedDate = DateTime.UtcNow,
            IsOutgoing = false,
            HasAttachments = false,
            FolderName = "INBOX"
        };
        ctx.ArchivedEmails.Add(email);
        await ctx.SaveChangesAsync();
        // Locked like every email under the permanent deletion lock
        await ctx.Database.ExecuteSqlRawAsync(
            "UPDATE mail_archiver.\"ArchivedEmails\" SET \"IsLocked\" = true WHERE \"Id\" = {0}", email.Id);
        return email;
    }

    private static async Task AddSourceAsync(MailArchiverDbContext ctx, int emailId, bool expired)
    {
        var raw = Encoding.ASCII.GetBytes($"Subject: {emailId}\r\n\r\nx\r\n");
        if (!expired)
        {
            await ctx.Database.ExecuteSqlRawAsync(
                "INSERT INTO archive_worm.\"ArchivedEmailSources\" (\"ArchivedEmailId\", \"RawMime\", \"Size\", \"Sha256\", \"Source\") " +
                "VALUES ({0}, {1}, 0, {2}, 'imap')", emailId, raw, Sha256Hex(raw));
            return;
        }

        // The insert trigger always sets a future RetainUntil; bypass it to simulate an old original.
        await ctx.Database.ExecuteSqlRawAsync(
            "ALTER TABLE archive_worm.\"ArchivedEmailSources\" DISABLE TRIGGER verify_source_insert");
        await ctx.Database.ExecuteSqlRawAsync(
            "INSERT INTO archive_worm.\"ArchivedEmailSources\" (\"ArchivedEmailId\", \"RawMime\", \"Size\", \"Sha256\", \"Source\", \"RetainUntil\") " +
            "VALUES ({0}, {1}, {2}, {3}, 'imap', now() - interval '1 day')",
            emailId, raw, (long)raw.Length, Sha256Hex(raw));
        await ctx.Database.ExecuteSqlRawAsync(
            "ALTER TABLE archive_worm.\"ArchivedEmailSources\" ENABLE TRIGGER verify_source_insert");
    }

    [Fact]
    public async Task Run_DeletesOnlyEmailsWhoseOriginalHasExpired()
    {
        await using var scope = await _fixture.CreateTransactionalContextAsync();
        var ctx = scope.Context;
        var account = await SeedAccountAsync(ctx);
        var expired = await SeedEmailAsync(ctx, account);
        var retained = await SeedEmailAsync(ctx, account);
        var withoutOriginal = await SeedEmailAsync(ctx, account);
        await AddSourceAsync(ctx, expired.Id, expired: true);
        await AddSourceAsync(ctx, retained.Id, expired: false);

        var deleted = await RetentionDeletionService.RunAsync(ctx, NullLogger.Instance, 500);

        Assert.True(deleted >= 1);
        Assert.False(await ctx.ArchivedEmails.AnyAsync(e => e.Id == expired.Id));
        Assert.False(await ctx.ArchivedEmailSources.AnyAsync(s => s.ArchivedEmailId == expired.Id));
        Assert.True(await ctx.ArchivedEmails.AnyAsync(e => e.Id == retained.Id));
        Assert.True(await ctx.ArchivedEmails.AnyAsync(e => e.Id == withoutOriginal.Id));
        Assert.True(await ctx.AccessLogs.AnyAsync(l =>
            l.Type == AccessLogType.Retention && l.SearchParameters!.StartsWith("@log:RetentionAutoDeleted")));

        await scope.RollbackAsync();
    }

    [Fact]
    public async Task Run_DeletesNothingWhilePaused()
    {
        await using var scope = await _fixture.CreateTransactionalContextAsync();
        var ctx = scope.Context;
        var account = await SeedAccountAsync(ctx);
        var expired = await SeedEmailAsync(ctx, account);
        await AddSourceAsync(ctx, expired.Id, expired: true);
        ctx.RetentionHolds.Add(new RetentionHold { StartedBy = "admin", Reason = "tax audit" });
        await ctx.SaveChangesAsync();

        Assert.Equal(0, await RetentionDeletionService.RunAsync(ctx, NullLogger.Instance, 500));
        Assert.True(await ctx.ArchivedEmails.AnyAsync(e => e.Id == expired.Id));

        // Ending the pause lets the next run delete it
        var hold = await ctx.RetentionHolds.SingleAsync(h => h.EndedAt == null);
        hold.EndedAt = DateTime.UtcNow;
        hold.EndedBy = "admin";
        await ctx.SaveChangesAsync();

        Assert.True(await RetentionDeletionService.RunAsync(ctx, NullLogger.Instance, 500) >= 1);
        Assert.False(await ctx.ArchivedEmails.AnyAsync(e => e.Id == expired.Id));

        await scope.RollbackAsync();
    }

    [Fact]
    public async Task LocalRetention_DeletesNothingWhilePaused()
    {
        await using var scope = await _fixture.CreateTransactionalContextAsync();
        var ctx = scope.Context;
        var account = await SeedAccountAsync(ctx, localRetentionDays: 1);
        var old = await SeedEmailAsync(ctx, account, DateTime.UtcNow.AddDays(-10));
        ctx.RetentionHolds.Add(new RetentionHold { StartedBy = "admin", Reason = "tax audit" });
        await ctx.SaveChangesAsync();

        var svc = ServiceFactory.CreateEmailCoreService(ctx);
        Assert.Equal(0, await svc.DeleteOldLocalEmailsAsync(account));
        Assert.True(await ctx.ArchivedEmails.AnyAsync(e => e.Id == old.Id));

        await scope.RollbackAsync();
    }
}
