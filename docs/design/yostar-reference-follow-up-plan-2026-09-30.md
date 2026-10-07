# 官方双样本参考后的后续计划

> 日期：2026-09-30。Cafe 源码基线：`fdefbe0c`。
> 目标：以 Cafe Launcher 的后续工作为主，同时明确 StellaSora 第二个消费者的复用路线。
> 本文是待执行计划；不表示已支持 Stella、不替代实机验收，也不新增审计台账。

建议顺序是：**双样本协议契约 → 官方/Cafe 互操作验收 → 两个消费者共用小范围协议库 → 再决定扩大共享范围与发行形态**。
保留各游戏独立产品与发行通道，暂不引入运行期游戏切换。

## 1. 参考范围与证据

| 对象 | 本轮使用的材料 | 结论边界 |
| --- | --- | --- |
| StellaSora 官方 CN 1.3.0 | `E:\Repos\_StellaSora_Gamelauncher\cn_app-32\resources\app`，第一方 `shared`、`@launcher/utils/src`、主进程和下载 worker | 官方发行包，不是包含测试与 CI 的完整源码仓库 |
| Blue Archive 官方 JP 1.7.2 | `E:\Repos\BlueArchive_JP_Launcher\app-32_v1.7.2\resources\app.asar`，选取第一方文件解至临时目录 | 该历史版本的构建行为，不代表当前服务端或新版启动器 |
| Cafe Launcher | 当前 Core/UI/host 分层、游戏操作、档案、协议测试、既有研究及 ADR | 本轮为静态核对与文档工作 |
| StellaSora Avalonia | 额外只读检查其 README、签名与配置模型、文件清单 | 工作树有未提交改动；只作第二消费者背景，不把 README 的在线验证声明视为本轮实测 |

BA `app.asar` SHA-256：`FD243A514FDCD5EA9CF91C2A4557C560F9023B59D8DD877312410B0C665CA4F7`。
Stella `out/main/index.js` SHA-256：`D96B0824409D7CCC78AD59B7FACF99BEAB07E95740605E4EAE6F470ECD2A9A38`。
BA `out/main/index.js` SHA-256：`22CB4202FF8AF22EDC6620ECE9E512E59DC60395EB543CA7470C3CB0C3576816`。

本轮重新验证：两包 `shared/constant.ts`、`shared/types.ts` 逐字节相同；`@launcher/utils/src` 八个文件中七个相同，
`manifest.ts` 的 BA 版本多了 `params?: string[]`。此前的完整比较见
[双样本接入研究](../research/yostar-multi-game-onboarding.md)与
[BA 官方行为对比](../research/official-launcher-v1.7.2-comparison.md)。

## 2. 已完成的基础与需要更正的假设

| 项目 | 当前状态 | 对计划的影响 |
| --- | --- | --- |
| Core/UI 程序集分层、产品命名 | [ADR-042](adr/ADR-042-Avalonia启动器按Core与UI程序集分层.md)、[ADR-043](adr/ADR-043-程序集与命名空间同名.md) 已落地 | 后续按现有分层推进，新增程序集时同步命名和公开面契约 |
| 游戏身份、产品身份注入 | [ADR-044](adr/ADR-044-游戏与产品身份由档案声明并注入.md) 的 S0 已完成 | 不重复“把硬编码改为档案”；验证档案能覆盖第二游戏的真实差异 |
| 下载恢复、多 CDN、Range、CRC64、写入前运行门 | 当前已有实现和测试 | 不重复建设管线；优先验证协议变体和跨客户端兼容 |
| 标准卸载残留、清单外内容提示 | [ADR-045](adr/ADR-045-卸载与安装的完整性口径必须说出清单外内容.md) 已落地 | 验收已有文案与行为，不把同一问题再列为待开发功能 |
| 官方启动检查 | 两包 `out/main/index.js:19616–19619` 都调用 `checkStat`；修复才调用 `checkHash` | Stella 早期骨架方案的“启动全量 CRC64”叙述有误，不按它改变 Cafe 启动策略 |
| Stella 旧配置兼容 | 官方 worker `index-52e4c114.js:18926–18930` 写 `tag,name,version`，不含 `params` | 是第二游戏接入前的实际协议缺口；不表示当前 BA 功能有缺陷 |

最后一项不能仅靠增加一个游戏档案解决：
[LocalInstallationStateStore](../../src/Cafe.Launcher.Core/Services/LocalInstallationStateStore.cs) 的 `ReadConfigAsync` 要求 `params` 存在，
[OfficialHashService](../../src/Cafe.Launcher.Core/Services/OfficialHashService.cs) 也固定把参数数组放进哈希输入。
缺字段与空数组不是同一个 wire 格式：`tag;name;version` 与 `tag;name;;version` 的哈希不同。
本轮用官方 JS 的算法和一个明确标为合成的向量核对，结果分别为 `LA6qz7kEN2YsdT7SSCt9Tg==`、`CDfk4yFrD/FbU8Tw4J/UQw==`
（`tag=StellaSora_CN`、`name=xtlr`、`version=fixture-version`）；不是来自真实安装的配置。

## 3. 分阶段执行

| 阶段 | 优先级 | 工作范围 | 交付物与完成条件 | 依赖 |
| --- | --- | --- | --- | --- |
| M0 双样本协议契约 | P0，第一批实施 | 双游戏签名向量、配置变体、manifest 字段序、路径与 CDN URL 组合 | 有来源与版本信息的最小 fixtures；官方算法与 .NET 输出交叉一致；既有 BA 向量不变 | 当前档案与协议测试 |
| M1 互操作和发布验收 | P0 | 官方读 Cafe 状态、Cafe 读官方状态；下载续传、提交中断、真实更新 helper | 有明确环境和结果的验收记录，失败转成最小复现或问题；未测项目保持“未验证” | M0；隔离安装副本、对应实机环境 |
| M2 小范围共享试点 | P1 | 签名、vc、wire 模型和序列化；两个 .NET 产品各自通过适配层消费 | 两个消费者引用同一份实现、删除对应重复算法、各自测试通过；有共享发行方式 ADR | M0；第二仓库变更基线明确 |
| M3 按实证扩大复用 | P2 | 先评估 CRC64、清单 diff 和安装状态存储，再评估网络/下载状态机 | 每一批都有两个实际调用方与独立契约；产品策略仍在各自宿主 | M1、M2；真实 Stella 下载路径确认 |
| M4 产品与平台扩展 | P2，择项实施 | Stella 独立发行、Linux 包实机验收、桌面通知 | 独立数据根与更新通道通过隔离验收；平台能力按实测声明 | 前序阶段；对应产品与平台需求 |

### M0：先证明两套协议能被同一个核心正确表达

1. **保留字段是否存在的信息。** 为旧版无 `params`、新版 `params: []`、新版非空 `params` 分别建 fixture；
   读取时在按输入字段顺序校验 vc 后，再映射到统一运行模型。写入格式按已验证的协议变体选择，
   不能靠异常后尝试多个哈希“放宽校验”。是否新增格式枚举由实现时的测试证据决定。
2. **用独立向量验证签名。** 固定时钟，分别使用 BA 1.7.2 与 Stella 1.3.0 的 tag、salt、协议版本；
   预期结果由参考算法产生，不能从被测签名器生成。`head.version` 与 Cafe 自己的构建版本分开，
   不沿用旧骨架“先上报第三方版本观察”的猜测。
3. **隔离品牌功能。** 当前 [LauncherProductProfile](../../src/Cafe.Launcher.Core/Models/LauncherProductProfile.cs)
   必填 Cafe 镜像、资源面板等地址，[YostarGameProfile](../../src/Cafe.Launcher.Core/Models/YostarGameProfile.cs)
   必填 Cookie 路径；共享协议库不继承这些要求。没有对应能力的 Stella 产品不能用 BA 地址或虚假路径占位。
4. **把下载地址当作待核对协议。** 验证 `cdn + source + file.path` 的路径拼接、主备源切换以及路径拒绝；
   使用可追溯抓包或响应样本，不根据游戏标识推导包域名与可执行名。
5. **只读在线采样另行记录。** 执行阶段分别查询 game config、CDN、manifest URL，记录采样时间、
   官方协议版本、状态码和必要结构；网络失败不得直接判定协议错误，线上不稳定样本也不进入默认 CI。

验收至少覆盖：无/空/非空参数、错误 tag、签名版本独立于构建版本、损坏 vc、数字字段格式、
清单文件路径越界、带前导斜杠的正常远端路径。BA 既有序列化顺序与签名固定值全部保持。

### M1：以实际兼容行为作为发布门槛

在副本或明确指定的测试目录中执行；不对现有真实安装做破坏性验证。

| 场景 | 必须观察到的结果 |
| --- | --- |
| 官方状态 → Cafe → 官方 | 两边都能识别版本与安装状态；非必要读取不重写文件；写回后官方仍接受 vc |
| 同版本正常安装 | 不做无意义下载；日志解释差异计数与是否提交状态；安装提示不夸大清单覆盖范围 |
| 缺文件、大小变化、同大小损坏 | 启动检查与修复按各自既定策略响应；同大小损坏可由 CRC64 修复发现 |
| 下载暂停、退出、重启、换源 | 断点可恢复，最终大小和 CRC64 正确，检查点不跨游戏复用 |
| 下载完成前启动游戏 | 写入边界拒绝覆盖；用户能看到原因，不强杀游戏 |
| 两个状态文件替换间中断 | 重启后不能把不一致状态呈现成有效安装；是否补恢复机制按复现决定 |
| 标准与彻底卸载 | 删除范围与确认文案一致，清单外残留和自定义 Prefix 的保留情况如实报告 |
| Windows 安装版与便携自更新 | 实际 ISCC 产包、退出等待、UAC、应用、重启握手；便携失败能恢复旧版且用户设置保留 |

Stella 的真实进程家族、反作弊兄弟进程、完整下载 URL 仍须取得实机或抓包证据。
其 Avalonia README 报告曾遇到 `NoSuchKey`，本轮未复现；把它作为 M1 的调查输入，不能据此实现猜测的签名 URL。
Stella 清单规模也只作为容量测试输入；BA 的“启动器清单外由游戏自行下载内容”现象不能直接外推到 Stella。

### M2：共享库从能立即删除重复实现的边界开始

推荐方向是“独立产品，共享协议”，对应既有研究的方案 C；这是建议，尚未替代既有 ADR。
第一版只共享签名、vc 与 wire 格式：不搬整个 `Cafe.Launcher.Core`，不把 UI、资源面板、汉化源、
日志产品策略、Windows updater 或 Linux 运行器一起纳入。

先在两个消费者上验证接缝，再确定中性程序集名称和分发方式。ADR 应比较：
同源码树工程引用、固定 Git revision 的源码依赖、版本化 NuGet；以双产品维护和发布成本选择。
试点可以用本地引用验证，正式共享必须固定版本或 revision，禁止正式工程依赖 `E:\Repos` 绝对路径。

新增项目同时调整 `AssemblyNamingContractTests`、公开面声明和拆分契约。
Core 实现收窄与共享移动按实际调用方逐批处理，不批量扩大 public，也不新增生产 `InternalsVisibleTo`。
升级协议库时分别运行两个产品的测试与发布检查；消费者保留回退到已验证版本的能力。

### M3/M4：有证据再扩展

- CRC64 与 manifest diff 可作为下一批低耦合候选；安装状态存储必须先证明配置变体、临时文件验证和中断语义。
- 网络传输与下载恢复的复用价值高，但依赖代理、诊断、取消、检查点和文件系统边界；先列出消费者差异，按完整行为模块迁移。
- Linux 以现有 [实机记录](linux-p0a-verification-2026-09-22.md) 为已验证范围，追加软件包安装、GUI 启动、桌面入口与卸载测试，避免由 Windows 官方样本推导反作弊兼容性。
- 桌面通知按 [既有通知计划](desktop-notifications-integration-plan.md) 单独推进；先更新其分层基线，不作为协议共享的前置条件。
- 若产品最终选择同仓库构建多发行版，再引入构建期档案与品牌资源装载；当前不实施运行期多游戏切换，也不迁移 Cafe 用户数据根。

## 4. 第一批可拆分的任务

| 顺序 | 任务 | 涉及现有位置 | 验收 |
| --- | --- | --- | --- |
| 1 | 建立双样本 fixture 及来源说明 | `tests/Cafe.Launcher.Tests` 的协议测试 | 独立向量可重现；样本不带用户数据、Cookie 或在线短期凭据 |
| 2 | 补配置格式变体与读取/写入契约 | `LocalGameContracts`、`OfficialHashService`、`LocalInstallationStateStore` | 三种参数形态双向验证；BA 当前固定向量不变 |
| 3 | 用第二档案验证签名与 API 组合 | `AuthorizationHeaderFactoryTests`、`LauncherApiClientTests` | 双游戏均通过；不登记未经验证的生产 Stella 可执行名或 Cookie 路径 |
| 4 | 执行 BA 官方互操作矩阵，收集 Stella 下载与进程证据 | 隔离环境、既有运行门及下载测试 | 每条记录环境/样本/结果；失败进入对应实现任务 |
| 5 | 编写共享试点 ADR并让两个消费者消费 | Core、第二产品适配层、程序集契约 | 删除重复算法且两个产品均通过；未满足就不扩大迁移 |

已有审计待办继续以 [CODEBASE_AUDIT.md](../../CODEBASE_AUDIT.md) 为准：
`AUD-TEST-015` 的 Headless 偶发失败须在发布前调查；`AUD-ARCH-015` 的公开实现收窄可结合 M2 实际调用方推进。
本计划不重新登记这两项，也不将第二游戏尚未支持的协议变体记为 BA 产品缺陷。

## 5. 验证与暂缓项

实施时先跑修改边界对应的协议、状态存储、下载/运行门测试；UI 变化运行 `dev.ps1 ui`，
资源变化运行本地化契约。每批准备合并或发布前运行 `verify.ps1`。
真实安装、官方互操作、UAC、反作弊与 Linux 桌面行为单独记录，不能以单元测试通过替代。

暂缓：`clickCode` 搬运、官方 Electron 自更新的 `YostarGames` 暂移/恢复、AIHelp、云端日志上报。
前两者只有在明确需要原位替换官方安装器且实验确认必要时才进入任务；后两者不符合当前本地诊断/无遥测方向。
保留 Cafe 的写入拒绝、路径保护、SHA-256 自更新信任链、快启动检查与 CRC64 修复策略。

本轮未调用在线 API、启动官方 EXE、安装/卸载游戏或执行两个仓库的共享重构。
文档验证结果在本轮交付说明中报告；上述里程碑全部仍为待执行。
