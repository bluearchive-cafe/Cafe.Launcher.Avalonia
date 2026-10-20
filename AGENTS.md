# Repository Guidelines

.NET 10 / Avalonia 12 desktop launcher for Blue Archive (JP). Windows is supported;
macOS and Linux are experimental.

## Before editing

Read [PROJECT_CONVENTIONS.md](PROJECT_CONVENTIONS.md) for implementation rules and
[docs/DEVELOPMENT.md](docs/DEVELOPMENT.md) for commands, validation, dependencies and releases.
These are the authoritative references; this file routes work to them.

| Task | Read before making changes |
| --- | --- |
| Architecture, DI, lifetime or feature boundaries | [Conventions §2](PROJECT_CONVENTIONS.md#2-程序集与命名边界) and [§7](PROJECT_CONVENTIONS.md#7-di-与生命周期) |
| Installation, launch, repair, uninstall, settings or self-update behavior | [Domain contracts](CONTEXT.md) |
| UI, XAML, styles or motion | [Conventions §4](PROJECT_CONVENTIONS.md#4-xaml-与视觉规范), [design specification](docs/design/design-system-spec.md), and the applicable workflow in [.agents/skills/](.agents/skills/) |
| User-facing text or translations | [Conventions §6](PROJECT_CONVENTIONS.md#6-本地化) and [canonical terminology](UBIQUITOUS_LANGUAGE.md) |
| Tests or shared test facilities | [Conventions §8](PROJECT_CONVENTIONS.md#8-测试) |
| Dependencies, packaging, CI or release | [Development guide](docs/DEVELOPMENT.md) and [toolchain table](PROJECT_CONVENTIONS.md#12-工具链与依赖) |
| Audit or accepted risks | [CODEBASE_AUDIT.md](CODEBASE_AUDIT.md), the only audit ledger |
| Changing an established decision | [ADR index](docs/design/adr/README.md) and the relevant decision |

## Working loop

1. Read the relevant contracts above and inspect the affected code and tests.
2. Keep the change within the existing assembly and feature boundaries. Add focused
   regression tests for behavior changes; use the repository's shared test facilities.
3. Run the affected checks from the development guide. Run `pwsh ./verify.ps1` before
   requesting review; report what passed and any verification limits.
4. Update each affected authoritative document in the same change. Update this router
   when architecture or developer workflow changes its entry points.

Use PowerShell 7 (`pwsh`) for repository scripts, including when invoked from Bash.
Tests use Microsoft.Testing.Platform; prefer `test.ps1` so its five-minute hang guard stays
enabled. Builds treat warnings as errors. Build the updater separately when changing it;
`build.ps1` builds only the launcher host graph.

Commit messages use Conventional Commits; the description and body may be Chinese:
`fix(settings): 修复切换语言后法律信息不显示`.
`release.ps1` commits, tags and pushes; run it only for an intended release.
