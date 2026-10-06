using System.Globalization;
using MailArchiver.Services.Shared;
using Microsoft.Extensions.Localization;
using Xunit;

namespace MailArchiver.Tests.Shared;

public class LogTextTests
{
    /// <summary>Minimal localizer: German or English text for one key, like the resource files.</summary>
    private sealed class FakeLocalizer(string culture) : IStringLocalizer
    {
        public LocalizedString this[string name] => this[name, Array.Empty<object>()];

        public LocalizedString this[string name, params object[] arguments] => name switch
        {
            "Log_RetentionPaused" => new(name, string.Format(
                culture == "de" ? "Automatische Löschung angehalten: {0}" : "Automatic deletion paused: {0}", arguments)),
            _ => new(name, name, resourceNotFound: true)
        };

        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => [];
    }

    [Fact]
    public void Event_IsShownInTheChosenLanguage()
    {
        var stored = LogText.Event("RetentionPaused", "Betriebsprüfung 2026");

        Assert.Equal("Automatische Löschung angehalten: Betriebsprüfung 2026", LogText.Display(stored, new FakeLocalizer("de")));
        Assert.Equal("Automatic deletion paused: Betriebsprüfung 2026", LogText.Display(stored, new FakeLocalizer("en")));
    }

    [Fact]
    public void PlainTextAndUnknownKeys_AreShownReadably()
    {
        Assert.Equal("Retention (Local): Deleted 3 emails", LogText.Display("Retention (Local): Deleted 3 emails", new FakeLocalizer("de")));
        Assert.Null(LogText.Display(null, new FakeLocalizer("de")));
        Assert.Equal("SomethingNew 5", LogText.Display(LogText.Event("SomethingNew", 5), new FakeLocalizer("de")));
    }

    [Fact]
    public void Values_CannotBreakTheFormat()
    {
        var stored = LogText.Event("RetentionPaused", "a\u001Fb");
        Assert.Equal("Automatic deletion paused: a b", LogText.Display(stored, new FakeLocalizer("en")));
    }
}
