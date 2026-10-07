# Dokumentation zum GoBD-Fork

Projektdoku zum GoBD/DSGVO-Fork von **mail-archiver** (ASP.NET Core / .NET 10, PostgreSQL, MailKit).

**Ziel:** E-Mails einer GmbH so archivieren, dass eine Betriebsprüfung (GoBD) und der Datenschutz (DSGVO) gleichzeitig erfüllt sind: vollständig, unveränderbar, im Originalformat, mit Fristen und geordnetem Löschen.

## So liest man diese Dokumentation

Die Dateien sind normale Markdown-Texte und lassen sich direkt hier auf GitHub lesen. Bequemer geht es in [Obsidian](https://obsidian.md): den Ordner `doc/gobd` herunterladen und in Obsidian über „Ordner als Vault öffnen“ laden. Dann sind die Notizen untereinander verlinkt und durchsuchbar.

## Notizen

| Thema | Notiz |
|---|---|
| Was das Steuer- und Handelsrecht verlangt | [GoBD-Anforderungen](gobd-anforderungen.md) |
| Was der Datenschutz verlangt | [DSGVO-Anforderungen](dsgvo-anforderungen.md) |
| Was der Mail Archiver heute kann und was fehlt | [Ist-Stand Mail Archiver](ist-stand-original.md) |
| Wie die Lücken geschlossen werden | [Architekturentscheidungen](architekturentscheidungen.md) |
| Reihenfolge der Umsetzung | [Roadmap](roadmap.md) |
| Muster-Verfahrensdoku zum Ausfüllen | [Verfahrensdokumentation](verfahrensdokumentation-muster.md) |
| Testergebnisse | [Testprotokoll 2026-10-05](testprotokoll-2026-10.md) |
| Repositories und Quellen | [Links](quellen-und-links.md) |

## Stand in einem Satz

Im Fork gebaut und getestet ([Testprotokoll 2026-10-05](testprotokoll-2026-10.md)): Original-EML mit Hash in einem schreibgeschützten Speicher, feste Löschsperre ohne manuelles Löschen, einheitlich 8 Jahre Frist mit automatischem Löschlauf und Löschpause, verkettetes Zugriffsprotokoll, Prüfer-Rolle. Seit 06.10.2026 im Hauptzweig des Forks. Installationsvorlage als Neuinstallation getestet. Datei-Import mit Original getestet und seit 06.10.2026 im Hauptzweig (PR #6). Offen: Microsoft 365 ohne Original ([Roadmap](roadmap.md)).

## Spannungsfeld

GoBD sagt *aufbewahren und nicht löschen*, DSGVO sagt *löschen, sobald der Zweck entfällt*. Aufgelöst wird das über Fristen: während der Frist gesperrt ([Aufbewahrungsfristen](gobd-anforderungen.md#aufbewahrungsfristen)), nach Fristablauf automatisch gelöscht ([Löschkonzept](dsgvo-anforderungen.md#löschkonzept)).

> [!WARNING]
> **Keine Rechtsberatung**
> Die rechtlichen Notizen sind eine Arbeitsgrundlage. Paragrafen, Fristen und die zitierten Stellen der GoBD wurden am 07.10.2026 gegen die Quellen geprüft ([Rechtsquellen](quellen-und-links.md#rechtsquellen)); die Auslegung bleibt ungeprüft. Vor dem Produktiveinsatz mit Steuerberater und, falls vorhanden, Datenschutzbeauftragtem abstimmen.


---

Diese Dokumentation wurde mit KI-Unterstützung erstellt und kann Fehler enthalten. Sie ist nach bestem Wissen geschrieben; Paragrafen und Fristen wurden gegen die Gesetzestexte geprüft, die Auslegung nicht. Sie ist keine Rechtsberatung und keine Zusicherung der Konformität.
