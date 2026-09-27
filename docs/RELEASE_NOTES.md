# 0.3.0-beta.2

- Claude quota now prefers the structured list used by the official client,
  preserving named model/surface windows and excluding duplicate/internal
  compatibility buckets. Legacy responses remain supported.
- Added contract cases for unknown percentages, inactive windows, distinct scopes,
  duplicate scope identity and legacy fallback. Automated suite: 68 tests.
- Added a bounded normal-background observation command for CPU/memory/handles,
  dispatcher delay, collection progress and provider refresh states.
- Sandbox package selection is version-specific and can validate an upgrade
  from beta.1 to beta.2 while retaining the existing history.
- Verified clean Windows Sandbox installation, beta.1 upgrade, restart, uninstall
  with retained data, and actual background startup after a new guest logon.
- Compared read-only quotas with Claude Code 2.1.283 and Codex 0.155.1. Completed
  a 30-minute normal-background resource observation with no collection failures.
- Public MIT repository and Windows CI covering build, 68 tests, packages and
  synthetic UI smoke. Source and distribution packages exclude local account data.

Still a pre-release; physical monitor, actual suspend/resume and live OpenRouter
account validation are not claimed by these changes.

# 0.3.0-beta.1 — local preview

First independent Windows implementation: incremental Claude/Codex token history,
read-only subscription quota cards, widget/tray/dashboard, analytics and exact-model
estimated pricing. Adds OpenRouter key/credit information and separate management-key
activity history, DPAPI credentials, CSV/JSON export and TR/EN themes.

Windows controls include card ordering/visibility, compact mode, cost visibility,
opacity, position lock, always-on-top, monitor-aware placement, opt-in startup and
deduplicated quota/reset/balance notifications. Includes a self-contained portable
packaging script, per-user installer script, license texts and Windows CI workflow.

Known release limitations: Claude quota source is experimental; old Codex fork
records can be excluded as uncertain; price estimates use current standard
short-context tariffs. OpenRouter live account verification and full physical
DPI/suspend/long-duration testing remain explicit release gates in WINDOWS_QA.md.
The self-contained portable and installer passed a clean Windows 11 Sandbox
lifecycle test without an installed SDK, including restart and data retention.

Beta.1 was a local preview. Initial packages are unsigned.
