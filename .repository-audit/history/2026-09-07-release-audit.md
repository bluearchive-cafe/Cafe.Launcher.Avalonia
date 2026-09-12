# 2026-09-07 Release 审计报告（beta.6 发布后 · 下一版本就绪性）

## Audit Metadata

- 审计日期：2026-09-07
- Commit：55ca3e7（工作树干净）
- 模式：release（desktop-launcher 风险档案）
- 前基线：1ce42d1（2026-09-07 full 全量审计，报告已归档）
- 范围：`v1.1.0-beta.6`（f57f675，2026-09-05 06:44Z 已发布）→ HEAD 共 19 提交；审计增量 `1ce42d1..HEAD` 6 提交。聚焦下一版本发布就绪性：门禁实跑、发布资产状态、发布链路漂移、依赖一致性、升级/回滚面
- 项目档案：跨平台桌面启动器（Windows 正式支持；macOS/Linux 实验性）；GitHub Releases 双仓库分发，tag 推送触发 release.yml

## Executive Summary

**结论：代码与发布链路处于可发布状态，无任何 Critical/High/Medium open 项。下一版本的发布资产（版本号、发布说明、横幅）尚未开始准备，且下一版本命名（beta.7 / RC / 稳定版）待产品决策——这是打 tag 前的全部剩余工作。**

- beta.6 发布完整性确认：6 个平台资产（setup.exe / win-x64.zip / osx-arm64.zip / AppImage / deb / tar.gz）全部在场，prerelease 标记正确（gh release view 实证）。
- HEAD 全部门禁绿灯（本机 + CI 实跑，见下表）。
- 发布链路自 beta.6 tag 以来零漂移（workflows / scripts / installer / Directory.Build.props 无改动），2026-09-05/07 深度供应链核验结论整体延续；唯一变更是依赖升级（Avalonia 12.1.1→12.1.2，lock 与 notices 均已同步再生，三工件一致）。

Open findings：
- Critical：0
- High：0
- Medium：0
- Low：1（AUD-REL-004 下一版本发布准备未开始，含版本命名决策）

Most important risks/actions：
1. 决定下一版本号（i18n 三连修 + 下载源故障转移修复 + 设置重组，倾向 `v1.1.0-beta.7`；若判定质量收敛可议 RC）。
2. 发布准备三件套：csproj `VersionPrefix` 递增 → `CHANGELOG_RELEASE.md` 整体替换为新版单节（素材清单见 AUD-REL-004）→ 制作 `docs/assets/release-banners/cafe-launcher-<tag>-release-banner.png`。
3. 打 tag 前确认准备提交的 main CI 绿灯（HEAD 55ca3e7 本轮已实证绿灯）。

## 本机/远端门禁执行记录（全部通过）

| 步骤 | 结果 | 证据 |
|---|---|---|
| `Test-LocalizationContract.ps1` | 通过（exit 0；成功时静默） | 裸 key 守卫 + 资源键/占位符合约（8b91a9f/144572f/6ad1d94 三次 resx 变更后复跑） |
| `build.ps1`（Debug） | 通过 | 0 警告 0 错误（AVLN3001 守卫维持零触发） |
| `test.ps1` 单元 | 通过 | 1446 过 / 2 跳（既有符号链接跳过），0 失败 |
| `test.ps1` Headless | 通过 | 164/164 |
| main CI @ HEAD（run 34131115195） | 通过 | build job success：test + coverage + 多 RID 发布全绿 |
| 工作树 | 干净 | 审计全程无未提交变更（含门禁实跑后复检） |
| 依赖三工件一致 | 通过 | Directory.Packages.props = packages.lock.json = THIRD-PARTY-NOTICES.md 均 Avalonia 12.1.2 |

注：本机未重复 Release 配置编译与 coverage（CI 在 HEAD 已实跑覆盖）；两个本地跳过的符号链接测试为既有已知项，与基线一致。

## 发布要素逐项核验（release 模式）

### 通过（正面，逐项实证）

- **上一发布完整性**：beta.6 六资产齐全、prerelease 正确、发布于 2026-09-05 06:44Z；tag 指向 f57f675（annotated， Peeled 与远端一致）。
- **发布链路零漂移**：`git diff v1.1.0-beta.6..HEAD -- .github/ scripts/ installer/ Directory.Build.props` 为空；仅 `Directory.Packages.props` 变更（Avalonia 家族 12.1.1→12.1.2）。2026-09-05 release 审计对 release.yml 的深度供应链结论（SHA 固定、locked mode、横幅硬门禁、fail_on_unmatched_files、最小权限提升）整体延续。
- **无未测试提交窗口**：`v1.1.0-beta.6..HEAD` 每个提交都被「其后某次绿色 CI 或本机全量套件」覆盖；两次 cancelled run 均为后续推送取代（34123627518→34124120454、34124878418→34125067193），内容已含入后继绿色 run。
- **版本一致性防呆维持**：csproj 仍为 `1.1.0-beta.6` = 已发布 tag = CHANGELOG 标题 = 既有横幅，四者自洽（处于「上一版已发布、下一版未开始」的正常中间态）。
- **升级/回滚面**：tag 后无 settings.json 结构变更（6ad1d94 仅 UI 分区迁移，`UpdateChannel` 绑定与持久化属性未动）；自更新流程代码无变更；38ad9bb 仅改 Cafe 源 failover 语义，内容校验（CRC64）路径未动。

### 审计增量提交核验（1ce42d1..HEAD，逐项读码）

| 提交 | 核验结论 |
|---|---|
| 55ca3e7 fix(i18n) T()/F() 按所选语言解析 | `ResourceManager.GetString` 改经 `LauncherCultureResolver.GetCultureFor(CurrentLanguage)`；文化链核验无误（zh-CN→zh-Hans 卫星、zh-TW→zh-Hant、ja-JP→ja、en-US→中性）。含后台线程 SetLanguage 回归测试。`CurrentLanguage` 为引用赋值原子读，竞态窗口良性（LanguageChanged 在 UI 线程刷新绑定）。实机验证记录在提交说明。**用户可见修复，应入下一版发布说明。** |
| 38ad9bb fix(download) Cafe 源主备 CDN 共用地址 | `PatchUrlGroupService`：Cafe 组 BackUpCdn=PrimaryCdn，注释说明单宿主镜像上官方备路径不存在。此前备路径必 404，现 failover 重试可用宿主——语义改善而非退化；文件内容哈希校验不受影响。测试同步更新（+20 行）。 |
| 6ad1d94 feat(settings) 更新通道移至高级分类 | 纯 UI 分区迁移：同一 `Settings.Editor.Current.UpdateChannel` TwoWay 绑定自下载与网络组移至高级页新组；新增 `settingsGroupUpdates` 键四语言齐备、Designer/LocalizationKeys 已再生、样式契约分区清单与 resx 键数守卫同步适配（合约脚本通过实证）。无持久化影响。 |
| 8b91a9f / 144572f fix(i18n) 文案修订 | 纯 resx 表述变更，合约脚本通过；resx 键数守卫维持。 |
| 75f4f1d docs(audit) | 审计文档，无代码影响。 |

### 发现

## AUD-REL-004 — 下一版本发布准备未开始：版本号仍为已发布的 beta.6，发布说明与横幅未备，版本命名待决策

- Category：release/preparation
- Severity：Low（就绪性清单项，非缺陷——发布后 19 提交的正常中间态）；Confidence：100
- Status：open；Disposition：Product Decision（版本命名）+ 发版检查单（资产三件套）
- **Evidence**：csproj `VersionPrefix` = `1.1.0-beta.6`（等于已发布 tag）；`CHANGELOG_RELEASE.md` 仍为 beta.6 单节；`docs/assets/release-banners/` 最高仅 beta.6 横幅（ls 实证）。
- **Impact**：直接打 tag 会被 `Read-LauncherVersion.ps1` 精确匹配（tag ≠ VersionPrefix 即 throw）与「Verify release banner」硬门禁双重拦截——防呆有效，但发布流程无法完成。
- **Recommendation**：① 决定版本号（建议 `v1.1.0-beta.7`：增量全部为向后兼容修复 + 一项设置重组 feat，维持预发布序列递增约定）；② 递增 `VersionPrefix` 并整体替换 `CHANGELOG_RELEASE.md` 为新版单节；③ 制作新横幅。发布说明素材（tag 后用户可见变更，`git log v1.1.0-beta.6..HEAD` 复核）：
  - 启动后界面语言混杂修复（55ca3e7：后台线程 SetLanguage 不再致 XAML 目录按系统文化回退）
  - 四语言本地化表述修订：日语导航词、下载/校验文案可读性、地区习惯用语（8b91a9f / 144572f）
  - Cafe 下载源故障转移修复：主备 CDN 共用地址，备路径不再 404（38ad9bb）
  - 更新通道设置移至「高级」分类（6ad1d94）
  - 动态中性色层级与外观设置文案修正（67e8f88）
  - 只读游戏目录下更新检查不再静默通过（291b77a）
  - 向导选路径即时探测可写性 + 权限指引（1b6902b）、路径校验防抖与探测移出 UI 线程（1f7a8a2）
  - 依赖升级：Avalonia 12.1.1→12.1.2 及配套（f6f4d18）
- **Recommendation validation**：Verified（版本/横幅门禁为 workflow 直读实证；素材清单逐提交核对 git log）。
- **Suggested guard**：既有守卫足够（版本精确匹配 + 横幅硬门禁 + CHANGELOG 单节规则 + 发版检查单「git log 复核 fix/perf/feat 全覆盖」项）。

## Changes Since Previous Audit

基线 1ce42d1 → 55ca3e7 共 6 提交（1 文档 + 3 i18n + 1 下载源 + 1 设置分区），全部经上文逐项核验；无新增 open 缺陷。台账既有 5 项暂缓/接受项（AUD-ARCH-001/003、AUD-MTN-001、AUD-DEP-002、AUD-TST-001）不受本增量影响，维持原状。

## Verified Strengths（发布视角）

- 发布防呆链条（版本四点一致 + 横幅硬门禁 + 单节 CHANGELOG + fail_on_unmatched_files）在发布后继续有效：本审计确认「未做准备就打 tag」会被确定性拦截，而非静默产出错误版本。
- 发布后 19 提交维持零链路漂移 + 三工件依赖一致，说明既定合并纪律（lock 再生、notices 再生、CI locked mode）被持续执行。
- i18n 核心修复（55ca3e7）以最小切口（两处解析点）消除一类启动期语言混杂，且带回归测试与实机验证记录。

## Recommended Priorities

1. **发版准备（按序）**：定版本号 → 递增 `VersionPrefix` → 替换 `CHANGELOG_RELEASE.md`（用 AUD-REL-004 素材清单）→ 制作横幅 → 确认该提交 main CI 绿 → 打 tag。
2. **发布后**：继续台账 deferred 项（不阻塞发布）；下一周期可议将「发布资产三件套」脚本化为单一准备命令（可选）。

## Audit Method and Limitations

- release 模式聚焦发布链路与下一版本就绪性；架构/性能/可维护性维度沿用 2026-09-07 full 审计结论（增量提交未触及相关区域）。
- 本机未复跑 Release 配置编译、coverage 与 RID 发布（CI 在 HEAD 的绿色 run 已覆盖同等步骤）；本机门禁为合约 + Debug 构建 + 全量测试。
- 未核验 `RELEASE_REPOSITORY_TOKEN` 的实际权限形态（分发仓库配置，仓库内无证据），维持既有建议（评估 fine-grained PAT）。
- 55ca3e7 的实机验证采信提交说明记录（启动后语言一致），本审计以回归测试 + 文化链读码替代复现。
