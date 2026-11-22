# BA JP 与 Stella CN 官方协议有限只读取证

采样日期：**2026-11-02**。首次运行 UTC **06:45:23–06:45:30**，Asia/Singapore **14:45:23–14:45:30**；
随后仅补 BA 备源 HEAD/Range，UTC **06:46:53–06:46:57**、Asia/Singapore **14:46:53–14:46:57**；
精确时刻见 [evidence.json](samples/yostar-2026-11-02/evidence.json)。
这是多游戏阶段 1 的当前协议证据，不是完整接入验收或新的仓库审计结论。

两游戏的配置、CDN、清单地址和清单均可读取；两游戏主备 CDN 的入口 EXE HEAD 为 200、
4096 bytes Range 为 206。**共 16 次真实 HTTP 请求全部成功**，其中六次 API 的业务 code 均为 200。
没有改签名、猜清单 URL、执行二进制或写入安装状态。

## 来源与方法

使用仓库外现存的两个官方 Windows Electron 包，未修改、解包到磁盘或执行样本：

| 游戏 | `E:\Repos\_GameLauncher_Samples` 下的来源 | 签名版本 |
| --- | --- | --- |
| Blue Archive JP | `BlueArchive_JP_Launcher/app-32_v1.7.2/resources/app.asar` | `1.7.2` |
| StellaSora CN | `_StellaSora_Gamelauncher/cn_app-32/resources/app.asar` | `1.3.0` |

脚本只读取 ASAR 目录及选定源码成员到内存，核对 `package.json` 版本、主进程 API 基址和
`getAuthHeader` 调用形状。两包的 salt 是官方公开协议常量，只在内存参与签名，不输出或保存值。
每次 API 请求按官方有序 `head={game_tag,time,version}` 生成当前时刻签名；GET body 为空。
这些版本是**已取得的包版本**，没有声称是当日最新官方启动器发行版。

完整 ASAR 与 `package.json`、主进程和共享/worker 成员 SHA-256 见
[official-source-hashes.json](samples/yostar-2026-11-02/official-source-hashes.json)。
主进程 SHA-256：BA 为 `22cb4202ff8af22edc6620ece9e512e59dc60395eb543ca7470c3cb0c3576816`；
Stella 为 `d96b0824409d7ccc78ad59b7facf99beab07e95740605e4eae6f470ecd2a9a38`。
Stella 主进程/worker 与其 [2026-09-30 历史研究](multi-game-launcher-samples-2026-11-02.md#stellasoralauncher可优先迁移的协议与能力适配)
所列源哈希一致；本次重新请求当前 API，没有将旧响应当作当前可用性证据。

## 当前 API 与清单

| 项目 | Blue Archive JP | StellaSora CN |
| --- | --- | --- |
| API 主机 | `api-launcher-jp.yo-star.com` | `launcher-api.yostar.net` |
| 游戏 tag | `BlueArchive_JP` | `StellaSora_CN` |
| 最低/最新版本 | `1.74.0` / `1.74.0` | `2.0.0` / `2.0.0` |
| `game_start_exe_name` | `xldr_BlueArchiveOnline_JP_loader_x64` | `xtlr` |
| `game_start_params` | 存在，`["BlueArchive.exe"]` | **字段缺失** |
| `game_latest_file_path` | `prod/ZIP_TEMP/BlueArchive_JP_TEMP/BlueArchive_JP-1.74.471552-game.zip` | `prod/ZIP_TEMP/StellaSora_CN_TEMP/StellaSora_CN-2.0.0-game.zip` |
| `decompression_size` | `18.5GB` | `20GB` |
| 主 CDN | `launcher-pkg-ba-jp.yo-star.com` | `game-launcher-ss-cn.yostar.net` |
| 备 CDN | `launcher-pkg-ba-jp-bk.yo-star.com` | `game-launcher-ss-cn-bk.yostar.net` |

每游戏依次请求 `/api/launcher/game/config`、`/api/launcher/advanced/game/download/cdn`，
再将本次返回的版本和 file_path URL 编码后传入 `/api/launcher/game/config/json`。
这三个 API 各返回 HTTP 200 / code 200；不附 API 认证头读取其返回的清单 URL，均 HTTP 200：

- [BA 1.74.0 清单](https://launcher-pkg-ba-jp.yo-star.com/zip_online_config_json/1.74.0YhGZXduw.json)
- [Stella 2.0.0 清单](https://game-launcher-ss-cn.yostar.net/zip_online_config_json/2.0.0pjZ0i6YH.json)

两份清单根字段均为 `source,file`；文件条目字段为 `path,hash,size`，hash/size 为十进制字符串。

| 清单摘要 | BA JP | Stella CN |
| --- | --- | --- |
| 原始响应 bytes | 18,378 | 1,194,807 |
| 原始响应 SHA-256 | `7b6426b92a6ed82b2f697b7c93657a1406304049c9df59857afce8e4ce35972c` | `c0506e009291b3f4775006d74149e63ca3119948e2a597095f5f05d34c9a1f86` |
| `source` | `/BlueArchive_JP-1.74.471552-game` | `/StellaSora_CN-2.0.0-game` |
| 文件条目数 | 157 | 9,112 |
| size 合计 bytes | 1,142,879,023 | 23,030,704,609 |
| 脚本条目 | `/run.bat` | 未发现 `.bat/.cmd/.ps1/.sh` |

清单总大小是该次清单文件项合计，不等于完整游戏数据、实际安装占用或 API 的解压提示大小。
格式化小型响应、清单选项与原字节摘要说明见[样本 README](samples/yostar-2026-11-02/README.md)。
Stella API 没有启动参数不等于已验证真实本地 `game-launcher-config.json` 互操作；
字段缺失和空数组仍需独立离线向量与安装状态测试分别覆盖。

## 入口文件有限读取

按官方 worker 已观察的规则拼接 CDN、清单 `source` 与入口 `path`，过滤空段并编码最后一段。
只对 API 指定且清单中精确存在的入口 EXE 分别 HEAD 和 `Range: bytes=0-4095`。

| 入口与响应 | BA 主/备 CDN | Stella 主/备 CDN |
| --- | --- | --- |
| 入口 | `/xldr_BlueArchiveOnline_JP_loader_x64.exe` | `/xtlr.exe` |
| HEAD | 两者 200 | 两者 200 |
| HEAD Content-Length | 两者 12,074,792 | 两者 687,488 |
| 服务端 CRC64 | 两者 `14858856991385480709` | 两者 `16948308644085240114` |
| Range | 两者 206，4096 bytes | 两者 206，4096 bytes |
| Content-Range | 两者 `bytes 0-4095/12074792` | 两者 `bytes 0-4095/687488` |
| 前缀 SHA-256 | 两者 `79eae521175a99ffff096bd0f299b6b7d2c1f3175f119465cef36e021f65223b` | 两者 `2a4844293ad92cf7568d498fdab5d53836dfdd44dd0ae7cb49d02d625128e457` |

主备同游戏的前缀相同，均以 `4D5A` 开头；HEAD 大小和服务端 CRC64 与相应清单条目一致。
**没有读取完整文件或在本地核验整文件 CRC64**，不能由此宣布整包完整性或主备所有内容一致。

第一次脚本预置的 BA 白名单仅含已知主 CDN，本次 API 新返回的备源因此产生两条
`blocked-host-policy` 记录，未发 HTTP。核对该**精确主机名**来自本次官方 CDN 响应后，
加入研究脚本白名单，用 `-ResumeBlockedFileProbes` 只补发这两次读取，结果 HEAD 200 / Range 206。
证据保留前后记录；这不是远端失败，不应把 18 条观察记录误算成 18 次 HTTP 请求。

清单中的其他可执行文件只证明**包内存在**：BA 的 `BlueArchive.exe`、`UnityCrashHandler64.exe`；
Stella 的 `UnityCrashHandler64.exe`、`AntiCheatExpert/ACE-Service64.exe`、`ACE-Setup64.exe`、
`xtlr_Data/Plugins/x86_64/BLPlatform64/PCGamePlatform.exe`、`game_security_protection.exe`。
本轮没有观察进程创建、父子关系或寿命，不能据文件名确定真实进程家族。

## 复现与边界

使用 [Test-YostarProtocol.ps1](../../scripts/Test-YostarProtocol.ps1)，PowerShell 7：

```powershell
# 新采样请使用新的输出目录，保留已归档的带日期证据。
pwsh -NoProfile -File ./scripts/Test-YostarProtocol.ps1 -OutputDirectory ./artifacts/yostar-protocol-probe
# 只核对官方包与源哈希，不联网：
pwsh -NoProfile -File ./scripts/Test-YostarProtocol.ps1 -SkipNetwork -OutputDirectory ./artifacts/yostar-protocol-offline
# 白名单经人工核对调整后，仅重试指定归档中此前被策略阻止的文件读取：
pwsh -NoProfile -File ./scripts/Test-YostarProtocol.ps1 -ResumeBlockedFileProbes -OutputDirectory ./artifacts/yostar-protocol-probe
```

脚本是 opt-in 研究工具；专用短生命周期 HttpClient 不进入应用，也不改变生产
`RemoteHttpTransport` / `HttpClientFactory` 规则。仅允许精确列出的 HTTPS 主机及默认端口，
禁止自动重定向、cookie 和向 CDN 附 API 认证头；API 限 256 KiB、清单限 4 MiB、
游戏文件限 4096 bytes。每次请求和响应 body 读取各有 25 秒预算，不重试、不改签名猜 URL。
若将来失败，保留 HTTP 状态或异常类型、HttpRequestError、SocketErrorCode 的分层原因；
不保存可能携带请求数据的异常消息，也不以在线探测失败阻止本地研发。

本轮脚本源读取、初次在线采样与两次备源补验均执行成功；样本 JSON 可解析，脚本语法已检查。
没有生产 C# 或运行时 HTTP 变更，本子任务未运行 .NET 构建或 `verify.ps1`。
仍未验证：完整安装/修复/续传与整文件 CRC64、官方本地状态互读、游戏真实启动和退出、
进程家族、Cookie/UID、反作弊、Linux/macOS 或其他实机兼容性。本记录不宣布 Stella 已正式支持。
