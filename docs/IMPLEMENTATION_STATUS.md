# Implementation evidence

Scope: [original plan](PLAN_TR.md). No stable v1.0 or completed full-roadmap claim is made
until the remaining release gates have actual evidence.

| Phase | Implementation and evidence | Still required |
|---|---|---|
| P0: data validation | Parser tests; live quota; Claude 2.1.283 `/usage` and Codex 0.155.1 `/status` scope/percentage/reset comparisons | Complete for the tested client versions |
| P1: application foundation | Independent WPF solution, SQLite, MVVM, three surfaces, TR/EN | Complete |
| P2: usable local monitor | Integrated live UI validation, incremental collection, cached quota/failure handling, restart tests and 30-minute normal-background observation | Longer live soak / actual resume observation |
| P3: analytics | Trend/heatmap, filters, project→session→model drill-down, versioned exact-model prices/overrides and CSV/JSON | Extended UI smoke passed; no separate feature blocker identified |
| P4: OpenRouter | Independent key/credits/history, partial source retention, period/BYOK, permission, key-change and Retry-After tests | Live standard/management-key verification unavailable without a test key |
| P5: Windows release | Customization, monitor placement, notifications/startup, public MIT repository, CI, portable/installer; Sandbox lifecycle, beta.1→beta.2 upgrade and real guest login passed | Physical mixed-DPI, actual suspend/resume and longer field use pending |

## Completed evidence (2026-09-27)

- .NET SDK 10.0.401 downloaded from Microsoft and SHA-512 verified. Runtime and
  NuGet dependencies are local to the parent workspace's `.tools` directory.
- Complete Release solution builds with zero warnings and zero errors.
- Latest completed suite: **68 passed, 0 failed**, including synthetic/live
  directory isolation and rejection of invalid HTTP credential characters.
  Added current Claude structured-limit, model/surface scope and legacy-fallback cases.
- Tests cover normalized token buckets, cumulative deltas/resets, copied logs,
  streaming revisions, fork identity, partial lines, transactional cursors,
  file replacement with unchanged length/mtime, time zones, dynamic quota windows,
  read-only RPC sequence, retries/concurrency/cancellation, OpenRouter periods,
  independent permissions, stale source retention, history replacement, pricing,
  CSV formula escaping, DPAPI, notification persistence and schema migration.
- Claude OAuth usage and Codex app-server both returned usable live quota windows.
  Integrated `--validate-live` run passed at 2026-09-27T18:02:49Z: local records,
  two account cards, both states Ready, all three windows rendered. No model calls,
  reset redemptions or private screenshots. Private reports remain ignored.
  A network-restricted run preserved cached windows and reported Unavailable.
  Validation now records cached rendering and fresh quota success separately.
- Initial large-history scan is background work. Profiling isolated a 38–53 second
  delay to opening active-log read handles, independently reproduced in Python.
  Metadata checks and reads/hashes were fast. A bounded content-handle cache with
  metadata identity checks now uses explicit-offset reads. Measured same-process
  repeat: three changed files in about **0.19 seconds**. Cold opens may still take
  around forty seconds on this host; cached UI data is displayed while they warm.
- A separate fresh-database import of 357 files took about 132 seconds before
  measurement began. The subsequent normal-background observation completed
  1,800.92 seconds / 60 samples: 232 additional scans and 950 changed-file reads,
  zero collection failures or source warnings, and both quota providers Ready.
  Process CPU increased by 89.14 seconds (about 4.95% of one CPU core); private
  bytes ranged 69.19–95.52 MiB and ended at 78.12 MiB versus 88.40 MiB initially.
  Working set ranged 335.09–396.14 MiB; handles ended at 847 versus 921 initially.
  Maximum measured dispatcher delay beyond its one-second heartbeat was 0.861
  seconds. This host was concurrently running development and Sandbox tasks;
  these measurements are a bounded baseline, not an overnight/leak-free claim.
- Extended WPF smoke passed (`artifacts/suite-today/smoke-report.json`): three main
  surfaces, project/session/model navigation, widget-only visibility, ordering,
  remaining mode, TR/dark and EN/light settings, all five settings pages,
  synthetic OpenRouter history and monitor clamping. Main Save commits pending
  account/visibility/price changes; selected prices show their source and date.
  Empty data has no invented cost/accounts, closing windows keeps the tray alive,
  and the widget's today total/cost remain independent of every dashboard filter.
- Render targets at 100/150/200% were generated. Actual connected display tested
  was 3840×2112 work area at 96 DPI. This is not physical mixed-DPI coverage.
- Smoke exposed and fixed transient null selections when replacing localized
  ComboBox lists. Screenshots also led to better initial widget height, dark
  scrollbars, readable light-theme quota colors and simple account/model labels.
- Self-contained win-x64 packages built: portable ZIP ~78 MB, installer ~55 MB,
  plus SHA256SUMS. Packaged smoke confirmed use of the included runtime.
- Clean Windows Sandbox build 26100, with no dotnet on PATH, passed at
  2026-09-27T17:44:11Z (`artifacts/sandbox-20260927-v2/sandbox-result.json`):
  package file hashes, portable startup, per-user installation, installed startup,
  restart history retention (632 synthetic records / 9,762,481 tokens), same-version
  reinstall and uninstall. App files and its startup entry were removed; user
  history remained. No host registry/startup setting was changed by this test.
  Beta.2 subsequently passed the same lifecycle plus an actual beta.1→beta.2
  upgrade at 2026-09-27T18:43:14Z (`artifacts/sandbox-beta2/sandbox-result.json`).
- Actual Windows startup passed at 2026-09-27T18:51:58Z in a separate Sandbox:
  HKCU Run launched the app after sign-out and a new Windows logon, verified by
  the token authentication ID. Paths contained spaces, the data initialized, and
  no main window was visible. Host startup/desktop settings were not changed.
- Official-client comparison passed: Claude session/weekly percentages and named
  model scope/reset, Codex weekly remaining/reset. Other account activity continued
  during the observations; the structured Claude model window was checked after
  its adapter fix. Reports with live values stay under ignored `local/official-display`.
- Inno Setup 7.1.0 downloaded from its official release. Authenticode status Valid,
  publisher Pyrsys B.V.; SHA-256 checked and pinned in Windows CI.
- MIT sources are public at https://github.com/ozkurkuran/ai-usage-viewer.
  Private vulnerability reporting is enabled. [Windows CI](https://github.com/ozkurkuran/ai-usage-viewer/actions/runs/36341182250)
  passed build, all 68 tests, packaging and UI smoke for source commit 38540c5.
  No stable release is claimed.

## Remaining release work

### Microsoft Store preparation (1 October 2026)

- Version 0.3.0-beta.3 / MSIX 0.3.3.0; application/window/tray icons, Store assets,
  MSIX packaging/CI, privacy policy and Turkish submission guide are prepared.
- All 68 automated tests passed; Release self-contained publish succeeded.
- Demo-only `--language en|tr` applies language and date/number culture.
  `--capture-scale` accepts finite 1–3 values only for demo screenshots; invalid
  NaN was rejected before capture. The explicit physical-DPI render loop retains
  its own 1/1.5/2 scales. Session capture waits for deferred table layout.
- English synthetic UI suite passed with 632 records / 9,762,481 tokens.
  Five 3840×2160 composed Store PNGs were visually inspected under
  `artifacts/store-screenshots`; all use synthetic data and a demo label.
- MSIX clean Sandbox build 26100 passed installation, Start-menu launch,
  containerized data, StartupTask enablement, background startup after a new
  Windows logon, uninstall and removal of package data
  (`artifacts/msix-check-2/msix-result.json`). The final package passed the same
  complete scenario (`artifacts/msix-check-final-2/msix-result.json`).
- Final package output is `artifacts/msix-final`. Partner Center
  identity values are empty, so these are local-test packages, not Store uploads.
  WACK and Store certification remain pending. No commit/push was performed.

### Ai UsageNest Store submission (3 October 2026)

- Product renamed to Ai UsageNest (publisher Mikrofab, Store ID 9P3MB5NZ4K55).
  Partner Center identity `Mikrofab.AiUsageNest` /
  `CN=3ECE3801-0802-47D1-A7CD-137704DE8F59` is in `packaging/msix/identity.json`.
- Release build 0 warnings / 0 errors; 68/68 tests passed.
- Upload package: `artifacts/msix-store/AIUsageViewer-0.3.0-beta.3-win-x64.msix`
  (0.3.3.0, Store identity, unsigned). Its manifest DisplayName, tile ShortName
  and StartupTask name are "Ai UsageNest"; PublisherDisplayName is "Mikrofab".
- English and Turkish synthetic UI suites passed; five 3840×2160 screenshots per
  language (`artifacts/store-screenshots`, `artifacts/store-screenshots-tr`)
  were inspected. The empty-data capture shows the Getting started card.
- An interactive `--demo` instance (the "Try with sample data" process) opened
  the "Ai UsageNest" window and exited with code 0 when the window was closed.
- Clean Windows Sandbox (build 26100, no dotnet on PATH) with the Store-identity
  package (test-signed copy) passed install, Start-menu activation, package data,
  StartupTask enable, background start after a new logon and uninstall with
  package-data removal. Package family `Mikrofab.AiUsageNest_6detc4ys2wkvc`
  matches Partner Center (`artifacts/msix-check-store/msix-result.json`).
- WACK needs an elevated session and was not run here.

### UI redesign (4 October 2026)

- Release build 0 warnings / 0 errors; 118/118 tests (adds pace, row status,
  forecast cut-offs, remaining-mode inversion, stale rule, EN/TR formatting and
  pace-alert tests).
- Smoke suite (`scripts/smoke.ps1 -Suite -Views -Language en|tr`) passed with
  29 checks each, including pace marker position, forecast only when ahead,
  remaining-mode inversion, stale rendering, widget without scrollbar, first-run
  checklist only with zero records, Settings Save of all dirty sections and no
  API key field for Claude/ChatGPT. 96 screenshots per language: every view in
  dark and light at 1×, 1.5× and 2× render scale
  (`artifacts/redesign/final-en|tr/views`). Physical-DPI switching is still a
  manual check.

### Stable release

1. Extend the completed 30-minute resource observation to daily/overnight use.
   Do not infer overnight stability from this bounded baseline.
2. Resolve physical mixed-DPI, monitor removal, actual suspend/resume and live
   OpenRouter standard/management-key checks.
3. Complete the remaining gates in ACCEPTANCE_TR.md. Unavailable external/hardware
   evidence remains explicit rather than being marked passed by unit tests.

See WINDOWS_QA.md for the detailed manual scenarios. Reference repositories are
unchanged and not dependencies. Local account data, settings, databases, keys and
private validation output must remain outside source/release packages.

### Language expansion (3 October 2026)

- Added complete embedded catalogs (140 strings each) for 12 language options:
  en, tr, es, de, fr, pt, pt-BR, ru, nl, cs, it and pl. Desktop and Windows board
  cards use the same catalogs. New installations default to System language;
  regional display languages map to a supported language or English, and saved
  manual en/tr preferences stay selected.
- Settings include System language and native language names. Language changes
  update number/date formatting, and Windows widget snapshots carry the resolved
  language. The MSIX manifest declares all 12 language tags.
- Added Partner Center description/feature drafts under packaging/store/listings;
  these are JSON field drafts, not Partner Center import CSV files. Screenshot
  generation accepts all 12 languages and keeps the light widget in the same
  language as the listing. No Store upload or publication performed for this change.
- Release build passed with zero warnings/errors. Current automated suite:
  103/103 passed. Every language passed 18 synthetic GUI checks (216 total),
  including settings selection, culture agreement, navigation, saves and widget
  rendering. Results: artifacts/localization-smoke/languages.json. German,
  French, Russian and Polish dashboard/settings/widget captures visually checked.

### Windows 11 Widgets Board (3 October 2026)

- Added a self-contained x64 provider, COM/Widgets manifest registrations and
  package-scoped IWidgetProvider RPC proxy. Small/medium/large Adaptive Cards
  follow app language, account order/visibility and percentage/cost settings.
  Commands reuse the packaged app; the provider reads only an atomic display
  summary, without credentials, conversation identifiers or a second database writer.
- Release build: zero warnings/errors; 103/103 automated tests passed. Synthetic
  packaged WPF smoke passed 18 checks (`artifacts/widget-ui-smoke/smoke-report.json`).
  All three Adaptive Cards rendered without validation errors; the 300x304 picker
  preview uses synthetic data, not a live Widgets Board capture.
- Final upload package is unsigned
  `artifacts/msix-widget-store-v2/AIUsageViewer-0.3.0-beta.3-win-x64.msix`,
  version 0.3.3.0, family `Mikrofab.AiUsageNest_6detc4ys2wkvc`.
  Windows Sandbox build 26100, without dotnet on PATH, passed installation,
  Start-menu activation, snapshot publication, native cross-process
  IWidgetProvider activation, StartupTask background launch after a new logon,
  and uninstall/data removal (`artifacts/widget-sandbox-5/msix-result.json`).
- Live Win+W card appearance/actions still require manual acceptance. Native UI
  automation was unavailable on this host. Partner Center access stopped at
  Microsoft's updated service agreement; no package upload or publication occurred.
  See [Windows widget guide](WINDOWS_WIDGET_TR.md).

## Workspace commands

```powershell
$env:DOTNET_CLI_HOME = Join-Path $PWD '.tools/cli'
$env:NUGET_PACKAGES = Join-Path $PWD '.tools/nuget'
$env:DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE = 'true'
& ./.tools/dotnet/dotnet.exe build app/AiUsageViewer.slnx -c Release --no-restore --disable-build-servers -m:1 -p:UseSharedCompilation=false
& ./.tools/dotnet/dotnet.exe test app/tests/AiUsageViewer.Tests -c Release --no-restore --disable-build-servers -m:1 -p:UseSharedCompilation=false
& app/scripts/package.ps1 -Dotnet (Join-Path $PWD '.tools/dotnet/dotnet.exe') -Iscc (Join-Path $PWD '.tools/inno/ISCC.exe') -SkipRestore
& app/scripts/package-source.ps1
```

Use one MSBuild node with build/compiler servers disabled in this environment.
Do not restart a live build merely because an output poll is empty.
