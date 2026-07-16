# Ubiquitous Language

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

## Proxy modes

| Mode | Definition | Simplified Chinese | Traditional Chinese | Japanese | Aliases to avoid |
| --- | --- | --- | --- | --- | --- |
| **Automatic system proxy** | The platform proxy selected through the runtime's automatic system detection. | 自动检测系统代理 | 自動偵測系統代理 | システムプロキシを自動検出 | 跟随系统、默认网络行为 |
| **Direct connection** | A connection that explicitly bypasses every proxy. | 直连（不使用代理） | 直連（不使用代理） | 直接接続（プロキシなし） | 直连、直接 |
| **Configured system proxy** | The proxy explicitly configured in operating-system settings. | 已配置的系统代理 | 已設定的系統代理 | 設定済みシステムプロキシ | 系统代理、システムプロキシ |

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

## Example dialogue

> **Developer:** “Should **launch verification** use the local or remote **manifest**?”
>
> **Domain expert:** “It uses the mode selected by the user. **Repair** always checks the **game installation** against the selected **download source**.”
>
> **Developer:** “When is the **Resource Panel** available?”
>
> **Domain expert:** “It is available with the **Cafe download source**, which provides the managed **localized resources**.”

## Flagged ambiguities

- “Download line” and “CDN line” referred to a **download source**; use **download source** because the setting selects a provider, not a network route.
- “Manifest,” “list,” and “file list” referred to the same domain object; use **manifest**, translated as “文件清单” and “マニフェスト.”
- “Resource Control Panel” and “Resource Panel” referred to the same interface; use **Resource Panel**.
- “Validation,” “verification,” and “check” overlapped in launch-related copy; use **launch verification** for the user-configurable pre-launch operation.
- “活动” and “活動” were used for **Banner**, but they mean an event; use “横幅” and “橫幅”.
- “跟随系统” and the unqualified “系统代理” made two proxy modes appear equivalent; use **Automatic system proxy** and **Configured system proxy** to expose the actual distinction.
- “汉化管理,” “中文化管理,” and “中国語化設定” named the **Resource Panel** after one resource type; use the canonical panel name and reserve localization wording for **localized resources**.
