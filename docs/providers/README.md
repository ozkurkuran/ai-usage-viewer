# Provider contracts

Validated source date: 2026-09-27. Monitoring is read-only; no model invocation,
session activation, quota reset redemption or credential refresh is performed.

| Provider | Token source | Account source | Status |
|---|---|---|---|
| Claude Code | `projects/**/*.jsonl` under selected config | OAuth `/api/oauth/usage` | Local/live quota and official `/usage` comparison with CLI 2.1.283; experimental endpoint |
| Codex | `sessions/**/*.jsonl`, `archived_sessions/**/*.jsonl` under CODEX_HOME | Official local `codex app-server` | Live tested with CLI 0.155.1 |
| OpenRouter | No local collector | `/api/v1/key`, `/credits`, `/activity` | Synthetic HTTP/contract tests; no real API key was supplied |

## Claude

The collector reads assistant usage records with message identity and maintains
input/output/cache-read/cache-write buckets. Streaming revisions replace the
earlier token bundle. Duplicate message IDs across copied files are not summed.
Cache writes are split into five-minute and one-hour pricing buckets when known.

The quota adapter reads only `claudeAiOauth.accessToken` from the chosen profile's
`.credentials.json`, sends it directly to `https://api.anthropic.com/api/oauth/usage`
and prefers the structured `limits` list used by current Claude Code: kind/group,
percent/reset and model/surface scope. This avoids duplicating compatibility and
internal promotional fields. Legacy utilization/reset windows remain a fallback.
Null percentages remain unknown; `is_active: false` does not erase an existing
quota window. Model IDs or display names and surface distinguish scoped limits.
The endpoint is used by the official client but is not assumed to be a stable
public third-party API. A denied/expired credential requires signing in through
the official CLI; the viewer does not change the CLI account or its files.

## Codex

The collector reads cumulative token_count deltas, handles resets, and uses stable
turn identity where available. Cached input is removed from ordinary input;
reasoning stays within output. Old forked records without stable turn identity are
flagged uncertain and excluded from verified totals. The UI reports their count.

The quota sequence is `initialize` → `initialized` →
`account/read(refreshToken=false)` → `account/rateLimits/read`. Each profile runs
with its own CODEX_HOME; the viewer never reads Codex's auth file. The child process
is bounded, its stderr discarded and it is terminated after the request. Dynamic
primary, secondary and model-specific windows are retained. Missing windows are
not invented. The CLI executable must be installed separately.

The optional account-wide `account/usage/read` summary is not ingested: local
project/session totals remain the token source, preventing duplicate accounting.
See the [official App Server reference](https://developers.openai.com/codex/app-server).

## OpenRouter

The settings form accepts a key through a password field and stores only a DPAPI
reference in settings. Key, credits and activity requests are independent. Missing
management permissions do not hide a standard key's available usage. Failed key
or credit refreshes retain each available source's original successful timestamp;
retained values are visibly stale. HTTP 429 is honored even during partial success.

Key limits prefer `limit_remaining`; otherwise usage must match the daily, weekly,
monthly or lifetime reset period and the BYOK inclusion rule. Lifetime spend is
never divided by a monthly limit. Account credits are not summed across keys.

Activity requires a management key. The API provides 30 completed UTC days;
the viewer replaces those days atomically, retains older previously observed
days and shows the account-wide history separately. It does not add it to local
token totals or merge histories from multiple keys. Missing/invalid pages cannot
erase a successful history. Replacing a key creates a new credential reference and
invalidates its old history binding.

References: [key](https://openrouter.ai/docs/api/api-reference/api-keys/get-current-api-key),
[credits](https://openrouter.ai/docs/api/api-reference/credits/get-credits),
[activity](https://openrouter.ai/docs/api/api-reference/analytics/get-user-activity-grouped-by-endpoint).

## Storage and refresh

The first scan is in the background. Cursors/events commit together, partial final
lines are retried, content handles are bounded and file identity catches rotation
even with unchanged length/mtime. There is a two-second filesystem debounce and
a thirty-second reconciliation check. Records only appear after the source tool
writes them. Cold file opens can be slow on the validation host; warm incremental
reads have been measured at approximately 0.19 seconds for three changed files.

Quota polling defaults to five minutes, with bounded concurrency and backoff.
Activity uses a separate fifteen-minute cadence. Manual refresh has a minimum gap
and cannot bypass Retry-After. Reset countdowns are calculated locally. Resume
triggers bounded refresh, not a backlog of accumulated timer requests.

Token events are retained locally. Quota observations are recorded only as they
are observed, not reconstructed historically. Config/profile paths and model/
project names are metadata, not anonymized personal data; handle exports accordingly.
