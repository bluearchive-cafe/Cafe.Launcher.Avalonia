# Desktop Launcher / Updater Profile

Use as a baseline for cross-platform desktop launchers, installers, patchers, and update clients.

```yaml
risk_weights:
  download_integrity: critical
  filesystem_safety: critical
  release_supply_chain: critical
  update_recovery: critical
  cross_platform_behavior: high
  testing_determinism: high
  ui_thread_performance: high
  persistence_and_migration: high
  architecture_boundaries: high
  localization_contracts: medium
  generic_code_smells: low
```

## High-value questions

- Can remote metadata escape intended filesystem roots?
- Are interrupted/resumed downloads recoverable and verified?
- Is validation duplicated in a way that materially increases IO?
- Can CI/release dependencies change without review?
- Are installer/uninstaller privilege boundaries safe?
- Are platform-specific behaviors covered or explicitly gated?
- Can shutdown/settings persistence fail silently?
- Does image decoding or filesystem work block the UI thread?
- Are async/headless tests bounded and deterministic?
- Are application-shell responsibilities consistent with documented feature boundaries?

Generic smell hunting should not outrank these risks.
