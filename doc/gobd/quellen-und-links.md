# Links

Zurück: [Index](README.md)

## Repositories

- Fork: https://github.com/matuopm/mail-archiver-gobd-dsgvo
- Original: https://github.com/s1t5/mail-archiver
- Fünf PRs, am 06.10.2026 in den Hauptzweig übernommen (Commit `55189e8`, inhaltsgleich mit dem getesteten Stand `1a8efe0`): #1 Speicher, #2 Speichern beim Sync und Löschsperre, #3 Frist und Löschlauf, #4 Protokollschutz, #5 Prüfer-Rolle.
- Installationsvorlage für Kunden: im Repo unter `deploy/` (PR #7, Commit `163fa96`); Arbeitskopie auf dem Testserver unter `\\Testserver\coding\mail-archiver-gobd-vorlage`.
- Datei-Import mit Original: PR #6, Commit `ad5cc51`, im Hauptzweig.
- Testinstanz: Stack `mailarchiver-gobd-test` auf dem Testserver, Ordner `\\Testserver\coding\mail-archiver-gobd-test`, Ergebnisse in [Testprotokoll 2026-10-05](testprotokoll-2026-10.md).
- Andere Forks des Originals (88, Stand 05.10.2026): keiner mit GoBD-Bezug.

## Wichtige Stellen im Code

| Thema | Datei |
|---|---|
| Mail-Modell mit `ContentHash`, `IsLocked` | `Models/ArchivedEmail.cs` |
| Sperr-Trigger | `Migrations/20251105131500_MigrateV2511_1.cs`, `Migrations/20260710120000_MigrateV2607_2_3.cs` |
| Sperre beim Start anwenden | `Program.cs`, `Services/DeletionPolicyApplicationService.cs` |
| Kontolöschung (umgeht Sperre) | `Controllers/MailAccountsController.cs`, `Services/MailAccountDeletionService.cs` |
| Aufbewahrung je Konto | `Services/Core/EmailCoreService.cs`, `doc/RetentionPolicies.md` |
| Protokoll | `Models/AccessLog.cs`, `Services/AccessLogService.cs` |
| Z3-Export | `Services/AuditExportService.cs`, `doc/AuditExport.md` |
| Einstellungen | `doc/Setup.md`, `appsettings.json` |

Einordnung der Fundstellen: [Ist-Stand Mail Archiver](ist-stand-original.md).

## Rechtsquellen

Abgerufen und mit den Notizen abgeglichen am 07.10.2026.

| Quelle | Inhalt | Fundstelle |
|---|---|---|
| § 147 AO | Aufbewahrungspflicht, Fristen 10/8/6 Jahre (Abs. 3), Fristbeginn (Abs. 4), Ablaufhemmung (Abs. 3 Satz 5), Datenzugriff (Abs. 6) | https://www.gesetze-im-internet.de/ao_1977/__147.html |
| § 257 HGB | Aufbewahrung für Kaufleute, Fristen (Abs. 4), Fristbeginn (Abs. 5) | https://www.gesetze-im-internet.de/hgb/__257.html |
| § 13 GmbHG | GmbH gilt als Handelsgesellschaft (Abs. 3) | https://www.gesetze-im-internet.de/gmbhg/__13.html |
| § 87 BetrVG | Mitbestimmung bei technischen Überwachungseinrichtungen (Abs. 1 Nr. 6) | https://www.gesetze-im-internet.de/betrvg/__87.html |
| § 38 BDSG | Datenschutzbeauftragter ab 20 Personen | https://www.gesetze-im-internet.de/bdsg_2018/__38.html |
| DSGVO | Art. 5, 6, 13, 15, 17, 28, 30, 32 | amtlich: https://eur-lex.europa.eu/eli/reg/2016/679/oj; gelesen über https://dsgvo-gesetz.de |
| GoBD | BMF-Schreiben vom 28.11.2019, GZ IV A 4 - S 0316/19/10003 :001 | gelesen als PDF der bayerischen Steuerverwaltung: https://www.lfst.bayern.de/fileadmin/RESSOURCEN/INFORMATIONEN/Steuerinfos/Weitere_Themen/Aussenpruefung/2019-11-28-GoBD-1.pdf |
| GoBD, Änderungen | Schreiben vom 11.03.2024 und 14.07.2025 (u. a. E-Rechnung, Rz. 118, 119, 121 ergänzt) | nur als Zusammenfassung gelesen: https://kpmg.com/de/de/themen/2025/07/bmf-gobd-zweite-aenderung.html, https://umwelt-online.de/recht/allgemei/steuer/11032024.htm |

Grenzen der Prüfung: Der amtliche Volltext der GoBD in der Fassung von 2025 war nicht abrufbar (Seite des Ministeriums nicht erreichbar). Die Aussagen zu E-Mails stammen aus der Fassung von 2019; nach den gelesenen Zusammenfassungen betreffen die Änderungen E-Rechnungen und Zahlungsnachweise, nicht die Aussage zu E-Mails. Nicht geprüft: Rechtsprechung, Kommentarliteratur, das Verhältnis von privater Mailnutzung und Fernmeldegeheimnis.

Zusammenfassungen: [GoBD-Anforderungen](gobd-anforderungen.md), [DSGVO-Anforderungen](dsgvo-anforderungen.md).
