# 界面术语与翻译 / Ubiquitous Language

本表用于界面、发布说明与中英文用户文档，统一概念与译名。先确定所指概念，再使用对应语言的规范名称；动态服务器文案保留原文。领域行为与状态约束见 [CONTEXT.md](CONTEXT.md)，资源键与生成命令见[开发规范 §6](PROJECT_CONVENTIONS.md#6-本地化)。

使用方法：

1. 在「概念与定义」中确认所指概念，并避开列出的别名。
2. 在「规范译名」中取对应语言的名称；未列出的内部概念不是用户可见术语。
3. 改写文案后按「翻译规则」与「保留词」自查。

## 概念与定义

### 启动器与游戏文件

| Term | Definition | Aliases to avoid |
| --- | --- | --- |
| **Launcher** | The Cafe Launcher desktop application. | Client |
| **Game installation** | The installed Blue Archive game and its tracked local state. | Game files |
| **Manifest** | The file list and metadata used to validate, repair, update, or uninstall a game installation. | List, config |
| **Launch verification** | The pre-launch integrity check configured by the user. | Launch check, validation |
| **Repair** | The operation that compares the game installation with a manifest and restores missing or damaged files. | Fix, recovery |

### Downloads and localized resources

| Term | Definition | Aliases to avoid |
| --- | --- | --- |
| **Download source** | The selected provider of game patch files and manifests. | Download line, CDN line |
| **Official download source** | The download source operated for the official launcher. | Official line |
| **Cafe download source** | The download source operated by BlueArchive.Cafe. | Cafe line |
| **Resource Panel** | The launcher interface for managing localized game resources for a UID. | Resource Control Panel |
| **Chinese-localized resources** | Game text, main-story Chinese voice, and image/video resources selected for a UID through the Resource Panel. | Localized resources, translation files |
| **Localization UID** | The eight-uppercase-letter identifier used for Cafe resource preferences. It is distinct from the numeric in-game account UID. | Account ID, numeric UID |
| **Resource preferences** | The resource switches stored on the Cafe service for a localization UID. | Local resource edits, local patch settings |

### Launcher updates

| Term | Definition | Aliases to avoid |
| --- | --- | --- |
| **Update channel** | The release track the launcher follows when checking for its own updates. | Update frequency, channel setting |
| **Stable** | The update channel that follows releases published without a prerelease tag. | Production build |
| **Beta** | The update channel that also follows prerelease builds. | Preview, nightly |
| **Launcher self-update** | The Windows flow that downloads the matching release package, verifies it against the release `SHA256SUMS`, and replaces the installation from an independent helper after the launcher exits. | Auto update, in-app upgrade |
| **Restart to Update** | The action that hands a verified package to the updater and exits the launcher. | Update now, apply update |
| **View Update** | The action that opens the update notes. | Show changelog |
| **Open Release Page** | The fallback action that opens the release page in the browser when no verifiable package is available. | Manual download |
| **In-app update unavailable** | The state where this host cannot apply an update itself, always with the cause the verdict reported: an unsupported platform, an installation without the Windows update helper, or a release with no verifiable package. The dialog names that cause before download and the primary action is **Open Release Page**. | Not supported, download manually, this device cannot |

### Appearance, status and general terms

| Term | Definition | Aliases to avoid |
| --- | --- | --- |
| **Video background** | A custom video rendered behind launcher surfaces instead of a static wallpaper. | Animated wallpaper |
| **Video background volume** | The persisted video volume from 0 to 100, defaulting to 0. | Media volume |
| **Mute video background** | The independent mute switch that keeps the chosen volume for later. | Disable audio |
| **Remote** | Content loaded over the network, such as a remote wallpaper or notice. | Online |
| **Banner** | A promotional image or news banner; not an event or activity. | Event, activity |
| **Fatal** | The log severity for fatal errors; the same translation applies to log filters and log-level settings. | Critical |
| **Game starting** | The session-status state after launch is requested and before the game is confirmed running. | Launching |
| **Game running** | The session-status state after the game process family is confirmed present. | Playing |
| **Game exited** | The session-status state after the game process family is confirmed absent. | Closed |

### Proxy modes

| Mode | Definition | Aliases to avoid |
| --- | --- | --- |
| **Automatic system proxy** | The launcher's default network behavior, backed by the runtime's system proxy selection. | 跟随系统 |
| **Direct connection** | A connection that explicitly bypasses every proxy. | 直连、直接 |
| **System proxy (configured first)** | The explicitly configured operating-system proxy, falling back to automatic system proxy detection when no explicit proxy exists. | 已配置的系统代理、設定済みシステムプロキシ |

## 规范译名

| English | 简体中文 | 繁體中文 | 日本語 |
| --- | --- | --- | --- |
| Launcher | 启动器 | 啟動器 | ランチャー |
| Video background | 视频背景 | 影片背景 | 動画背景 |
| Video background volume | 视频背景音量 | 影片背景音量 | 動画背景の音量 |
| Mute video background | 视频背景静音 | 影片背景靜音 | 動画背景のミュート |
| Manifest | 文件清单 | 檔案清單 | マニフェスト |
| Launch verification | 启动校验 | 啟動校驗 | 起動チェック |
| Download source | 下载源 | 下載來源 | ダウンロードソース |
| Cafe download source | Cafe 下载源 | Cafe 下載來源 | Cafe ダウンロードソース |
| Official download source | 官方下载源 | 官方下載來源 | 公式ダウンロードソース |
| Remote | 远程 | 遠端 | リモート |
| Resource Panel | 资源面板 | 資源面板 | リソースパネル |
| Chinese-localized resources | 汉化资源 | 中文化資源 | 中国語化リソース |
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
| Automatic system proxy | 自动检测系统代理 | 自動偵測系統代理 | システムプロキシを自動検出 |
| Direct connection | 直连（不使用代理） | 直連（不使用代理） | 直接接続（プロキシなし） |
| System proxy (configured first) | 系统代理（优先使用显式配置） | 系統代理（優先使用明確設定） | システムプロキシ（明示設定を優先） |

## 翻译规则

- Prefer the natural localized name; do not append the English source term mechanically.
- A first explanation or dangerous confirmation may keep `Manifest` in parentheses after “文件清单”/“檔案清單”; short labels use only the localized name. Japanese uses “マニフェスト” without a repeated English term.
- Automatic language selection uses the localized `languageAuto` value; never build it by appending a fixed English `(Auto)` suffix.
- `banner`/`banners` mean **Banner**, not an event or activity. Chinese copy uses “横幅”/“橫幅”.
- Log filters and log-level settings use the same **Fatal** translation within each language.
- Download-source sentence copy uses the full term: “Cafe 下载源 / Cafe 下載來源 / Cafe ダウンロードソース / Cafe download source” and “官方下载源 / 官方下載來源 / 公式ダウンロードソース / Official download source”. Bare display names (“Cafe”, “官方”, “Official”, “公式”) are only for option labels, buttons, and status-bar values. Do not shorten to “Cafe 源 / Cafe 來源 / Cafe ソース” or call it a “Cafe CDN / CDN サーバー”; the setting selects a provider, not a network route.
- The localized-resources term must say what the resources are localized to: zh-Hans “汉化资源”, zh-Hant “中文化資源”, ja “中国語化リソース”, en “Chinese-localized resources”. Do not use bare “本地化资源 / 本地化資源 / ローカライズリソース” in user-facing copy.
- Do not use the middle dot (`·`) to separate ordinary interface copy. Use natural punctuation or separate layout fields; preserve proper names and externally supplied data.

## 保留词与外部内容

- Keep `UID`, `CDN`, `API`, `Cafe Launcher`, and file names exactly as supplied; do not translate, recase, or respell them.
- Keep dynamic server-provided content unchanged. These rules apply only to launcher-owned interface copy.

## 概念关系

- A **game installation** has one local **manifest**; a **download source** supplies remote manifests and patch files.
- **Launch verification** checks an installation before launch; **repair** restores it against the selected download source.
- The **Resource Panel** manages **Chinese-localized resources** when the **Cafe download source** is selected. UID selection is saved locally; resource preferences are stored by the service and do not directly rewrite game files.
- An **update channel** decides which launcher releases are offered. A **launcher self-update** installs one only after verifying it against the release checksum manifest; otherwise the state is **in-app update unavailable** and the exit is **Open Release Page**.
- The stable channel (“稳定版”) and the 正式版 label on download badges name the same release track; keep each label where it ships.
- The game **session status** line (starting/running/exited) is separate from **launch verification**.
