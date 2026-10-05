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

> 🚧 **Status**: This is the storage foundation only. Nothing writes to the table yet;
> capturing the original bytes during IMAP sync and import follows in a later change.

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
