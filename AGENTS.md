# Repository Guidelines

.NET 10 / Avalonia 12 desktop launcher for Blue Archive (JP). Windows is supported;
macOS and Linux are experimental.

## Read first

| Need | Source of truth |
| --- | --- |
| Where any rule lives | [Documentation map](docs/README.md) |
| Implementation rules | [PROJECT_CONVENTIONS.md](PROJECT_CONVENTIONS.md) |
| Commands, validation, dependencies, release | [Development guide](docs/DEVELOPMENT.md) |
| Behavior that must not regress | [Domain contracts](CONTEXT.md) |
| UI wording and translations | [Canonical terminology](UBIQUITOUS_LANGUAGE.md) |

This file routes work; it does not own detailed rules. When a rule changes, update its
owning document and keep this router pointing at it.

## Task router

| Task | Read before changing code |
| --- | --- |
| Architecture, DI, lifetime, feature boundaries | [Conventions §2](PROJECT_CONVENTIONS.md#2-程序集与命名边界), [§7](PROJECT_CONVENTIONS.md#7-di-与生命周期) |
| Install, launch, repair, uninstall, settings, self-update | [Domain contracts](CONTEXT.md) |
| UI, XAML, styles, motion | [Conventions §4](PROJECT_CONVENTIONS.md#4-xaml-与视觉规范), [design specification](docs/design/design-system-spec.md), applicable workflow in [.agents/skills/](.agents/skills/) |
| User-facing text or translations | [Conventions §6](PROJECT_CONVENTIONS.md#6-本地化), [canonical terminology](UBIQUITOUS_LANGUAGE.md) |
| Tests or shared test facilities | [Conventions §8](PROJECT_CONVENTIONS.md#8-测试) |
| Dependencies, packaging, CI, release | [Development guide](docs/DEVELOPMENT.md), [toolchain table](PROJECT_CONVENTIONS.md#12-工具链与依赖) |
| Audit or accepted risks | [CODEBASE_AUDIT.md](CODEBASE_AUDIT.md), the only audit ledger |
| Changing an established decision | [ADR index](docs/design/adr/README.md) and the relevant decision |
| Documentation or user instructions | [Documentation ownership](docs/DEVELOPMENT.md#文档职责); inspect current code before describing behavior, and update both languages in the companion `cafe-docs` workspace |

## Hard constraints

- Keep changes inside the existing assembly and feature boundaries.
- Add focused regression tests for behavior changes; use the shared test facilities.
- Run `pwsh ./verify.ps1` before requesting review; report what passed and what was not verified.
- Builds treat warnings as errors. Build the updater separately; `build.ps1` builds only the launcher host graph.
- Use PowerShell 7 (`pwsh`) for repository scripts, including from Bash. Prefer `test.ps1` so the five-minute hang guard stays enabled.
- Do not add remote telemetry. Diagnostic logs stay local.
- Update the owning document in the same change. For user-visible behavior, also update `docs/cafe-launcher/` and `docs/en/cafe-launcher/` in `cafe-docs`, retaining existing anchors and the legacy `user-guide.md` entry; validate that repository with `npm ci` and `npm run docs:build`.
- Commit with Conventional Commits; the description and body may be Chinese. `release.ps1` commits, tags, and pushes; run it only for an intended release.

## Working loop

1. Read the routed contracts and inspect the affected code and tests.
2. Make the smallest change that satisfies the contract; do not add new rules to this router.
3. Run the affected checks, then `pwsh ./verify.ps1`.
4. Update the owning documents and this router if a workflow entry point changed.

Keep historical ADR and research evidence dated. Generated third-party notices, release
notes, and the audit ledger follow their own maintenance workflows; a prose rewrite does
not establish a new audit conclusion.
