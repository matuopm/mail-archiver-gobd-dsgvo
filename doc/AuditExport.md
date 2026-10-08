# 📤 Audit Data Export

[← Back to Documentation Index](Index.md)

## 📋 Overview

The audit data export creates tabular mass data packages (ZIP with `INDEX.XML` + CSV tables + index DTD) from the existing archive. These packages follow the established index structure for data media handover and can be imported directly by common external audit and analysis tools that support this index format.

The feature is available as a **dedicated page reachable from the Logs page** and is restricted to admin users. Every export run is recorded in the access log, so the export history is revision-safe.

## 🧭 Access

1. Log in as an admin user
2. Open the **Logs** page
3. Click the **Audit Data Export** button in the top right corner

Non-admin users do not see the button and cannot call any of the export endpoints (the server rejects them).

## 📦 Export Package Structure

The generated ZIP file is named `audit-export-<timestamp>.zip` and contains:

| File | Content |
|---|---|
| `INDEX.XML` | Index file describing the data supplier, the media and all table/column definitions |
| `index.dtd` | The DTD the index file references; required by importing tools for validation |
| `emails.csv` | One row per archived email in the selected period/mailbox |
| `attachments.csv` | Optional; one row per attachment of the exported emails |
| `accesslog.csv` | Always; the access log entries of the selected period |

### Table `emails.csv`

Columns (no header row; the column names come from `INDEX.XML`, as usual for this format):

| Column | Description |
|---|---|
| Id | Internal email ID (numeric) |
| MessageId | Email Message-ID header |
| SentDate | Sent date, ISO 8601 UTC (`yyyy-MM-ddTHH:mm:ssZ`) |
| ReceivedDate | Received date, ISO 8601 UTC |
| IsOutgoing | `1` = outgoing, `0` = incoming |
| From | Sender |
| To | Recipients |
| Cc | CC recipients |
| Bcc | BCC recipients |
| Subject | Subject |
| FolderName | Folder the email is archived in |
| HasAttachments | `1` = with attachments, `0` = without |
| AttachmentCount | Number of attachments (numeric) |
| AccountEmail | Email address of the archiving mailbox |

### Table `attachments.csv`

| Column | Description |
|---|---|
| EmailId | Internal ID of the email the attachment belongs to (join key to `emails.csv`) |
| FileName | Attachment file name |
| ContentType | MIME type |
| Size | Size in bytes (numeric) |
| Sha256 | SHA-256 hash of the content (from the content-addressed attachment storage) |

### Table `accesslog.csv`

The access log entries whose time lies in the selected period, in chain order. Type and
event texts are always German, whatever the language of the user who starts the export.
With a mailbox selected, only entries of that mailbox are included (entries naming the
mailbox or an email of it); the chain then has gaps. Without a mailbox, the table holds
every entry of the period without gaps: each `PrevHash` equals the `Hash` of the row
before it.

Each `Hash` can be recomputed from the table alone, without access to the database:

```
Hash = lowercase hex of SHA-256( UTF-8 bytes of HashInput )
```

`HashInput` is the exact text the database hashed when it wrote the entry
(`archive_worm.access_log_hash`, see [doc/sql/MigrateV2610_3-audit-log.sql](sql/MigrateV2610_3-audit-log.sql)).
It joins these values with `|`:

```
ChainSeq|Id|'Username'|TypeCode|TimestampExact|'EmailId'|'EmailSubject'|'EmailFrom'|'RawDetails'|'MailAccountId'|PrevHash
```

Every value except `ChainSeq`, `Id`, `TypeCode`, `TimestampExact` and `PrevHash` is in single
quotes (a quote inside is doubled; text with a backslash is written as `E'…'` with the
backslash doubled, as PostgreSQL's `quote_nullable` does). A missing value is the word `NULL`
without quotes. `TimestampExact` is UTC with microseconds, `PrevHash` is empty for the first
entry of the log. An auditor checks a row by hashing `HashInput` and comparing the result
with `Hash`, by comparing the values in `HashInput` with the columns of the same row, and by
checking that `HashInput` ends with the `Hash` of the row before. **Check log** on the Logs
page ([Logs](Logs.md#checking-the-log)) checks the whole chain in the database.

| Column | Description |
|---|---|
| ChainSeq | Position in the hash chain (numeric) |
| Timestamp | Time of the access, ISO 8601 UTC |
| Username | User (or owner of the API key) |
| Type | Kind of access, German (e.g. "Öffnen", "Suche") |
| EmailId | Email the entry refers to, if any |
| EmailSubject | Subject of that email |
| EmailFrom | Sender of that email |
| Details | Search parameters or event text, German for entries written since the fork's log texts; older entries keep their English text |
| MailAccountId | Mailbox the entry refers to, if any |
| PrevHash | Hash of the previous entry in the chain |
| Hash | SHA-256 of this entry |
| Id | Internal ID of the entry (numeric) |
| TypeCode | Kind of access as stored (numeric) |
| TimestampExact | Time of the access as hashed, UTC with microseconds (`yyyy-MM-ddTHH:mm:ss.ffffff`) |
| RawDetails | `Details` as stored, before translation (`@log:` event key, values separated by the control character U+001F) |
| HashInput | The text the hash is computed over (see above) |

### CSV Conventions

- Encoding: UTF-8 **without** BOM (declared as `UTF8` in `INDEX.XML`)
- Column delimiter: `;`
- Text encapsulator: `"` (doubled for escaping)
- Record delimiter: `\n` (explicitly declared in `INDEX.XML`)
- Dates: ISO 8601, times converted from the configured display timezone to UTC
- No header row; no localized numbers (numeric columns contain plain integers)

## 🖥️ Using the Export Page

### Form Fields

1. **From / To date** - Period to export (required, maximum span configurable, default 10 years)
2. **Mailbox** - Restrict the export to a single mailbox or export all mailboxes
3. **Attachment metadata** - When checked, `attachments.csv` is added to the package
4. **Data supplier name / location / comment** - Written into the `DataSupplier` block of `INDEX.XML`; pre-filled from the `AuditExport` configuration section and editable per export

### Job Lifecycle

1. Clicking **Start Export** validates the period and mailbox, creates a job and returns to the page
2. The job runs in the background (queued exports are processed one after another); the job table shows live progress (processed/total emails with a progress bar) via automatic polling
3. When the export finishes, the **Download** button appears in the job row
4. Running or queued jobs can be **cancelled**

### Export History

The history table below the form lists the most recent exports with timestamp, user, period, mailbox, email count, status and file size. The history is **persisted in the database** (`AuditExportJobs` table), so it survives application restarts and is revision-safe.

Download files are kept for a configurable retention window (default 30 days, see `AuditExport__RetentionDays`); after that the ZIP files are deleted by the daily cleanup while the history rows remain.

> 📝 **Note**: Export files are stored inside the application container. If you need to keep them long-term, download them and store them in your audit evidence repository.

## 🔐 Auditing

Every export produces two access log entries of the new type **Audit Data Export**:

1. **Start entry** - when the export is queued (with job ID, period, mailbox, options as JSON)
2. **Result entry** - when the export completes, fails or is cancelled (with job ID, period, mailbox, email count, result and package size as JSON)

The entries appear in the Logs page like all other access log entries and can be filtered by the new type. The type filter dropdown picks them up automatically.

## ⚙️ Configuration

The `AuditExport` section in `appsettings.json` (environment variables use the double-underscore syntax, e.g. `AuditExport__RetentionDays`):

| Setting | Description | Default |
|---|---|---|
| `DataSupplierName` | Default value for the data supplier name form field | empty |
| `DataSupplierLocation` | Default value for the data supplier location form field | empty |
| `Comment` | Default value for the comment form field | empty |
| `OutputDirectory` | Directory for the generated ZIP files (relative paths resolve against the app content root) | `exports/audit` |
| `RetentionDays` | Days until completed export files are deleted | `30` |
| `MaxRangeYears` | Maximum allowed span between period start and end date | `10` |

## 🗄️ Database

The export history is stored in the `mail_archiver."AuditExportJobs"` table (created by migration `MigrateV2609_1`, applied automatically on startup). No existing tables are modified by this feature.

## 🔍 Troubleshooting

| Problem | Cause / Solution |
|---|---|
| Download button missing after restart | Export files live in the container; if the container was recreated or the retention window passed, the ZIP is gone while the history row remains. Re-run the export. |
| "The period must not exceed X years" | Reduce the period or raise `AuditExport__MaxRangeYears`. |
| Job stays in *Running* for a long time | Large archives are streamed in batches of 1000 emails; progress is updated continuously. Very large periods can take a while — check the processed/total counter. |
| Import tool reports encoding problems | The package is UTF-8 without BOM with declared `UTF8` flag and explicit record delimiter; make sure the tool reads the `INDEX.XML` next to the data files (extract the whole ZIP, do not open files individually from within the ZIP). |

## 🔗 Related Documentation

- [Access Logging](Logs.md)
- [Installation, Setup and Parameters](Setup.md)