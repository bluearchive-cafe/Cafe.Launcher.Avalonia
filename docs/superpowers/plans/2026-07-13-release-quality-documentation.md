# Release, Quality, and Documentation Implementation Plan

**Goal:** Remove the non-verifiable updater checksum contract, make the tested quality gate explicit, and align all published launcher facts with the implementation.

**Architecture:** The update hand-off remains a browser action, so the release contract exposes only metadata that the desktop can enforce: name, GitHub release URL, and size. A root test script becomes the common local/CI test interface. Documentation retains narrative guidance but lists product facts defined by the desktop implementation.

## Tasks

1. Add failing desktop tests that reject non-GitHub release URLs and no longer expect a checksum field; remove the unused field from the Worker and desktop contract.
2. Add a root `test.ps1` that invokes both exact test projects; use it from build and release CI, and run `coverage.ps1` in build CI.
3. Align `cafe-docs` and the release README with five settings categories, four languages, managed uninstall, current version, current proxy choices, and current help links.
4. Keep the existing game-operation interface stable; record its planned deepening separately because changing it is not required for the verified defects above.

## Verification

- `dotnet test` via the root test interface exercises both test projects.
- `dotnet build .\\Cafe.Launcher.Avalonia.csproj -c Debug --no-restore` reports zero warnings and errors.
- `npx tsc --noEmit` verifies the Worker.
- `npm run docs:build` verifies the documentation site.
