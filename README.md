# InboxSentinel

InboxSentinel is a Windows desktop app for monitoring Gmail and Outlook inboxes with OAuth login only.

It shows all incoming mail notifications and highlights high-risk messages (for example password reset, payment, invoice, or security-related mail) with a red warning state.

## What It Does

- OAuth-only sign-in for:
  - Gmail
  - Outlook/Hotmail
- Real-time inbox monitoring using IMAP
- Shows all new emails
- Red warning flag for keyword matches
- Built-in keyword management in Settings

## Requirements

- Windows 10/11
- .NET 10 SDK (project target: `net10.0-windows`)
- A Google OAuth Desktop app client ID (for Gmail)
- A Microsoft app registration client ID (for Outlook)

## Quick Start

1. Clone the repo.
2. Build:
   - `dotnet build`
3. Copy `oauth.settings.sample.json` to `oauth.settings.json` (local machine only).
4. Add your client IDs in `oauth.settings.json`.
5. Run:
   - `dotnet run`

## OAuth Configuration

`oauth.settings.json` is local-only and should not be committed.

### Gmail

- `authorizationEndpoint`: `https://accounts.google.com/o/oauth2/v2/auth`
- `tokenEndpoint`: `https://oauth2.googleapis.com/token`
- Required scope:
  - `openid email https://mail.google.com/`

### Outlook

- `authorizationEndpoint`: `https://login.microsoftonline.com/common/oauth2/v2.0/authorize`
- `tokenEndpoint`: `https://login.microsoftonline.com/common/oauth2/v2.0/token`
- Required scopes:
  - `openid email offline_access https://outlook.office.com/IMAP.AccessAsUser.All`

Note: This app uses PKCE desktop flow. Keep `clientSecret` blank unless your provider setup explicitly requires it.

## Usage

1. Open **Connect Accounts**.
2. Choose Gmail or Outlook.
3. Click **Sign in with ... (OAuth)**.
4. Complete browser sign-in.
5. Click **Connect**.
6. Go to **Settings** to manage warning keywords.

## If You Get Stuck (Troubleshooting)

### "OAuth Not Configured"

- Ensure `oauth.settings.json` exists in the app folder.
- Verify provider section names are exactly `Gmail` and `Outlook`.
- Ensure `clientId` is filled for the provider.

### Gmail: "Authentication failed" after OAuth

- Confirm Gmail scope includes:
  - `openid email https://mail.google.com/`
- In Gmail web settings, make sure IMAP is enabled.
- Confirm the signed-in Google account has a real Gmail/Workspace mailbox.

### Outlook: "Authentication failed" after OAuth

- Confirm scope includes:
  - `openid email offline_access https://outlook.office.com/IMAP.AccessAsUser.All`
- Ensure IMAP is enabled for that mailbox in Microsoft account/org policies.

### "OAuth succeeded but no email/username claim was returned"

- Add `openid email` to provider scope.
- Re-run OAuth sign-in after saving `oauth.settings.json`.

### App builds but run/login behaves unexpectedly

- Close all running `EmailToastUI.exe` processes.
- Rebuild:
  - `dotnet clean`
  - `dotnet build`

## Security and Privacy Notes

- `oauth.settings.json` is ignored by git and should stay local.
- Runtime file logging is disabled by default.
- If file logging is enabled (`EMAILTOAST_ENABLE_FILE_LOGGING=1`), sensitive token fields are redacted.

## Current Limitations

- Provider support is currently Gmail + Outlook only.
- One connected account per provider in current UI flow.

