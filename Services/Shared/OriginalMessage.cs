using MailArchiver.Data;
using Microsoft.EntityFrameworkCore;
using MimeKit;

namespace MailArchiver.Services.Shared
{
    /// <summary>
    /// Hands out an email as it was received: the byte-exact original from
    /// archive_worm."ArchivedEmailSources". Only emails without a stored original (archived
    /// before originals were kept) fall back to a message rebuilt from the database fields.
    /// </summary>
    public static class OriginalMessage
    {
        /// <summary>The stored original, or null when the email has none.</summary>
        public static Task<byte[]?> LoadAsync(MailArchiverDbContext context, int emailId,
            CancellationToken cancellationToken = default) =>
            context.ArchivedEmailSources
                .AsNoTracking()
                .Where(s => s.ArchivedEmailId == emailId)
                .Select(s => s.RawMime)
                .FirstOrDefaultAsync(cancellationToken)!;

        /// <summary>The original if stored, otherwise the message built by <paramref name="rebuild"/>.</summary>
        public static async Task<byte[]> GetBytesAsync(MailArchiverDbContext context, int emailId,
            Func<Task<MimeMessage>> rebuild, CancellationToken cancellationToken = default)
        {
            var original = await LoadAsync(context, emailId, cancellationToken);
            if (original != null)
                return original;

            using var ms = new MemoryStream();
            await (await rebuild()).WriteToAsync(ms, cancellationToken);
            return ms.ToArray();
        }

        /// <summary>
        /// Writes a message into an mbox file byte for byte, so non-UTF-8 content stays intact.
        /// Lines starting with "From " get a leading '>' as the mbox format requires, and the
        /// message is terminated with a line break. The caller writes the "From " separator
        /// line before and the blank line after.
        /// </summary>
        public static async Task WriteMboxBodyAsync(Stream target, byte[] message, CancellationToken cancellationToken = default)
        {
            var lineStart = 0;
            while (lineStart < message.Length)
            {
                var newline = Array.IndexOf(message, (byte)'\n', lineStart);
                var lineEnd = newline < 0 ? message.Length : newline + 1;

                if (StartsWithFrom(message, lineStart))
                    target.WriteByte((byte)'>');
                await target.WriteAsync(message.AsMemory(lineStart, lineEnd - lineStart), cancellationToken);

                lineStart = lineEnd;
            }

            if (message.Length == 0 || message[^1] != (byte)'\n')
                target.WriteByte((byte)'\n');
        }

        private static bool StartsWithFrom(byte[] message, int offset) =>
            message.Length - offset >= 5
            && message[offset] == (byte)'F' && message[offset + 1] == (byte)'r' && message[offset + 2] == (byte)'o'
            && message[offset + 3] == (byte)'m' && message[offset + 4] == (byte)' ';
    }
}
