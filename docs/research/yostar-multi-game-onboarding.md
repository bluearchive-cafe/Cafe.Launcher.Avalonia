# 接入其他 Yostar 游戏：官方双样本对比与方案探索

> 样本日期：2026-09-28。官方侧为两份 Windows 32 位 Electron 发行包解包结果，Cafe 侧为本仓库当前 `main`。
> 本文是**探索**，不是实现计划；它记录证据、划出方案空间，并把需要裁决的点列给 ADR。
> §1–§5、§7 是当时的证据与判断；**§6 的 S0 已于同日落地**（见
> [ADR-044](../design/adr/ADR-044-游戏与产品身份由档案声明并注入.md)），S1/S2 仍是提案。
> 静态对比结论不替代实机互操作验证。
> 2026-09-30 的重新核对与执行顺序见 [官方双样本参考后的后续计划](../design/yostar-reference-follow-up-plan-2026-09-30.md)，
> 特别补充了旧配置缺少 `params` 的协议变体与小范围共享试点的前置验收。

## 1. 样本与方法

| 样本 | 位置（仓库外） | 版本 | 包名 / 作者 |
| --- | --- | --- | --- |
| StellaSora CN 官方启动器 | `E:\Repos\_StellaSora_Gamelauncher\cn_app-32` | 1.3.0 | `StellaSora_CN_Gamelauncher` / YOSTAR（HONG KONG）LIMITED |
| Blue Archive JP 官方启动器 | `E:\Repos\BlueArchive_JP_Launcher\app-32_v1.7.2` | 1.7.2 | `BlueArchive_JP_Gamelauncher` / Yostar, Inc. |

Stella 样本的 `resources/app` 是已解包目录；BA 样本是 `resources/app.asar`，用
`asar extract`（`@electron/asar` 4.3.0）解到临时目录后对比。方法：逐文件 SHA-256 比对 +
`git diff --no-index` 行级差异 + 顺序无关的行多重集比对（用于排除模块拼接顺序造成的伪差异）。

BA 侧与本仓库的逐项行为对比已另有专文（[official-launcher-v1.7.2-comparison.md](./official-launcher-v1.7.2-comparison.md)）；
本文改用「两份官方包互比」，回答的是另一个问题：**Yostar 自己是怎么支持多个游戏的。**

## 2. 结论一：两个官方启动器是同一产品的两个构建

`resources/app` 是同一套源码树（`out/main` + `out/preload` + `out/renderer` + `shared` + `node_modules/@launcher/utils`）。
对应文件逐个比对的结果：

| 区域 | 结果 |
| --- | --- |
| `shared/constant.ts`、`shared/types.ts` | **逐字节相同** |
| `out/main/constant-*.js`、`types-*.js`、`config-*.js`、`index-cf0fb39f.js` | **逐字节相同** |
| `out/main/logReport-*.js` | **逐字节相同** |
| `out/preload/index.js`、`out/preload/update.js` | **逐字节相同** |
| `out/main/index.js`（主进程，739 714 / 740 833 字节） | 124 行差异；顺序无关差异 110 行 |
| `out/main/index-*.js`（下载 worker，18 999 / 19 097 行） | 720 行差异；**顺序无关差异 292 行（约 1.5%）** |
| `out/renderer/assets/main-*.js`（渲染层，约 605 KB） | 167 行差异，其中绝大多数是新增一个字段后 v-dom `_hoisted_*` 变量后缀整体位移 |
| `out/renderer/assets/*.css` | 一个文件差 40 行（仅 `data-v-` scope 哈希），另一个只差一行背景图 URL |
| `out/renderer/assets/{Menu,Progress,Download,ProgressBar,GameControl,loading1,update,windowDrag}.*` | 逐个只差 `data-v-` scope 哈希或内容哈希的 PNG 文件名 |
| `node_modules/@launcher/utils/src/*`（8 个源文件） | **7 个逐字节相同**，仅 `manifest.ts` 差 1 行 |
| `package.json`、`app-update.yml` | 有意不同（产品标识与更新源） |

`@launcher/utils/src/manifest.ts` 的唯一差异是 BA 1.7.2 比 Stella 1.3.0 多一个可选字段：

```diff
 export interface IGameConfig {
   tag: string;
   version: string;
   name: string;
+  params?: string[];
 }
```

主进程 `out/main/index.js` 的全部游戏差异（去掉版本漂移后）就是一张配置表：

| 配置槽 | Stella CN 1.3.0 | BA JP 1.7.2 |
| --- | --- | --- |
| 游戏标识（`gameId` / `GAME_DIR_NAME` / `gameTag`，同一值的多处使用） | `StellaSora_CN` | `BlueArchive_JP` |
| API `baseURL` | `https://launcher-api.yostar.net` | `https://api-launcher-jp.yo-star.com` |
| `getAuthHeader` 的 `salt` | `872550AD…BE4B` | `DE7108E9…7869` |
| 主进程语言 | `zh_CN` | `ja` |
| 托盘提示 | `星塔旅人` | `ブルアカ` |
| `setAppUserModelId` 后缀 | `…StellaSora_CN` | `…BlueArchive_JP` |
| 更新源（`app-update.yml`） | `…/pubplat/game-launcher-cn/install_pkg/launcher/StellaSora_CN/` | `…/install_pkg/game_launcher/BlueArchive_JP/` |
| 卸载后注册表清理命令 | `reg delete HKCU\Software\RoamingStar\StellaSora` | 无 |
| 日志上报（SLS）project / store / endpoint | `game-launcher-cn` / `game-launcher-cn` / `cn-shanghai` | `yostar-oversea-launcher-logging` / `oversea-launcher` / `ap-southeast-1` |
| 包内相对资源前缀 | `prod/StellaSora_CN/…` | `/prod/BlueArchive_JP/launcher_background_img/` |

**与游戏无关、纯属版本漂移的差异**只有两条，都是 BA 1.7.2 后来加的，与「对接哪个游戏」无关：
带参数启动（`params` → `execFile` + 取系统环境）以及移除卸载后的注册表清理块。

> **结论：官方对「N 个游戏」的答案是「一套代码 + 构建期游戏常量」，不是运行期多游戏切换。**
> 游戏档案的字段集就是上表这十来个槽位；官方没有任何运行期「选择游戏」的界面或数据模型。

## 3. 结论二：协议层完全共享，且本项目已经落在共享的那一半上

从两份官方包可以直接读出**逐游戏不变**的协议面：

- **游戏配置端点契约相同**：`GET /api/launcher/game/config` 返回
  `game_latest_version` / `game_latest_file_path` / `game_start_exe_name`（1.7.x 起另加 `game_start_params`）。
  两个官方渲染层消费这段响应的代码逐行一致（只有 BA 多读了 `game_start_params`）。
- **远端清单格式相同**：主进程 `gameDownloadList` / `gameRepairList` 都按 `res.file`（单数键）取文件列表、
  按 `res.source` 取 CDN 前缀——即同一份 `{ source, file: [{ path, hash, size }] }`。
- **本地安装状态格式相同**：下载 worker 的 `insertManifestFile` 在两个包里都是
  `{ name: gameTag, version, basis, vc, files: [{ …file, vc }] }`，`insertGameConfigFile` 都是 `{ …config, vc }`；
  `vc = base64(md5(Object.values(obj).join(";")))`。
- **下载 worker 与游戏无关**：两份 worker 全文都不含 `StellaSora_CN` / `BlueArchive_JP` 字面量，
  游戏名由主进程经 `options.gameTag` 传入。
- **认证头算法相同**：`sign = md5(JSON.stringify(head) + body + salt)`，`head = { game_tag, time, version }`，
  两包只在 `salt` 与 `game_tag` 取值上不同。

本项目已经实现了同一批协议（[LocalGameContracts.cs](../../src/Cafe.Launcher.Core/Models/LocalGameContracts.cs)
的 `RemoteManifest{source,file}`、`LocalManifest{name,version,basis,vc,files}`、
`GameLauncherConfig{tag,name,params,version,vc}` 与官方逐字段对应；
[AuthorizationHeaderFactory.cs](../../src/Cafe.Launcher.Core/Services/Auth/AuthorizationHeaderFactory.cs)
与 [OfficialHashService.cs](../../src/Cafe.Launcher.Core/Services/OfficialHashService.cs) 有固定向量测试）。

同时，`E:\Repos\StellaSora.Launcher.Avalonia`（约 2 700 行生产代码）已经把同一批知识**又实现了一遍**
（`VcHash` / `YostarAuthorization` / `YostarApiClient` / `InstallationStateModels` / `GamePathValidator` /
`RemoteHttpTransport` / `InstallationPlanner` / `InstallationCommitter` / `GameProcessGate` / `Uninstaller`）。
于是同一份协议目前有 **三份 .NET 实现**（Cafe、Stella、以及本文对比的两份官方 JS），而官方只有一份。

## 4. 本项目当前的耦合面（实测）

统计口径：`src/`、`tests/` 下全部 `.cs` / `.axaml`，排除 `bin`/`obj`。

### 4.1 每游戏（game profile）——真正随游戏变化的部分

下表是 S0 落地**之前**的形状（S0 的收口见 §6）。「原位置」一列的文件此后已由
[LauncherProfiles.cs](../../src/Cafe.Launcher.Core/Constants/LauncherProfiles.cs) 取代，因此不再作为链接。

| 耦合点 | 原位置（S0 前） | 形态 |
| --- | --- | --- |
| 游戏标识、目录名 | `Constants/GamePaths.cs` 的 `GameTag` / `GameFolderName` | 编译期常量 |
| 游戏客户端可执行名、启动脚本名 | 同上 `GameExecutableFileName` / `GameStartScriptFileName` | 编译期常量 |
| API 基址、salt、签名版本号、官方包主机 | `Constants/ApiConfig.cs` 的 `ApiBaseUrl` / `AuthorizationSalt` / `YostarAuthorizationVersion` / `OfficialPackageHost` | 编译期常量 |
| 包内相对资源前缀 | [LauncherApiClient.cs](../../src/Cafe.Launcher.Core/Services/LauncherApiClient.cs) `ResolveLauncherBackgroundUrl` 里的 `/prod/BlueArchive_JP/launcher_background_img/` | 方法内字面量 |
| 运行时身份（UMU GAMEID / 兼容前缀命名空间） | `Services/GameRuntime/GameRuntimeIds.cs` | 单个常量 |
| 汉化下载源主机 | [PatchUrlGroupService.cs](../../src/Cafe.Launcher.Core/Services/PatchUrlGroupService.cs) `launcher-pkg-ba-jp.bluearchive.cafe` | 私有常量（游戏 × 品牌混合） |
| 官网链接 | `Constants/LauncherConstants.cs` 的 `OfficialGameWebsiteUrl` | 编译期常量 |
| 界面文案 | [LauncherStrings.resx](../../src/Cafe.Launcher.UI/Resources/LauncherStrings.resx) 中 `gameDisplayName`、`setupWizardGamePathHint` 等 | 资源键 |

### 4.2 每产品（product profile）——随「这是谁的启动器」变化，与游戏并非一一对应

`LauncherConstants.ProductName`（并决定数据根 `%LOCALAPPDATA%\Cafe Launcher\`，见
[LauncherDataRoot.cs](../../src/Cafe.Launcher.Core/Services/LauncherDataRoot.cs)）、全部 cafe 链接与 GitHub 仓库 slug、
`ApiConfig` 里 Cafe 自己的发行 API 与资源面板 API、资源面板整条竖切
（[ResourcePanelUidService.cs](../../src/Cafe.Launcher.UI/Features/ResourcePanel/ResourcePanelUidService.cs) 里 `bluearchive.cafe`
与 `LocalLow\YostarJP\BlueArchive` 路径）、Inno 安装器的
`AppId` / `AppName` / `AppPublisher` / `EXECUTABLE_NAME` / `OutputBaseFilename`（[Cafe.Launcher.iss](../../installer/windows/Cafe.Launcher.iss)）、
自更新 helper 协议与 `.cafe-launcher-install` 标记、`PRIVACY.md` 与发布说明。

### 4.3 已经是数据驱动的部分（接入新游戏时**不需要**改）

- 进程名字家族 [GameProcessNames.cs](../../src/Cafe.Launcher.Core/Services/GameRuntime/GameProcessNames.cs)：
  从 `game-launcher-config.json` 的 `name` + `params` 反推，源码里只有注释提到 BA，行为里没有游戏字面量。
- 兼容前缀命名空间 `GameCompatibilityPaths`：按 `gameId` 分目录。
- 清单差异、下载状态机、`.tmp`/Range/CRC64、SSRF 校验、代理租约、安装状态提交、
  vc 校验、卸载范围：均不引用游戏名。
- 规模分布：Core 12 256 行、UI 28 889 行、宿主 1 750 行、Updater.Core 619 行；资源 594 键 × 4 语言，
  其中**真正含游戏或品牌字样的只有 18 个键，含游戏的只有 6 个**。

**耦合面的真实形状**：常量少（十来个），但散落在四个静态类里、没有注入接缝，
且「每游戏」与「每产品」两类值混在同一批类型里（`ApiConfig` 同时放官方 API 与 Cafe GitHub slug；
`GamePaths` 同时放游戏目录名与 `settings.json` 这类启动器通用文件名）。

## 5. 方案空间

| 方案 | 形状 | 主要收益 | 主要代价 |
| --- | --- | --- | --- |
| **A 运行期多游戏单应用** | 一份 Cafe Launcher 内含游戏切换器；`GameProfile` 注入，数据根按游戏分目录 | 机械层 100% 复用，一次更新覆盖全部游戏 | 产品身份与发行身份变成运行期分支：About/法务/公告/汉化源/资源面板/快捷方式/发布说明都按游戏分叉；BA 汉化品牌与「Cafe Launcher」这个名字绑死在一个二进制里；现有用户设置与数据根要迁移 |
| **B 单仓库 + 构建期游戏档案** | 同一代码库，`profiles/<game>` 在构建/打包期解析出该游戏的发行版；无运行期切换 | 与官方同构；零运行期分支；一套代码、多条发行通道；档案是新游戏的唯一输入 | 需要先把常量拆成档案 + 通用两层；品牌仍需参数化（「Cafe Launcher 的 StellaSora 版」这一产品是否成立要先定） |
| **C 抽共享协议库 + 每游戏宿主** | `Cafe.Launcher.Core` 里的游戏无关一半抽成独立程序集/仓库，各游戏宿主只提供档案与表现层 | 每个游戏保住自己的品牌与发行通道；最贵的机械层只维护一份、只测一份；与官方 `@launcher/utils` 的选择同构 | 跨仓库/跨包版本协调；共享面必须是深模块而不是浅接口；程序集命名与拆分契约测试需要同步修订 |
| **D 维持现状（复制知识）** | 每游戏一个独立仓库，复制机械层 | 零耦合，各产品完全独立 | 协议知识静默漂移；官方每次变更要重新取证 N 次；已有第二份实现缺了 Cafe 已经硬化的若干防线（写入边界二次进程门、下载会话检查点、Fake-IP 判定、卸载范围与残留报告） |

关于 A 的一条硬证据：**官方自己不做 A**。两份官方包的产品身份（包名、作者、`AppUserModelId`、
托盘名、更新源、日志 project、卸载清理命令）全部按构建分叉，包内没有任何运行期游戏选择的数据结构。

关于 C 的一条内部依据：[ADR-042](../design/adr/ADR-042-Avalonia启动器按Core与UI程序集分层.md) 曾明确否决
「独立 `Contracts` 或 `Yostar.Launcher.Protocol` 程序集」，理由是**「在确有第二个 .NET 消费方之前，
这只是单消费者的假接缝」**。StellaSora 仓库的存在已经推翻了这个前提条件——但那只是解除否决，
不等于现在就该做；`ADR-042` 的判据（接缝必须有真实消费者）应当以同样的严格程度重新适用一次。

## 6. 建议：分三步走，先做与方案选择无关的那一步

**S0｜把「游戏档案」变成一等公民（✅ 已落地，2026-09-28；无论最后选 B 还是 C 都要做，且不需要新程序集）**

在 `Cafe.Launcher.Core` 内引入了两个值对象（record），静态常量已搬进去，改由组合根注入：

- [YostarGameProfile.cs](../../src/Cafe.Launcher.Core/Models/YostarGameProfile.cs)：`Tag`、`ApiBaseUrl`、
  `AuthorizationSalt`、`AuthorizationVersion`、`OfficialPackageHost`、`PackageAssetPrefix`、
  `RootFolderName`、`GameFolderName`、`GameExecutableFileName`、`GameStartScriptFileName`、
  `RuntimeId`、`OfficialWebsiteUrl`、`CookieLibraryRelativeSegments`。
- [LauncherProductProfile.cs](../../src/Cafe.Launcher.Core/Models/LauncherProductProfile.cs)：`ProductName`（数据根名）、
  发行仓库 slug、汉化源主机、自有服务地址，以及由 slug 派生的发行下载前缀与 GitHub API 地址。

值只写在 [LauncherProfiles.cs](../../src/Cafe.Launcher.Core/Constants/LauncherProfiles.cs)，由
`AddLauncherCore(buildIdentity, dataRoot, gameProfile, productProfile)` 交给容器；`GamePaths` 收窄为
[LauncherPaths.cs](../../src/Cafe.Launcher.Core/Constants/LauncherPaths.cs)（只剩启动器与协议通用文件名），
`ApiConfig` 与 `GameRuntimeIds` 已删除，`LauncherConstants` 只剩与游戏和产品都无关的常量。
`AddLauncherCore` 的两个档案参数是必填的：写错游戏标识不会被本地编译挡住，只会被服务端拒绝，
因此这里刻意不提供静态兜底。

收益独立于后续选择：S0 之前 `ApiConfig` 把官方 API 与 Cafe 自己的 GitHub slug 放在一起，
`GamePaths` 把游戏目录名与 `download_state.json` 放在一起——这是**混合关注点**，不是多游戏需求才暴露的问题。
行为不变：固定向量测试（vc、认证签名、清单字段序）未改，值也未变。

**S1｜抽协议层为独立程序集（让第二个消费者吃掉已写好的实现）**

把 Core 里游戏无关的一半（认证签名、API 包络与端点、wire 模型、vc、CRC64、路径校验、
远端传输与 SSRF、下载状态机与传输接缝、安装状态存储、进程名字家族、运行器与兼容前缀、Updater.Core）
抽成一个不以 `Cafe.` 开头的程序集，由 `Cafe.Launcher.Core` 引用；`StellaSora.Launcher.Avalonia`
改为引用同一份，而不是继续维护第二份实现。

落地前必须先裁决两件仓库级的事：

1. **程序集归属与命名**：`AssemblyNamingContractTests.DeclaredProjectTokens` 断言磁盘上的 `.csproj`
   集合与声明表完全相等（[AssemblyNamingContractTests.cs](../../tests/Cafe.Launcher.Tests/AssemblyNamingContractTests.cs)），
   新增工程必须同时改表；而 ADR-043 的「命名空间指明属主程序集」意味着共享程序集不能叫 `Cafe.Launcher.*`，
   否则它把 Cafe 的产品前缀带给了所有消费者。
2. **共享方式**：同仓库工程引用 / 独立仓库 + git submodule / NuGet 包——三者的发行节奏耦合程度不同，
   需要按「Cafe 与 Stella 是否愿意被同一份 Core 的发布阻塞」来选。

**S2｜真正接入某个游戏时，再在 B 与 C 之间定型**

判据是产品问题而不是技术问题：**要「一个产品支撑多个游戏」，还是「多个产品共用一个内核」？**
若答案是前者，走 B（同一份宿主 + `profiles/<game>`）；若是后者，走 C（共享内核 + 每游戏宿主工程）。
S0 已经把这题的成本压到最低——两种走向都只需要再加一层档案装载。

**明确不建议现在做 A。** 它要求把 BA 汉化品牌、Cafe 资源面板、发布说明、UID/前缀探测
全部降级为条件分支，而这些恰好是本项目区别于官方启动器的用户价值所在；官方在同一条路上选择了
按构建分叉，没有理由相信第三方团队会比官方更需要运行期切换。

## 7. 接入新游戏的取证清单（本次对比归纳出的可复用步骤）

前置：从发行渠道取得该游戏的官方启动器安装包（当前项目做同类研究时使用的即本清单第 1 步的样本形态）。

1. **解包**：`asar extract <resources>\app.asar <临时目录>`；若 `resources\app` 已是目录则直接用。
2. **比对官方样本**：拿本仓库记录的两个已知样本（Stella CN 1.3.0 / BA JP 1.7.2）作为参照，
   逐文件哈希 + `git diff --no-index`。已知样本经此对比已被证明高度一致，因此**差异行本身就是游戏档案**。
   重点看 `out/main/index.js`（配置槽就在这个文件里，且它没有被压缩）、`app-update.yml`、`package.json`。
3. **抄录配置槽**：游戏标识、`GAME_DIR_NAME`、API `baseURL`、`salt`、语言、
   托盘名、`AppUserModelId`、更新源 URL、卸载后注册表命令、日志上报端点、包内资源前缀。
4. **只读探测端点**：`/api/launcher/game/config`（取 `game_latest_version` / `game_latest_file_path` /
   `game_start_exe_name` / `game_start_params`）、`/api/launcher/advanced/game/download/cdn`（主备 CDN 域）、
   `/api/launcher/advanced/config`。鉴权头按第 3 步的 `salt` 与 `game_tag` 生成。
5. **确认清单变体**：远端 `{ source, file[] }` 与本地 `{name,version,basis,vc,files[]}` 是两个已知样本共有的形状，
   但 `source` 前缀与 CDN 域名是每游戏的；不得由已知样本外推。
6. **实机取一次真实安装状态**：读该游戏的 `game-launcher-config.json`（`name` / `params` / `tag`）、
   确认进程名家族（反作弊宿主的同族规则必须实测，不能沿用 BA 的 `_loader_x64` 假设）、
   记下真实安装目录名。未经实机验证的进程名与目录名一律按未知处理。

第 2 步的价值在于：**接入一个新游戏的主要工作是取证，不是写代码**——这正是官方两包对比得出的结论。

## 8. 需要新 ADR 裁决的点

1. ~~游戏档案与产品档案的边界（哪些值属于游戏、哪些属于产品、哪些属于协议）。~~
   → 已由 [ADR-044](../design/adr/ADR-044-游戏与产品身份由档案声明并注入.md) 裁决。
2. 共享内核的程序集名与命名空间前缀，以及它与 ADR-043 产品 token 规则的关系。
3. 共享方式（同仓库 / 独立仓库 / 包）与它对发行节奏、`release.ps1`、安装器身份的影响。
4. 是否接受「运行期多游戏」——若否，明确记下来，避免以后反复（ADR-044 的「被否决的替代方案」已记下当前答案）。
5. 汉化下载源与资源面板是否随内核外提：它们目前既是游戏相关又是品牌相关，是最难被参数化的一块。
   S0 只把游戏相关的一半（运行时身份、Cookie 库相对路径）收进游戏档案，产品域与资源面板竖切未动。

## 9. 验证边界

- 本文的官方侧结论全部来自两份**静态发行包**的构建产物；没有运行官方 EXE、没有连接官方线上 API、
  没有安装或启动任何游戏。两份包之间有版本落差（1.3.0 vs 1.7.2），因此「差异」被拆成
  「游戏配置」与「版本漂移」两类，但两者的归属是**推断**：只有官方源码才能真正分开它们。
- 官方 `node_modules` 中的第三方实现、图片逐像素差异、运行时响应均不在结论内。
- 两份样本是当前项目做同类研究时的取样本，不保证是各游戏的最新版本；服务端配置可使某些入口显示或隐藏。
- 本仓库侧的数字（行数、常量引用次数、资源键计数）为本次实测快照，会随代码演进失效。
- `E:\Repos\StellaSora.Launcher.Avalonia` 仅按源码结构与 README 记录的内容引用，未运行其构建或测试。
