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
}
