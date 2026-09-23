# Ubiquitous Language

界面与文案的规范术语、四语言译法和翻译规则。安装生命周期与诊断领域概念（本地安装状态、损坏安装状态、崩溃快照等）定义在 [CONTEXT.md](CONTEXT.md)；工程结构见 [AGENTS.md](AGENTS.md)。

## Launcher and game files

| Term | Definition | Aliases to avoid |
| --- | --- | --- |
| **Launcher** | The Cafe Launcher desktop application. | Client |
| **Game installation** | The installed Blue Archive game and its tracked local state. | Game files |
| **Manifest** | The file list and metadata used to validate, repair, update, or uninstall a game installation. | List, config |
| **Launch verification** | The pre-launch integrity check configured by the user. | Launch check, validation |
| **Repair** | The operation that compares the game installation with a manifest and restores missing or damaged files. | Fix, recovery |

## Downloads and localized resources

| Term | Definition | Aliases to avoid |
| --- | --- | --- |
| **Download source** | The selected provider of game patch files and manifests. | Download line, CDN line |
| **Official download source** | The download source operated for the official launcher. | Official line |
| **Cafe download source** | The download source operated by BlueArchive.Cafe. | Cafe line |
| **Resource Panel** | The launcher interface for managing localized game resources for a UID. | Resource Control Panel |
| **Localized resources** | Game text, voice, image, and video resources managed through the Resource Panel. | Translation files |

## Launcher updates

| Term | Definition | Aliases to avoid |
| --- | --- | --- |
| **Update channel** | The release track the launcher follows when checking for its own updates. | Update frequency, channel setting |
| **Stable** | The update channel that follows releases published without a prerelease tag. | Production build |
| **Beta** | The update channel that also follows prerelease builds. | Preview, nightly |
| **Launcher self-update** | The Windows flow that downloads the matching release package, verifies it against the release `SHA256SUMS`, and replaces the installation from an independent helper after the launcher exits. | Auto update, in-app upgrade |
| **Restart to Update** | The action that hands a verified package to the updater and exits the launcher. | Update now, apply update |
| **Open Release Page** | The fallback action that opens the release page in the browser when no verifiable package is available. | Manual download |

## Canonical translations

| English | Simplified Chinese | Traditional Chinese | Japanese |
| --- | --- | --- | --- |
| Launcher | 启动器 | 啟動器 | ランチャー |
| Manifest | 文件清单 | 檔案清單 | マニフェスト |
| Launch verification | 启动校验 | 啟動校驗 | 起動チェック |
| Download source | 下载源 | 下載來源 | ダウンロードソース |
| Remote | 远程 | 遠端 | リモート |
| Resource Panel | 资源面板 | 資源面板 | リソースパネル |
| Localized resources | 本地化资源 | 本地化資源 | ローカライズリソース |
| Banner | 横幅 | 橫幅 | バナー |
| Fatal | 致命 | 致命 | 致命的 |
| Update channel | 更新通道 | 更新頻道 | 更新チャンネル |
| Stable | 稳定版 | 穩定版 | 安定版 |
| Beta | 测试版 | 測試版 | ベータ版 |
| Restart to Update | 重启以更新 | 重啟以更新 | 再起動して更新 |
| View Update | 查看更新 | 查看更新 | 更新を見る |
| Open Release Page | 前往发布页 | 前往發布頁 | リリースページを開く |
| Game starting | 游戏启动中 | 遊戲啟動中 | ゲーム起動中 |
| Game running | 游戏运行中 | 遊戲執行中 | ゲーム実行中 |
| Game exited | 游戏已退出 | 遊戲已結束 | ゲームが終了しました |

## Proxy modes

| Mode | Definition | Simplified Chinese | Traditional Chinese | Japanese | Aliases to avoid |
| --- | --- | --- | --- | --- | --- |
| **Automatic system proxy** | The launcher's default network behavior, backed by the runtime's system proxy selection. | 自动检测系统代理 | 自動偵測系統代理 | システムプロキシを自動検出 | 跟随系统 |
| **Direct connection** | A connection that explicitly bypasses every proxy. | 直连（不使用代理） | 直連（不使用代理） | 直接接続（プロキシなし） | 直连、直接 |
| **System proxy (configured first)** | The explicitly configured operating-system proxy, falling back to automatic system proxy detection when no explicit proxy exists. | 系统代理（优先使用显式配置） | 系統代理（優先使用明確設定） | システムプロキシ（明示設定を優先） | 已配置的系统代理、設定済みシステムプロキシ |

## Translation rules

- Prefer the natural localized name in user-facing copy; do not append the English source term mechanically.
- A first explanation or a dangerous confirmation may retain `Manifest` in parentheses after “文件清单” or “檔案清單”; short labels use only the localized name. Japanese uses “マニフェスト” without a repeated English term.
- Automatic language selection uses the localized `languageAuto` value; never build it by appending a fixed English `(Auto)` suffix.
- `banner` and `banners` mean **Banner**, not an event or activity. Chinese copy uses “横幅” or “橫幅”.
- Log filters and log-level settings use the same **Fatal** translation within each language.

## Reserved terms

- Keep `UID`, `CDN`, `API`, `Cafe Launcher`, and file names exactly as supplied; do not translate, recase, or respell them.
- Keep dynamic server-provided content unchanged. These rules apply only to launcher-owned interface copy.

## Relationships

- A **game installation** has one local **manifest**.
- A **download source** supplies remote manifests and game patch files.
- **Launch verification** checks a **game installation** before launch.
- **Repair** restores a **game installation** against the selected **download source**.
- The **Resource Panel** manages **localized resources** when the **Cafe download source** is selected.
- An **update channel** decides which launcher releases are offered; a **launcher self-update** installs the offered release only after verifying it against the release checksum manifest.
- The stable update channel (“稳定版”) and the 正式版 label on the download badges name the same release track; keep each label where it ships rather than unifying them.
- The game **session status** line reports the state of a launched **game installation** (starting, running, exited), which is separate from **launch verification**.

## Flagged ambiguities

- “Download line” and “CDN line” referred to a **download source**; use **download source** because the setting selects a provider, not a network route.
- “Manifest,” “list,” and “file list” referred to the same domain object; use **manifest**, translated as “文件清单” and “マニフェスト.”
- “Resource Control Panel” and “Resource Panel” referred to the same interface; use **Resource Panel**.
- “Validation,” “verification,” and “check” overlapped in launch-related copy; use **launch verification** for the user-configurable pre-launch operation.
- “活动” and “活動” were used for **Banner**, but they mean an event; use “横幅” and “橫幅”.
- “跟随系统” and the unqualified “系统代理” made two proxy modes appear equivalent; use **Automatic system proxy** and **System proxy (configured first)** to expose the explicit-configuration priority and automatic fallback.
- “汉化管理,” “中文化管理,” and “中国語化設定” named the **Resource Panel** after one resource type; use the canonical panel name and reserve localization wording for **localized resources**.
