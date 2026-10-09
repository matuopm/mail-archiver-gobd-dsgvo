# 📋 Access Log

[← Back to Documentation Index](Index.md)

## 📋 Overview

This guide explains the access log functionality in the Mail Archiver application, which enables logging and viewing of user activities within the system. This functionality is crucial for monitoring, security, and debugging purposes.

## 📊 What is Logged

The application logs various types of user activities, including:

1. **Login and Logout** - Logging of login and logout operations
2. **Search Operations** - Logging of email search requests within the system
3. **Email Opening** - Logging when users open email content
4. **Email Downloading** - Logging when users download emails
5. **Email Restoration** - Logging when users restore archived emails
6. **Account Management** - Logging of account-related actions
7. **Deletion Operations** - Logging of deletion actions
8. **Audit Data Export** - Logging of audit data export runs (start and result, see [Audit Data Export](AuditExport.md))

## 📝 Action Types and Descriptions

The logged actions are categorized into the following types:

### Login
- **Description**: Logs when a user logs into the system
- **Details**: Username, timestamp

### Logout
- **Description**: Logs when a user logs out of the system
- **Details**: Username, timestamp

### Search
- **Description**: Logs search requests within the email system
- **Details**: Username, timestamp, search parameters

### Open
- **Description**: Logs when a user opens an email
- **Details**: Username, timestamp, email ID, sender, subject

### Download
- **Description**: Logs when users download emails
- **Details**: Username, timestamp, email ID, sender, subject

### Restore
- **Description**: Logs when users restore archived emails
- **Details**: Username, timestamp, email ID, sender, subject

### Account
- **Description**: Logs account-related actions (creation, modification, etc.)
- **Details**: Username, timestamp, account ID

### Deletion
- **Description**: Logs deletion operations on emails or other resources
- **Details**: Username, timestamp, affected resource
- **Compliance Note**: Deletion operations are logged for compliance auditing purposes

## 🔍 Filter Options

The log page offers the following filter options:

1. **Date** - Filter by date range (From-To)
2. **Username** - Filter by specific user (Admin only)
3. **Action Type** - Filter by specific action type

## 🖥️ Log Display

Logs are displayed in a table format with the following columns:

- **Timestamp**: Date and time of the action
- **Username**: Name of the involved user
- **Type**: Type of action with color coding
- **Information**: Additional details about the action

For mobile devices, logs are displayed in card format.

## 🔐 Access Permissions

- **Non-admin users**: Can only view their own logs
- **Admin users**: Can view logs for all users and filter by user

## 🔗 Protection against later changes

The access log is write-once. It lives in the schema `archive_worm` next to the stored
original messages ([Write-Once Storage](WormStorage.md)), and the database enforces:

- Entries can only be added. `UPDATE`, `DELETE` and `TRUNCATE` on
  `archive_worm."AccessLogs"` are rejected, also for the application itself.
- Every new entry gets a sequence number (`ChainSeq`), the hash of the previous entry
  (`PrevHash`) and its own SHA-256 hash (`Hash`) over all its fields plus `PrevHash`. The
  database trigger sets these columns, not the application.

Whoever bypasses the triggers (only possible for a database superuser, or for the owner
of `archive_worm` when it was not [hardened](WormStorage.md#-hardening-separate-owner-role-recommended-for-compliance-setups))
and changes, deletes or inserts an entry breaks the chain from that entry on.

### Checking the log

Admins click **Check log** on the Logs page. The result shows how many entries were checked
and the check value (hash) of the last entry; if the chain is broken, it names the first
entry that does not fit. The same check in SQL:

```sql
SELECT * FROM archive_worm.verify_access_log();
-- checked | first_broken_seq | head_hash
```

Removing the newest entries at the end leaves a shorter chain that is still intact. To
catch that too, note the check value from time to time outside the system (for example
in the tax adviser's files or a dated email to yourself). A later check must still contain
an entry with that hash:

```sql
SELECT "ChainSeq", "Timestamp" FROM archive_worm."AccessLogs" WHERE "Hash" = '<noted value>';
```

Entries that existed before the upgrade were chained once by the migration, in the order
they were written; the protection covers them from that point on.

## 📤 Audit Data Export

Admins can open the dedicated [Audit Data Export](AuditExport.md) page directly from the Logs page. It creates tabular mass data packages (INDEX.XML + CSV) from the archive for external audit tools. The package always contains the access log of the export period as `accesslog.csv`, in German, with the hash chain columns. Every export run writes two access log entries of the type **Audit Data Export** (start and result), which are listed and filterable on the Logs page like all other entries.

For details see the [Audit Data Export guide](AuditExport.md).
