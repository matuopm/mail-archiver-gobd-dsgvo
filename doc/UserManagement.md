# 👥 User Management and Mailbox Permissions

[← Back to Documentation Index](Index.md)

## 📋 Overview

This guide provides detailed instructions for creating new user accounts and assigning mailbox permissions in the Mail Archiver application.


## 🛠️ Prerequisites

- Administrative access to the Mail Archiver application
- Existing email accounts configured in the system

## 🧑‍💻 Creating a New User Account

1. Log into the Mail Archiver application with an account that has administrative privileges
2. Navigate to the "Users" section from the main menu
3. Click the "Create New User" button
4. Fill in the required user information:
   - **Username**: Enter a unique username for the new user
   - **Password**: Enter a secure password for the user
   - **Email**: Email address of the user
   - **Admin**: Check this box if the user should have administrative privileges
   - **Self Manager**: Check this box if the user should be able to manage their own mail accounts (edit existing ones and add new ones as well as delete accounts)
   - **active**: Should be checked to allow logins for the user
5. Click "Create User" to save the new user account

## 🔐 Assigning Mailbox Permissions to Users

1. After creating the user, or when editing an existing user, navigate to the "Users" section from the main menu
2. Find the user in the list where you want to assign mail accounts and click on the "Assign" button for that user
3. In the "Assign Mail Accounts" page, you will see all available mail accounts and checkboxes which indicate if they are assigned to the user
5. To assign a new email account check the corresponding box for the specific account
6. To remove an email account from the user uncheck the box
7. Click "Save Assignments" to apply the changes

## 👤 User Account Permissions

### Admin Users
Admin users have full access to the application and can:
- Manage all user accounts and their permissions
- Access all email accounts in the system
- View all access logs
- Perform all administrative tasks

### Self Manager Users
Self manager users can:
- Add new email accounts to their own account
- Edit existing email accounts assigned to them
- Delete email accounts assigned to them
- Access and manage emails from their assigned accounts (including deletion)
- View their own access logs

### Standard Users
Standard users have limited access and can:
- View archived emails from assigned email accounts
- Search within their assigned email accounts
- Export emails from their assigned accounts
- Restore emails from their assigned accounts
- Access email attachments from assigned accounts

### Auditor Users (read only)
The auditor role is meant for a tax auditor or an external reviewer who needs read access
to the archive (GoBD "Z1" read access). An auditor:
- Sees **all** mail accounts without any mailbox assignment, unless limited (see below)
- Can search, open and download emails and attachments, and export selected emails
- Reads the whole access log of all users and can run **Check log**
- Can change their own password, set up 2FA and manage their own API keys (the API and
  MCP are read-only anyway)

An auditor cannot restore (copy back) emails, start or cancel syncs, import, manage mail
accounts, users or settings, delete anything, or start the audit data export. The auditor
role cannot be combined with Admin or Self Manager.

This is enforced on the server, deny by default: for an auditor every request that is not
a plain read (GET) is refused unless the action is explicitly marked as read-only
(`[AuditorAllowed]`), and the restore forms are closed even for GET (`[AuditorForbidden]`).
Actions added later are therefore closed for auditors until someone reviews them.
Everything an auditor opens is recorded in the access log like for every other user.

#### Limiting an auditor to mailboxes and an audit period

By default an auditor sees every mail account and every email. Both can be limited, for
example when a tax audit only covers certain years:

- **Mailboxes:** assign the mail accounts with **Assign**, as for a standard user. An
  auditor without assigned mail accounts sees all of them.
- **Audit period:** on the user's **Edit** page, set *Audit period from* and/or *to*
  (dates inclusive, either may stay empty). Only emails sent within the period are visible.

The limit applies to everything the auditor reads: search, email view, attachments,
originals, exports, dashboard, REST API and MCP. It is enforced by global query filters of
the database context for every request of that auditor, so emails outside the scope do not
exist for them. A limited auditor sees only those access log entries that refer to emails
or mail accounts within the scope, plus their own entries. Changes take effect with the
auditor's next request.

Role changes take effect at the next login. Deactivate the auditor's account when the
audit is over.

## 🔒 Security Considerations

1. Use strong passwords for all user accounts
2. Only assign the Admin permission to users who need it
3. Only assign the Self Manager permission to users who need to manage their own accounts
4. Regularly review user permissions to ensure access is appropriate
5. Remove user access when it is no longer needed
6. Use the principle of least privilege - only assign access to the email accounts that users need
