# 🔏 Write-Once Storage for Original Messages (archive_worm)

[← Back to Documentation Index](Index.md)

## 📋 Overview

`ArchivedEmails` holds the parsed form of a mail: subject, body, addresses, attachments.
That form is what search and display need, but it is not the message as it was delivered,
and an export rebuilds a new `.eml` from it. Retention rules such as the German GoBD expect
electronically received documents to be kept in the form they were received in and to be
protected against unnoticed change.

For that, the original message bytes get their own table in a separate schema:

| Layer | Schema | Content | Changeable |
|---|---|---|---|
| Working layer | `mail_archiver` | Parsed fields, folder, search data | Yes (locked rows only by the existing lock rules) |
| Write-once layer | `archive_worm` | Original `.eml` bytes, SHA-256, capture time | No |

## ⚙️ Capture

Capture is always on in this fork; there is no setting to disable it. Whoever does not need
GoBD-style archiving should use the upstream Mail Archiver.

The IMAP sync fetches each new message as raw bytes, stores them unchanged in `archive_worm`
together with their SHA-256 (in the same transaction as the email), and also writes the hash
to `ArchivedEmails.ContentHash` / `HashCreatedAt`. Storage per email roughly doubles, because
attachments are kept once in the parsed form and once inside the original.

Not covered yet: emails archived before this version, EML/MBOX import and Microsoft 365
(Graph) accounts.

### Effect on deleting emails

An email whose original is still within `RetainUntil` cannot be deleted. The application
checks this up front instead of running into the database error:

| Path | Behaviour |
|---|---|
| Local retention (`Local Retention Days`) | Such emails are skipped and kept; the rest is deleted as before |
| Manual delete of a single email | Refused with an error message |
| Bulk delete / delete job | Such emails are skipped; the job reports how many were kept |
| Deleting a mail account | Refused while the account has such emails; nothing is changed. The delete page says so up front, with the count and the date the last original is retained until |

A single retained original blocks deleting its whole account, including the emails of that
account that have no stored original. The account can be disabled instead; it becomes
deletable once the last of its originals has passed `RetainUntil`. Single emails without an
original can still be deleted individually or in bulk.

## 🗄️ Table `archive_worm."ArchivedEmailSources"`

| Column | Description |
|---|---|
| `ArchivedEmailId` | `ArchivedEmails.Id` this source belongs to (1:1, cascades on delete) |
| `RawMime` | Raw RFC 5322 message bytes exactly as received |
| `Size` | Length of `RawMime`, set by the database |
| `Sha256` | SHA-256 of `RawMime` as lowercase hex, verified by the database |
| `CapturedAt` | Capture time, set by the database (cannot be backdated) |
| `Source` | `imap`, `eml-import`, `mbox-import`, `graph` or `reconstructed` |
| `RetainUntil` | No deletion before this time; set by the database to the end of the 10th year after the capture year |

## 🔒 What the database enforces

Triggers created by migration `MigrateV2610_1`:

| Operation | Rule |
|---|---|
| `INSERT` | The hash is recomputed from `RawMime` and must match the given `Sha256`; `Size` and `CapturedAt` are set by the database |
| `UPDATE` | Always rejected |
| `DELETE` | Only after `RetainUntil` has passed |
| `TRUNCATE` | Always rejected |

The delete rule deliberately ignores `ArchivedEmails.IsLocked`: the application can switch
that flag off itself (retention, account deletion), so it is no protection for the
original. Deleting a mail cascades to its source, which means a mail whose original is
still retained cannot be deleted at all, whatever its lock state. Once `RetainUntil` has
passed, deleting the mail removes the original as well.

`RetainUntil` always uses the longest statutory period (10 years under § 147 AO / § 257
HGB, counted from the end of the calendar year). Shorter periods per document type are a
planned, administrator-only extension.

## 🛡️ Hardening: separate owner role (recommended for compliance setups)

Triggers alone only protect against the application. Whoever owns a table can drop its
triggers, and a PostgreSQL **superuser** bypasses every permission. In the default
`docker-compose.yml` the application connects as `POSTGRES_USER`, which is a superuser.

The hardening therefore has two parts:

1. The application connects with its **own non-superuser role**.
2. `archive_worm` is owned by a separate role without login, and the application role only
   gets `SELECT` and `INSERT` there. It can then neither change nor delete stored originals
   directly, nor drop or replace the triggers.

### Step 1: create the application role and hand it the regular schema

Run as the database superuser (e.g. `docker compose exec postgres psql -U mailuser -d MailArchiver`)
with the application **stopped**. Choose your own password.

```sql
CREATE ROLE mailarchiver_app LOGIN PASSWORD 'change-me';
GRANT CONNECT ON DATABASE "MailArchiver" TO mailarchiver_app;
-- EF Core issues CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (in schema public)
-- on every start. Since PostgreSQL 15 only superusers and the schema owner may create
-- objects in public, so without this grant the startup migration fails.
GRANT CREATE ON SCHEMA public TO mailarchiver_app;
-- The application runs CREATE EXTENSION IF NOT EXISTS citext on every start, which a
-- non-superuser may not do. Make sure it already exists.
CREATE EXTENSION IF NOT EXISTS citext;

DO $$
DECLARE r record;
BEGIN
    EXECUTE 'ALTER SCHEMA mail_archiver OWNER TO mailarchiver_app';
    FOR r IN SELECT tablename FROM pg_tables WHERE schemaname = 'mail_archiver' LOOP
        EXECUTE format('ALTER TABLE mail_archiver.%I OWNER TO mailarchiver_app', r.tablename);
    END LOOP;
    FOR r IN SELECT sequencename FROM pg_sequences WHERE schemaname = 'mail_archiver' LOOP
        EXECUTE format('ALTER SEQUENCE mail_archiver.%I OWNER TO mailarchiver_app', r.sequencename);
    END LOOP;
    FOR r IN SELECT p.oid::regprocedure AS sig FROM pg_proc p
             JOIN pg_namespace n ON n.oid = p.pronamespace WHERE n.nspname = 'mail_archiver' LOOP
        EXECUTE format('ALTER FUNCTION %s OWNER TO mailarchiver_app', r.sig);
    END LOOP;
    EXECUTE 'ALTER TABLE public."__EFMigrationsHistory" OWNER TO mailarchiver_app';
END $$;
```

### Step 2: lock down `archive_worm`

```sql
SELECT archive_worm.harden('mailarchiver_app');
```

This creates the role `mailarchiver_worm_owner` (no login), makes it the owner of the
schema, the table and all functions in `archive_worm`, and leaves `mailarchiver_app` with
`SELECT` and `INSERT` only. A different owner role name can be passed as second argument.

### Step 3: switch the application to the new role

Change the connection string in the mounted `appsettings.json` (or set the environment
variable `ConnectionStrings__DefaultConnection` on the `mailarchive-app` service):

```json
"ConnectionStrings": {
  "DefaultConnection": "Host=postgres;Database=MailArchiver;Username=mailarchiver_app;Password=change-me"
}
```

Keep `POSTGRES_USER` for administration only and do not give its password to the application.

### Limits

- A database superuser can still do anything. The hardening separates the application
  from the archive; it does not protect against the database administrator. For that,
  store the originals on storage with a retention lock (e.g. S3 Object Lock), which is
  planned as an optional later step.
- Future migrations that change `archive_worm` must be run by an administrator, because
  the application no longer owns that schema.
- The parsed copy in `mail_archiver` is still controlled by the application (it can be
  unlocked and changed). The original in `archive_worm` and its hash stay the reference
  that any later change can be checked against.

## 🔗 Related Documentation

- [Retention Policies](RetentionPolicies.md)
- [Access Logging](Logs.md)
- [Audit Data Export](AuditExport.md)
- [Backup and Restore Guide](BackupRestore.md)
