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

| English | Simplified Chinese | Japanese |
| --- | --- | --- |
| Launcher | 启动器 | ランチャー |
| Manifest | 文件清单 | マニフェスト |
| Launch verification | 启动校验 | 起動チェック |
| Download source | 下载源 | ダウンロードソース |
| Remote | 远程 | リモート |
| Resource Panel | 资源面板 | リソースパネル |
| Localized resources | 本地化资源 | ローカライズリソース |

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
