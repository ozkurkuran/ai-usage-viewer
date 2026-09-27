# Contributing

Use Windows x64 and the .NET SDK in `global.json`. The application has no server
or real-account requirement for builds/tests. Restore through the checked-in
NuGet configuration, then run:

```powershell
dotnet restore AiUsageViewer.slnx
dotnet build AiUsageViewer.slnx -c Release --disable-build-servers -m:1 -p:UseSharedCompilation=false
./scripts/test.ps1
dotnet run --project src/AiUsageViewer.App -c Release -- --demo
```

Changes to token identity, cumulative counters, time ranges, pricing or provider
failure behavior should include a fixture demonstrating the failure and expected
result. Fixtures must be synthetic: never commit actual JSONL conversations,
account emails, credentials, local databases or screenshots of private usage.

Keep the dependency direction `Core <- Application <- Infrastructure <- App`.
WPF is restricted to App. Provider capabilities are explicit; an absent field is
unknown, not a fabricated zero. Do not combine local events and account summaries.

For a provider, implement `IQuotaProvider` and/or `IActivityProvider`, document the
read-only source and permissions in `docs/providers`, and test 401/403, 429 with
Retry-After, timeout, malformed data and partial success. Use the existing secret
store; secrets must not enter model types, logs, SQL, exports or error messages.
Do not add automatic login, refresh-token exchange, reset redemption, paid model
requests or provider-account switching to monitoring code.

Prices require exact model IDs, an authoritative source URL, effective date and
explicit tariff assumptions. Keep cache token buckets disjoint and reasoning a
subset of output. Add a schema migration before changing persistent tables.

Run `scripts/package.ps1` with Inno Setup's `ISCC.exe` to build both distributions.
Validate the portable executable with an empty data directory and preserve all
license files. Release checks and current gaps are tracked in
`docs/IMPLEMENTATION_STATUS.md` and `docs/WINDOWS_QA.md`.
