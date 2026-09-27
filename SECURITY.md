# Security and privacy

The application reads local usage records and explicitly configured provider
profiles. It stores counts, timestamps, model IDs, project/session identifiers,
source cursors and provider quota snapshots. It does not store conversation
messages, source code, raw OAuth credentials or raw API keys in SQLite.

OpenRouter credentials are encrypted with current-user Windows DPAPI. Copying
the encrypted file to another Windows user does not transfer access. DPAPI is
not protection from malicious software already running as that same user.
Claude credentials are read transiently from the selected official CLI profile.
Codex manages its own credentials through its local app-server process.

Network destinations are fixed provider endpoints; HTTP redirects are disabled.
Requests have deadlines and size limits. Provider response bodies and process
stderr are not written to diagnostics. There is no telemetry or hosted app
account. Windows startup and notifications are opt-in.

Do not attach production settings, `.credentials.json`, `auth.json`, `.secrets`,
JSONL files or databases to a public issue. Project names and usage counts can
also be sensitive. Use `--demo` for screenshots and synthetic repro cases.

For a sensitive issue, use [private vulnerability reporting](https://github.com/ozkurkuran/ai-usage-viewer/security/advisories/new).
The repository's private reporting channel is enabled. Never put working
credentials, private logs or reproduction data in a public issue.

The initial Windows artifacts are unsigned. Checksums identify release content;
they are not a publisher signature. No signed-release claim is made.
