# Windows release gates

This checklist records actual evidence, not assumed coverage from unit tests.
Target: Windows 11 x64. ARM64 and Windows 10 support are not claimed.

| Check | Current evidence |
|---|---|
| Parse, deduplication, quota/period math, persistence | Automated suite; see implementation status for count |
| WPF startup / widget / tray / dashboard | Synthetic screenshots, close-to-tray behavior and clean exit on development host |
| Empty data and widget scope | Empty-state render passed; widget today total/cost unchanged by dashboard date/tool/model/project/session filters |
| Light/dark, TR/EN, compact, settings pages, drill-down | Extended synthetic smoke passed; pending form edits saved and source/date displayed |
| 100/150/200% rendering | Render-target smoke images; does not prove physical monitor DPI transitions |
| Monitor work-area clamping | Passed on available 3840×2112 work area at 96 DPI |
| Mixed-DPI monitor transition/removal | Pending physical hardware scenario |
| Actual suspend/resume and overnight usage | Pending long-duration manual observation |
| Normal-background resource observation | 30 minutes / 60 samples completed; no collection failures or source warnings; CPU/memory/handle figures in implementation status |
| Self-contained portable with fresh synthetic data directory | Passed; runtime loaded from package directory |
| Installer, uninstall and data retention | Clean Sandbox passed; 632 records / 9,762,481 tokens unchanged after restart; data retained on uninstall |
| Upgrade | Beta.1→beta.2 and same-version reinstall passed in clean Sandbox; history retained |
| Clean Windows 11 without SDK/CLI | Passed in network-isolated Windows Sandbox build 26100; dotnet absent from PATH |
| Windows startup | Real sign-out/new-logon in Sandbox passed: HKCU Run, quoted paths, no main window; uninstall removes matching entry |
| Live Claude/Codex read-only quota | Passed earlier; repeat after material provider changes |
| Official client quota display comparison at matching time | Claude Code 2.1.283 `/usage` and Codex 0.155.1 `/status` compared; structured Claude model scope fixed and verified |
| Live OpenRouter standard/management keys | No test key supplied; synthetic permissions/contract cases only |
| Windows CI | First public run passed build, 68 tests, packages and UI smoke; link in implementation status |

Manual release procedure:

1. On clean Windows 11 x64, extract the ZIP and launch without an SDK installed.
   Empty data is explained, the tray menu works, closing windows leaves the tray,
   and Exit ends the process. Install the setup package as a standard user.
2. Authenticate the official CLIs. Review discovered sources, test connections,
   and compare quota scope/percent/reset with each official client at the same time.
   Disconnect networking, refresh, then reconnect: last-good data and age persist.
3. Add a standard OpenRouter key, then a management key in another profile. Verify
   permission messages, key-period math and separate completed-day UTC history.
   Never put the keys in this checklist or a public issue.
4. Move/resize the widget at 100%, 150% and 200% physical scaling. Switch monitors,
   unplug one, vary taskbar position, lock/unlock the widget and restart. All controls
   remain reachable. Tab, arrow keys, Enter and Space must reach the controls.
5. Enable startup, log out/in, and verify background launch without opening the
   dashboard. Turn startup off. Check notifications against synthetic transitions
   and Windows notification settings; do not spend tokens to force alerts.
6. Suspend/resume and run overnight with an active CLI. Observe refresh cadence,
   repeated alerts, responsiveness and memory/handle trends. Record cold and warm
   scans separately.
7. Upgrade an existing install and restart. Verify cursor/history/settings retention.
   Uninstall: app files and its startup entry disappear, user data remains. Confirm
   installer/ZIP contents against the file manifest and release SHA-256 list.

Store only counts, version/build numbers, timing and non-sensitive pass/fail
results as evidence. A stable v1.0 requires the pending gates to be resolved.

## Reproduce isolated Sandbox checks

Run from the repository root on a Windows host with Windows Sandbox and `wsb.exe`.
Use a fresh output directory for every run. Packages must already exist under
`artifacts/release`; an optional beta.1 setup adds the upgrade scenario.

```powershell
./scripts/sandbox-test.ps1 -Output artifacts/sandbox-check -Version 0.3.0-beta.2
./scripts/sandbox-test.ps1 -Output artifacts/startup-check -Version 0.3.0-beta.2 -Scenario startup
```

The lifecycle guest writes `sandbox-result.json` and shuts down. The startup
scenario first writes `startup-stage.json` with `stage: ready-for-logoff`. Wait
for that stage and check there is no failure report before the next commands.
Use exactly the ID generated in that fresh run; these commands sign out only
the disposable guest, never the host:

```powershell
$sandboxId = (Get-Content artifacts/startup-check/sandbox-id.txt -Raw).Trim()
wsb.exe exec --id $sandboxId --command 'shutdown.exe /l' --run-as ExistingLogin --raw
Start-Process wsb.exe -ArgumentList @('connect','--id',$sandboxId) -WindowStyle Hidden
```

The new guest logon runs a verifier through RunOnce. It checks a different token
authentication ID, the app started through HKCU Run with paths containing spaces,
database initialization and no visible main window. It writes `startup-result.json`
and shuts down. `wsb.exe list --raw` can confirm the guest has stopped. Do not
substitute a host sign-out or modify host startup settings for this procedure.
