using System.Security.Cryptography;
using System.Text;
using MailArchiver.Models;
using MailArchiver.Services.Shared;
using MailArchiver.Tests.Infrastructure;
using MailArchiver.ViewModels;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace MailArchiver.Tests.Services;

/// <summary>
/// Exports hand out the stored original byte for byte instead of a message rebuilt from the
/// database fields. Every test rolls back, because retained originals cannot be deleted.
/// </summary>
[Collection(TestDbFixture.CollectionName)]
public class OriginalMessageExportTests
{
    private readonly TestDbFixture _fixture;
    public OriginalMessageExportTests(TestDbFixture fixture) => _fixture = fixture;

    // Latin-1 umlaut, CRLF line ends and a header the database does not keep
    private static readonly byte[] Original = Encoding.Latin1.GetBytes(
        "X-Custom: kept\r\nSubject: Original\r\nContent-Type: text/plain; charset=iso-8859-1\r\n" +
        "Content-Transfer-Encoding: 8bit\r\n\r\nGrüße\r\nFrom the start of a line\r\n");

    private static string Sha256Hex(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static async Task<ArchivedEmail> SeedEmailAsync(MailArchiver.Data.MailArchiverDbContext ctx, bool withOriginal)
    {
        var account = new MailAccount
        {
            Name = $"orig-{Guid.NewGuid():N}".Substring(0, 25),
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
            Subject = "Rebuilt",
            From = "a@x.com", To = "b@x.com", Cc = string.Empty, Bcc = string.Empty,
            Body = "Body", HtmlBody = string.Empty,
            SentDate = DateTime.UtcNow, ReceivedDate = DateTime.UtcNow,
            FolderName = "INBOX"
        };
        ctx.ArchivedEmails.Add(email);
        await ctx.SaveChangesAsync();

        if (withOriginal)
        {
            await ctx.Database.ExecuteSqlRawAsync(
                "INSERT INTO archive_worm.\"ArchivedEmailSources\" (\"ArchivedEmailId\", \"RawMime\", \"Size\", \"Sha256\", \"Source\") " +
                "VALUES ({0}, {1}, {2}, {3}, 'imap')", email.Id, Original, (long)Original.Length, Sha256Hex(Original));
        }
        return email;
    }

    [Fact]
    public async Task SingleExport_ReturnsStoredOriginal()
    {
        await using var scope = await _fixture.CreateTransactionalContextAsync();
        var email = await SeedEmailAsync(scope.Context, withOriginal: true);

        var svc = ServiceFactory.CreateEmailCoreService(scope.Context);
        var bytes = await svc.ExportEmailsAsync(new ExportViewModel { EmailId = email.Id });

        Assert.Equal(Original, bytes);
        await scope.RollbackAsync();
    }

    [Fact]
    public async Task SingleExport_WithoutOriginal_FallsBackToRebuiltMessage()
    {
        await using var scope = await _fixture.CreateTransactionalContextAsync();
        var email = await SeedEmailAsync(scope.Context, withOriginal: false);

        var svc = ServiceFactory.CreateEmailCoreService(scope.Context);
        var bytes = await svc.ExportEmailsAsync(new ExportViewModel { EmailId = email.Id });

        Assert.Contains("Subject: Rebuilt", Encoding.UTF8.GetString(bytes));
        await scope.RollbackAsync();
    }

    [Fact]
    public async Task GetBytes_PrefersOriginal_AndRebuildsOnlyWithout()
    {
        await using var scope = await _fixture.CreateTransactionalContextAsync();
        var withOriginal = await SeedEmailAsync(scope.Context, withOriginal: true);
        var without = await SeedEmailAsync(scope.Context, withOriginal: false);
        var rebuilt = 0;
        Task<MimeKit.MimeMessage> Rebuild()
        {
            rebuilt++;
            return Task.FromResult(new MimeKit.MimeMessage { Subject = "Rebuilt" });
        }

        Assert.Equal(Original, await OriginalMessage.GetBytesAsync(scope.Context, withOriginal.Id, Rebuild));
        Assert.Equal(0, rebuilt);
        Assert.Contains("Subject: Rebuilt", Encoding.UTF8.GetString(await OriginalMessage.GetBytesAsync(scope.Context, without.Id, Rebuild)));
        Assert.Equal(1, rebuilt);
        await scope.RollbackAsync();
    }

    [Fact]
    public async Task MboxBody_IsByteExact_EscapesFromLines_AndEndsWithNewline()
    {
        using var ms = new MemoryStream();
        await OriginalMessage.WriteMboxBodyAsync(ms, Original);

        var expected = Encoding.Latin1.GetBytes(Encoding.Latin1.GetString(Original)
            .Replace("\r\nFrom the start", "\r\n>From the start"));
        Assert.Equal(expected, ms.ToArray());

        using var noNewline = new MemoryStream();
        await OriginalMessage.WriteMboxBodyAsync(noNewline, Encoding.ASCII.GetBytes("From x\nlast"));
        Assert.Equal(">From x\nlast\n", Encoding.ASCII.GetString(noNewline.ToArray()));
    }
}
