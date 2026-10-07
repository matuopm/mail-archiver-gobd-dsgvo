# Ist-Stand Mail Archiver

Zurück: [Index](README.md) · Weiter: [Architekturentscheidungen](architekturentscheidungen.md)

Geprüft am Fork, Branch `main`, Stand 05.10.2026 (Commit `a9b548e`). Stack: .NET 10, EF Core mit Npgsql, MailKit, PostgreSQL 14 (docker-compose), Schema `mail_archiver`.

## Vorhanden

### Sperre per Datenbank-Trigger
- Schalter `DeletionPolicy__DeletionAllowed=false`: beim Start werden alle Mails auf `IsLocked = true` gesetzt, der Spaltenstandard wird angepasst, der Zustand im Protokoll vermerkt (`Program.cs`, `Services/DeletionPolicyApplicationService.cs`).
- Trigger `prevent_locked_email_updates` und `prevent_locked_email_deletion` auf `ArchivedEmails` blockieren jede Änderung und jedes Löschen gesperrter Zeilen. Ausgenommen sind nur die Spalten `IsLocked` und `FolderName` (`Migrations/20260710120000_MigrateV2607_2_3.cs`).
- Manuelles Löschen (einzeln, Masse, Löschjob) wird zusätzlich in der Anwendung abgewiesen.

### Zugriffsprotokoll (AccessLog)
Tabelle `AccessLogs` mit Benutzer, Typ, Zeitpunkt, Mail- und Kontobezug. Typen u. a. Login, Search, Open, Download, Restore, Deletion, DeletionPolicy, Retention, AuditExport (`Models/AccessLog.cs`).

### Z3-Export
ZIP mit `INDEX.XML`, `index.dtd`, `emails.csv` und optional `attachments.csv` (mit SHA-256 je Anhang). Nur für Admins, Zeitraum bis 10 Jahre, jeder Lauf wird protokolliert (`Services/AuditExportService.cs`, `doc/AuditExport.md`).

### Sonstiges
- Aufbewahrung je Konto in Tagen (`Local Retention Days`), läuft im Sync.
- Rohheader (`RawHeaders`) und Anhänge mit inhaltsadressierter Ablage.

## Fehlt

| Lücke | Befund | Folge | Lösung |
|---|---|---|---|
| `ContentHash` nie befüllt | Feld und Index existieren (`Models/ArchivedEmail.cs`), kein Code schreibt hinein | Integrität nicht nachweisbar | Schritt 1 |
| Keine Original-EML | Mail wird in Felder zerlegt (Body, HtmlBody, Header, Anhänge); die `.eml` wird beim Export neu zusammengesetzt | Originalformat nicht erfüllt | Schritt 1 |
| Kontolöschung umgeht Sperre | `MailAccountsController.cs` und `MailAccountDeletionService.cs` setzen `IsLocked = false` und löschen, ohne `DeletionAllowed` zu prüfen | Admin kann ein ganzes Postfach trotz Sperre löschen | Schritt 2 |
| Sperre selbst aufhebbar | `IsLocked` ist vom Trigger ausgenommen; die App-Rolle darf die Spalte ändern | Wer DB-Zugriff mit App-Rechten hat, entsperrt und löscht | Schritt 2 |
| Keine Fristen je Dokumentart | Nur Tage je Konto ab `SentDate`; kein Jahresende, keine 6/8/10 Jahre, keine Löschsperre | Zu frühes oder zu spätes Löschen | Schritt 4 |
| Protokoll ungeschützt | Kein Trigger, keine Verkettung auf `AccessLogs`; Einträge lassen sich ändern und löschen | Protokoll nicht beweiskräftig | Schritt 3 |
| Keine Prüfer-Rolle | Z1 nur über ein Admin-Konto möglich | Prüfer bekäme zu viele Rechte | Schritt 5 |

Die Schritte verweisen auf die [Roadmap](roadmap.md), die Lösungen sind in [Architekturentscheidungen](architekturentscheidungen.md) begründet. Maßstab sind [GoBD-Anforderungen](gobd-anforderungen.md) und [DSGVO-Anforderungen](dsgvo-anforderungen.md).
