# CLAUDE.md

Guidance for Claude Code in this repository. The root `AGENTS.md` is the authoritative, detailed project guide (architecture, integrations, storage, theming, limitations); read it before non-trivial changes and verify claims against source. `docs/README.md` has the layout, harness list and feature status.

## What this is

MochiDock: a Windows WPF "dynamic island" overlay (.NET 8, `net8.0-windows10.0.19041.0`). Source is in `MochiDock/`, `MochiDock.Core/`, `MochiDock.OutlookHelper/` and `MochiDock.Tests/`. Preserve the `MochiDock` storage, registry and mutex identities. Oyenza Studio (a local checkout outside this repository) is a **read-only reference project**: never modify it or apply its docs here. Code or designs ported from it, or from any other project, must be credited in `THIRD-PARTY-NOTICES.txt`.

## Commands

Run from the repository root.

```powershell
dotnet build MochiDock/MochiDock.csproj -c Release            # verify compilation
dotnet test MochiDock.Tests/MochiDock.Tests.csproj -c Release  # all tests
dotnet test MochiDock.Tests/MochiDock.Tests.csproj -c Release --filter "FullyQualifiedName~SomeName"  # one test/class
scripts\build_and_publish.bat                                            # portable publish (elevates, never launches the app)
scripts\build_installer.bat                                              # self-contained installer via Inno Setup
git diff --check                                                 # documentation-only changes
```

- Code changes: build before finishing and run tests for affected logic. Report compilation separately from manual runtime verification.
- Documentation-only or read-only review tasks: do not build, publish or run generators.
- Render harnesses (`tools/verify/verify-*.ps1`, listed in `docs/README.md`) check real controls without launching the app. Run them after the build finishes, one at a time (loaded DLLs lock output).
- **Never launch `MochiDock.exe`.** It requires Administrator and enforces a global single-instance mutex. Leave runtime checks to the user and do not claim checks you did not perform.

## Conventions

- Comments and docs are in English. Keep `AGENTS.md` and `docs/README.md` consistent with verified behavior when changing features.
- Update UI properties and bound collections on their owning dispatcher; respect COM/WinRT apartment needs.
- New async work needs ownership, cancellation and stale-result handling; stop timers, listeners and processes on shutdown.
- Use theme `DynamicResource` brushes and shared typography styles, not hardcoded colors; check both themes (Default, Matcha).
- Distinguish measured, estimated and unavailable telemetry. CPU temperature is an estimate and display-only.
- Settings with no runtime consumer are removed; old settings JSON with removed keys must still load.
- Tests that assert visual structure rely on exact shapes (for example the FX chart canvas must keep exactly 3 children).
- Never add attribution lines to commits or pull requests.
