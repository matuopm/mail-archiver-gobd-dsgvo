using System.Security.Cryptography;
using System.Text;
using MailArchiver.Data;
using MailArchiver.Models;
using MailArchiver.Services.Providers.Eml;
using MailArchiver.Services.Providers.MBox;
using MailArchiver.Services.Shared;
using MailArchiver.Tests.Infrastructure;
using MailArchiver.Utilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MimeKit;
using Xunit;

namespace MailArchiver.Tests.Services;

/// <summary>
/// EML and MBOX imports keep the message bytes as they stand in the imported file write-once
/// in archive_worm, with SHA-256, origin and the retention period, like IMAP originals.
/// </summary>
[Collection(TestDbFixture.CollectionName)]
public class ImportOriginalCaptureTests
{
    private readonly TestDbFixture _fixture;
    public ImportOriginalCaptureTests(TestDbFixture fixture) => _fixture = fixture;

    private static string Sha256Hex(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static string Eml(string subject, string body = "hello") =>
        $"From: a@x.com\r\nTo: b@x.com\r\nSubject: {subject}\r\nDate: Mon, 01 Jan 2024 10:00:00 +0000\r\n" +
        $"Message-Id: <{Guid.NewGuid():N}@test>\r\nContent-Type: text/plain\r\n\r\n{body}\r\n";

    [Fact]
    public async Task MBox_HandsEachMessagesExactBytesToTheHandler()
    {
        var first = Eml("One", "line\r\n>From quoted in the body");
        var second = Eml("Two");
        var path = Path.Combine(Path.GetTempPath(), $"mbox-orig-{Guid.NewGuid():N}.mbox");
        File.WriteAllText(path,
            "From sender@x.com Mon Jan  1 10:00:00 2024\r\n" + first + "\r\n" +
            "From sender@x.com Mon Jan  1 10:00:01 2024\r\n" + second);
        try
        {
            var job = new MBoxImportJob { FilePath = path, TargetFolder = "INBOX" };
            var originals = new List<string>();
            await new MBoxStreamProcessor(NullLogger<MBoxStreamProcessor>.Instance).ProcessMBoxFile(
                job, new MailAccount { Id = 1, Name = "t", EmailAddress = "t@x.com" }, CancellationToken.None,
                (msg, folder, raw) =>
                {
                    originals.Add(Encoding.ASCII.GetString(raw!));
                    return Task.FromResult(ImportResult.CreateSuccess());
                });

            // Without the "From " separator line and the blank line before the next message
            Assert.Equal(new[] { first, second }, originals);
        }
        finally { File.Delete(path); }
    }

    private static MailImporter CreateImporter(MailArchiverDbContext ctx)
    {
        var services = new ServiceCollection();
        services.AddSingleton(ctx);
        services.AddSingleton(new DateTimeHelper(Options.Create(new TimeZoneOptions())));
        return new MailImporter(services.BuildServiceProvider(), NullLogger<MailImporter>.Instance,
            new EmlAttachmentCollector(NullLogger<EmlAttachmentCollector>.Instance));
    }

    private static async Task<MailAccount> SeedAccountAsync(MailArchiverDbContext ctx)
    {
        var account = new MailAccount
        {
            Name = $"imp-{Guid.NewGuid():N}".Substring(0, 25),
            EmailAddress = $"{Guid.NewGuid():N}@test.local",
            Provider = ProviderType.IMPORT,
            IsEnabled = true,
            LastSync = DateTime.UtcNow
        };
        ctx.MailAccounts.Add(account);
        await ctx.SaveChangesAsync();
        return account;
    }

    [Theory]
    [InlineData(ArchivedEmailSourceKinds.EmlImport)]
    [InlineData(ArchivedEmailSourceKinds.MboxImport)]
    public async Task Import_StoresTheOriginalWriteOnceWithOriginAndRetention(string kind)
    {
        await using var scope = await _fixture.CreateTransactionalContextAsync();
        var ctx = scope.Context;
        var account = await SeedAccountAsync(ctx);
        var raw = Encoding.ASCII.GetBytes(Eml("Imported " + kind));
        using var message = MimeMessage.Load(new MemoryStream(raw));

        var result = await CreateImporter(ctx).ImportEmailToDatabase(message, account, "job", "INBOX", raw, kind);

        Assert.True(result.Success, result.Error);
        var email = await ctx.ArchivedEmails.SingleAsync(e => e.MailAccountId == account.Id);
        // Re-read: the database trigger sets RetainUntil, CapturedAt and Size
        var source = await ctx.ArchivedEmailSources.AsNoTracking().SingleAsync(s => s.ArchivedEmailId == email.Id);
        Assert.Equal(kind, source.Source);
        Assert.Equal(raw, source.RawMime);
        Assert.Equal(Sha256Hex(raw), source.Sha256);
        Assert.Equal(Sha256Hex(raw), email.ContentHash);
        // 8 years from the end of the archiving year
        Assert.Equal(new DateTime(DateTime.UtcNow.Year + 9, 1, 1), source.RetainUntil.ToUniversalTime().Date, TimeSpan.FromDays(1));

        await scope.RollbackAsync();
    }

    [Fact]
    public async Task Import_WithoutOriginal_StoresNoSource()
    {
        await using var scope = await _fixture.CreateTransactionalContextAsync();
        var ctx = scope.Context;
        var account = await SeedAccountAsync(ctx);
        using var message = MimeMessage.Load(new MemoryStream(Encoding.ASCII.GetBytes(Eml("No original"))));

        var result = await CreateImporter(ctx).ImportEmailToDatabase(message, account, "job", "INBOX");

        Assert.True(result.Success, result.Error);
        var email = await ctx.ArchivedEmails.SingleAsync(e => e.MailAccountId == account.Id);
        Assert.False(await ctx.ArchivedEmailSources.AnyAsync(s => s.ArchivedEmailId == email.Id));

        await scope.RollbackAsync();
    }

    private static MimeMessage Invoice(string? messageId, string date, string body)
    {
        var header = messageId == null ? string.Empty : $"Message-Id: <{messageId}>\r\n";
        return MimeMessage.Load(new MemoryStream(Encoding.ASCII.GetBytes(
            $"From: shop@x.com\r\nTo: kunde@x.com\r\nSubject: Ihre Rechnung\r\nDate: {date}\r\n{header}" +
            $"Content-Type: text/plain\r\n\r\n{body}\r\n")));
    }

    [Fact]
    public async Task Import_SameFieldsButOwnMessageIds_KeepsBothEmails()
    {
        // An invoice run: sender, recipient and subject match one second apart; only the
        // Message-IDs and contents differ. Both are separate documents and must be archived.
        await using var scope = await _fixture.CreateTransactionalContextAsync();
        var ctx = scope.Context;
        var account = await SeedAccountAsync(ctx);
        var importer = CreateImporter(ctx);

        var first = await importer.ImportEmailToDatabase(Invoice($"{Guid.NewGuid():N}@x.com", "Mon, 01 Jan 2024 10:00:00 +0000", "Rechnung 1001"), account, "job", "INBOX");
        var second = await importer.ImportEmailToDatabase(Invoice($"{Guid.NewGuid():N}@x.com", "Mon, 01 Jan 2024 10:00:01 +0000", "Rechnung 1002"), account, "job", "INBOX");

        Assert.True(first.Success, first.Error);
        Assert.True(second.Success, second.Error);
        Assert.False(second.AlreadyExists);
        Assert.Equal(2, await ctx.ArchivedEmails.CountAsync(e => e.MailAccountId == account.Id));

        await scope.RollbackAsync();
    }

    [Fact]
    public async Task Import_SameMessageId_IsSkippedAsDuplicate()
    {
        await using var scope = await _fixture.CreateTransactionalContextAsync();
        var ctx = scope.Context;
        var account = await SeedAccountAsync(ctx);
        var importer = CreateImporter(ctx);
        var id = $"{Guid.NewGuid():N}@x.com";

        await importer.ImportEmailToDatabase(Invoice(id, "Mon, 01 Jan 2024 10:00:00 +0000", "Rechnung 1001"), account, "job", "INBOX");
        var again = await importer.ImportEmailToDatabase(Invoice(id, "Mon, 01 Jan 2024 10:00:00 +0000", "Rechnung 1001"), account, "job", "INBOX");

        Assert.True(again.AlreadyExists);
        Assert.Equal(1, await ctx.ArchivedEmails.CountAsync(e => e.MailAccountId == account.Id));

        await scope.RollbackAsync();
    }

    [Fact]
    public async Task Import_WithoutMessageId_StillMatchesByFields()
    {
        await using var scope = await _fixture.CreateTransactionalContextAsync();
        var ctx = scope.Context;
        var account = await SeedAccountAsync(ctx);
        var importer = CreateImporter(ctx);

        await importer.ImportEmailToDatabase(Invoice(null, "Mon, 01 Jan 2024 10:00:00 +0000", "Rechnung"), account, "job", "INBOX");
        var nearCopy = await importer.ImportEmailToDatabase(Invoice(null, "Mon, 01 Jan 2024 10:00:01 +0000", "Rechnung"), account, "job", "INBOX");

        Assert.True(nearCopy.AlreadyExists);
        Assert.Equal(1, await ctx.ArchivedEmails.CountAsync(e => e.MailAccountId == account.Id));

        await scope.RollbackAsync();
    }

    private static (MimeMessage Message, byte[] Raw) Scan(string body)
    {
        // Scan-to-mail device: no Message-ID, same subject, same second
        var raw = Encoding.ASCII.GetBytes(
            "From: scanner@x.com\r\nTo: buero@x.com\r\nSubject: Scan\r\nDate: Mon, 01 Jan 2024 10:00:00 +0000\r\n" +
            $"Content-Type: text/plain\r\n\r\n{body}\r\n");
        return (MimeMessage.Load(new MemoryStream(raw)), raw);
    }

    [Fact]
    public async Task Import_WithoutMessageId_DifferentOriginals_KeepsBoth()
    {
        await using var scope = await _fixture.CreateTransactionalContextAsync();
        var ctx = scope.Context;
        var account = await SeedAccountAsync(ctx);
        var importer = CreateImporter(ctx);
        var (first, firstRaw) = Scan("Seite 1");
        var (second, secondRaw) = Scan("Seite 2");

        var a = await importer.ImportEmailToDatabase(first, account, "job", "INBOX", firstRaw, ArchivedEmailSourceKinds.EmlImport);
        var b = await importer.ImportEmailToDatabase(second, account, "job", "INBOX", secondRaw, ArchivedEmailSourceKinds.EmlImport);

        Assert.True(a.Success, a.Error);
        Assert.True(b.Success, b.Error);
        Assert.False(b.AlreadyExists);
        Assert.Equal(2, await ctx.ArchivedEmails.CountAsync(e => e.MailAccountId == account.Id));

        await scope.RollbackAsync();
    }

    [Fact]
    public async Task Import_WithoutMessageId_IdenticalOriginal_IsSkippedAsDuplicate()
    {
        await using var scope = await _fixture.CreateTransactionalContextAsync();
        var ctx = scope.Context;
        var account = await SeedAccountAsync(ctx);
        var importer = CreateImporter(ctx);
        var (first, raw) = Scan("Seite 1");
        var (again, rawAgain) = Scan("Seite 1");

        await importer.ImportEmailToDatabase(first, account, "job", "INBOX", raw, ArchivedEmailSourceKinds.EmlImport);
        var result = await importer.ImportEmailToDatabase(again, account, "job", "INBOX", rawAgain, ArchivedEmailSourceKinds.EmlImport);

        Assert.True(result.AlreadyExists);
        Assert.Equal(1, await ctx.ArchivedEmails.CountAsync(e => e.MailAccountId == account.Id));

        await scope.RollbackAsync();
    }
}
