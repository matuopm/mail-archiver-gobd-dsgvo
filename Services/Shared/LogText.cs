using Microsoft.Extensions.Localization;

namespace MailArchiver.Services.Shared
{
    /// <summary>
    /// Access log texts that are shown in the user's language. Instead of a finished sentence,
    /// <see cref="Event"/> stores an event key plus its values in AccessLog.SearchParameters;
    /// <see cref="Display"/> turns that into the sentence from the resource "Log_{key}" in the
    /// current UI language (languages without a translation fall back to English).
    /// Older entries are plain text and are shown unchanged.
    /// </summary>
    public static class LogText
    {
        private const string Prefix = "@log:";
        private const char Separator = '\u001F'; // ASCII unit separator, never part of normal text

        public static string Event(string key, params object[] values) =>
            Prefix + key + string.Concat(values.Select(v => Separator + Clean(v)));

        public static string? Display(string? stored, IStringLocalizer localizer)
        {
            if (stored == null || !stored.StartsWith(Prefix, StringComparison.Ordinal))
                return stored;

            var parts = stored.Substring(Prefix.Length).Split(Separator);
            var text = localizer["Log_" + parts[0], parts.Skip(1).Cast<object>().ToArray()];
            // Unknown key (e.g. an entry written by a newer version): show key and values
            return text.ResourceNotFound ? string.Join(" ", parts) : text.Value;
        }

        private static string Clean(object? value) =>
            (Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty)
                .Replace(Separator, ' ');
    }
}
