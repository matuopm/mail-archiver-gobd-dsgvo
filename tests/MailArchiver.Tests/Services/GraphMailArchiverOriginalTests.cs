using System.Net;
using System.Security.Cryptography;
using System.Text;
using MailArchiver.Models;
using MailArchiver.Services.Providers.Graph;
using MailArchiver.Tests.Infrastructure;
using MailArchiver.Utilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using Microsoft.Kiota.Abstractions.Authentication;
using Xunit;

namespace MailArchiver.Tests.Services;

/// <summary>
/// Microsoft 365 emails are archived together with their MIME content from Graph
/// ($value) as a write-once original with SHA-256. Without that content the email
/// is not archived, so the next sync retries it.
/// </summary>
[Collection(TestDbFixture.CollectionName)]
public class GraphMailArchiverOriginalTests
{
    private readonly TestDbFixture _fixture;
    public GraphMailArchiverOriginalTests(TestDbFixture fixture) => _fixture = fixture;

    /// <summary>Answers $value with the given MIME (or an error) and the attachment list with nothing.</summary>
    private sealed class FakeGraphHandler(byte[]? mime) : HttpMessageHandler
    {
        public List<string> Paths { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = Uri.UnescapeDataString(request.RequestUri!.AbsolutePath);
            Paths.Add(path);
            if (path.EndsWith("/$value"))
            {
                if (mime == null)
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                    {
                        Content = new StringContent("{\"error\":{\"code\":\"ServiceUnavailable\",\"message\":\"down\"}}", Encoding.UTF8, "application/json")
                    });
                var content = new ByteArrayContent(mime);
                content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"value\":[]}", Encoding.UTF8, "application/json")
            });
        }
    }

    private static GraphServiceClient Client(FakeGraphHandler handler) =>
        new(new HttpClient(handler), new AnonymousAuthenticationProvider(), "https://graph.test/v1.0");

    private static GraphMailArchiver Archiver(Data.MailArchiverDbContext ctx) =>
        new(ctx, NullLogger<GraphMailArchiver>.Instance,
            new DateTimeHelper(Options.Create(new TimeZoneOptions { DisplayTimeZoneId = "Europe/Berlin" })));

    private static async Task<MailAccount> AccountAsync(Data.MailArchiverDbContext ctx)
    {
        var account = new MailAccount
        {
            Name = $"m365-{Guid.NewGuid():N}".Substring(0, 25),
            EmailAddress = $"{Guid.NewGuid():N}@test.local",
            Provider = ProviderType.M365,
            IsEnabled = false,
            LastSync = DateTime.UtcNow
        };
        ctx.MailAccounts.Add(account);
        await ctx.SaveChangesAsync();
        return account;
    }

    private static Message GraphMessage(string internetMessageId) => new()
    {
        Id = "AAMkGraphId",
        InternetMessageId = internetMessageId,
        Subject = "Rechnung",
        From = new Recipient { EmailAddress = new EmailAddress { Address = "kunde@example.com" } },
        ToRecipients = new List<Recipient> { new() { EmailAddress = new EmailAddress { Address = "info@example.com" } } },
        CcRecipients = new List<Recipient>(),
        BccRecipients = new List<Recipient>(),
        SentDateTime = new DateTimeOffset(2026, 10, 1, 9, 0, 0, TimeSpan.Zero),
        Body = new ItemBody { ContentType = BodyType.Text, Content = "Hallo" },
        HasAttachments = false
    };

    [Fact]
    public async Task ArchivedEmail_GetsTheGraphMimeAsWriteOnceOriginal()
    {
        await using var scope = await _fixture.CreateTransactionalContextAsync();
        var ctx = scope.Context;
        var account = await AccountAsync(ctx);
        var messageId = $"{Guid.NewGuid():N}@example.com";
        var mime = Encoding.ASCII.GetBytes(
            $"Message-ID: <{messageId}>\r\nFrom: kunde@example.com\r\nTo: info@example.com\r\nSubject: Rechnung\r\n\r\nHallo\r\n");
        var handler = new FakeGraphHandler(mime);

        var isNew = await Archiver(ctx).ArchiveGraphEmailAsync(Client(handler), account, GraphMessage($"<{messageId}>"), false, "INBOX");

        Assert.True(isNew);
        Assert.Contains(handler.Paths, p => p.EndsWith($"/users/{account.EmailAddress}/messages/AAMkGraphId/$value"));
        var email = await ctx.ArchivedEmails.AsNoTracking().SingleAsync(e => e.MailAccountId == account.Id);
        var source = await ctx.ArchivedEmailSources.AsNoTracking().SingleAsync(s => s.ArchivedEmailId == email.Id);
        var expectedHash = Convert.ToHexString(SHA256.HashData(mime)).ToLowerInvariant();
        Assert.Equal(mime, source.RawMime);
        Assert.Equal(expectedHash, source.Sha256);
        Assert.Equal(ArchivedEmailSourceKinds.Graph, source.Source);
        Assert.Equal(expectedHash, email.ContentHash);

        await scope.RollbackAsync();
    }

    [Fact]
    public async Task WithoutMimeContent_TheEmailIsNotArchived()
    {
        await using var scope = await _fixture.CreateTransactionalContextAsync();
        var ctx = scope.Context;
        var account = await AccountAsync(ctx);

        await Assert.ThrowsAnyAsync<Exception>(() => Archiver(ctx).ArchiveGraphEmailAsync(
            Client(new FakeGraphHandler(null)), account, GraphMessage($"<{Guid.NewGuid():N}@example.com>"), false, "INBOX"));

        Assert.False(await ctx.ArchivedEmails.AnyAsync(e => e.MailAccountId == account.Id));

        await scope.RollbackAsync();
    }
}
