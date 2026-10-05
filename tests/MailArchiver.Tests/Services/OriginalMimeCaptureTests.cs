using System.Security.Cryptography;
using System.Text;
using MailArchiver.Data;
using MailArchiver.Models;
using MailArchiver.Services.Shared;
using MailArchiver.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using MimeKit;
using Xunit;

namespace MailArchiver.Tests.Services;

/// <summary>
/// Storing the original message during archiving (Compliance:StoreOriginalMime) and how the
/// deletion paths treat emails whose original is still retained in archive_worm.
/// Every test runs in a transaction that is rolled back, because retained originals cannot be
/// deleted by the usual cleanup.
/// </summary>
[Collection(TestDbFixture.CollectionName)]
public class OriginalMimeCaptureTests
{
    private readonly TestDbFixture _fixture;
    public OriginalMimeCaptureTests(TestDbFixture fixture) => _fixture = fixture;

    private static string Sha256Hex(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static async Task<MailAccount> SeedAccountAsync(MailArchiverDbContext ctx, int? localRetentionDays = null)
    {
        var account = new MailAccount
        {
            Name = $"mime-{Guid.NewGuid():N}".Substring(0, 25),
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

    private static async Task<ArchivedEmail> SeedEmailAsync(MailArchiverDbContext ctx, MailAccount account, DateTime sentDate)
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
            SentDate = sentDate,
            ReceivedDate = DateTime.UtcNow,
            IsOutgoing = false,
            HasAttachments = false,
            FolderName = "INBOX",
            IsLocked = false
        };
        ctx.ArchivedEmails.Add(email);
        await ctx.SaveChangesAsync();
        await ctx.Database.ExecuteSqlRawAsync(
            "UPDATE mail_archiver.\"ArchivedEmails\" SET \"IsLocked\" = false WHERE \"Id\" = {0}", email.Id);
        return email;
    }

    private static Task AddRetainedSourceAsync(MailArchiverDbContext ctx, int emailId)
    {
        var raw = Encoding.ASCII.GetBytes($"Subject: {emailId}\r\n\r\nx\r\n");
        return ctx.Database.ExecuteSqlRawAsync(
            "INSERT INTO archive_worm.\"ArchivedEmailSources\" (\"ArchivedEmailId\", \"RawMime\", \"Size\", \"Sha256\", \"Source\") " +
            "VALUES ({0}, {1}, 0, {2}, 'imap')", emailId, raw, Sha256Hex(raw));
    }

    private static (MimeMessage Message, byte[] Raw) BuildMessage()
    {
        // Folded header and LF-only line ends: re-serializing a parsed message would not
        // reproduce these bytes, so equality proves the original is what got stored.
        var raw = Encoding.ASCII.GetBytes(
            "From: a@x.com\nTo: b@x.com\nSubject: original\n bytes\n" +
            $"Message-ID: <{Guid.NewGuid():N}@test.local>\nDate: Mon, 5 Oct 2026 10:00:00 +0200\n\nBody\n");
        using var stream = new MemoryStream(raw);
        return (MimeMessage.Load(stream), raw);
    }

    [Fact]
    public async Task Archive_WithOriginalMime_StoresTheExactBytesAndTheirHash()
    {
        await using var scope = await _fixture.CreateTransactionalContextAsync();
        var ctx = scope.Context;
        var account = await SeedAccountAsync(ctx);
        var (message, raw) = BuildMessage();

        var svc = ServiceFactory.CreateEmailCoreService(ctx);
        Assert.True(await svc.ArchiveEmailAsync(account, message, false, "INBOX", raw));
        ctx.ChangeTracker.Clear();

        var email = await ctx.ArchivedEmails.SingleAsync(e => e.MailAccountId == account.Id);
        var source = await ctx.ArchivedEmailSources.SingleAsync(s => s.ArchivedEmailId == email.Id);
        Assert.Equal(raw, source.RawMime);
        Assert.Equal(Sha256Hex(raw), source.Sha256);
        Assert.Equal(ArchivedEmailSourceKinds.Imap, source.Source);
        Assert.Equal(source.Sha256, email.ContentHash);
        Assert.NotNull(email.HashCreatedAt);

        await scope.RollbackAsync();
    }

    [Fact]
    public async Task Archive_WithoutOriginalMime_StoresNoSource()
    {
        await using var scope = await _fixture.CreateTransactionalContextAsync();
        var ctx = scope.Context;
        var account = await SeedAccountAsync(ctx);
        var (message, _) = BuildMessage();

        var svc = ServiceFactory.CreateEmailCoreService(ctx);
        Assert.True(await svc.ArchiveEmailAsync(account, message, false, "INBOX"));

        var email = await ctx.ArchivedEmails.SingleAsync(e => e.MailAccountId == account.Id);
        Assert.False(await ctx.ArchivedEmailSources.AnyAsync(s => s.ArchivedEmailId == email.Id));
        Assert.Null(email.ContentHash);

        await scope.RollbackAsync();
    }

    [Fact]
    public async Task LocalRetention_KeepsEmailsWithRetainedOriginal_AndDeletesTheRest()
    {
        await using var scope = await _fixture.CreateTransactionalContextAsync();
        var ctx = scope.Context;
        var account = await SeedAccountAsync(ctx, localRetentionDays: 1);
        var retained = await SeedEmailAsync(ctx, account, DateTime.UtcNow.AddDays(-10));
        var plain = await SeedEmailAsync(ctx, account, DateTime.UtcNow.AddDays(-10));
        await AddRetainedSourceAsync(ctx, retained.Id);

        var svc = ServiceFactory.CreateEmailCoreService(ctx);
        var deleted = await svc.DeleteOldLocalEmailsAsync(account);

        Assert.Equal(1, deleted);
        Assert.True(await ctx.ArchivedEmails.AnyAsync(e => e.Id == retained.Id));
        Assert.False(await ctx.ArchivedEmails.AnyAsync(e => e.Id == plain.Id));

        await scope.RollbackAsync();
    }

    [Fact]
    public async Task RetainedSources_FindsRetainedEmailsAndAccounts()
    {
        await using var scope = await _fixture.CreateTransactionalContextAsync();
        var ctx = scope.Context;
        var withSource = await SeedAccountAsync(ctx);
        var withoutSource = await SeedAccountAsync(ctx);
        var retained = await SeedEmailAsync(ctx, withSource, DateTime.UtcNow);
        var plain = await SeedEmailAsync(ctx, withSource, DateTime.UtcNow);
        await SeedEmailAsync(ctx, withoutSource, DateTime.UtcNow);
        await AddRetainedSourceAsync(ctx, retained.Id);

        Assert.True(await RetainedSources.AccountHasRetainedAsync(ctx, withSource.Id));
        Assert.False(await RetainedSources.AccountHasRetainedAsync(ctx, withoutSource.Id));
        Assert.Equal(new[] { retained.Id },
            await RetainedSources.FilterRetainedAsync(ctx, new List<int> { retained.Id, plain.Id }));

        await scope.RollbackAsync();
    }
}
