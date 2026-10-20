# ADR-049：Linux 保留单一数据根，只统一小写目录名

- 状态：✅ 已接受
- 日期：2026-10-20
- 来源：用户评估 ADR-048 的四类根后认为其复杂度高于收益
- 相关：[ADR-025](ADR-025-数据根由组合根解析并注入.md)（数据根解析一次并注入）、[ADR-034](ADR-034-Unix单实例所有权用数据根内锁套接字判定.md)（Unix 单实例套接字）、[ADR-048](ADR-048-Linux按XDG基目录拆分数据根.md)（被本 ADR 撤销的拆分方案）

## 背景

ADR-048 把 Linux 的单一数据根拆成 `$XDG_DATA_HOME`、`$XDG_CONFIG_HOME`、`$XDG_STATE_HOME` 与 `$XDG_CACHE_HOME` 四个 `cafe-launcher` 目录，以贴合 XDG 分类。该决定尚未进入任何发布版本；复核后发现它给这款启动器带来的净收益有限，而长期成本明确：

- `LauncherDataRoot` 从一个根变成四个根，新增四参构造器与「某个文件属于哪一类」的判断；
- 多出一份按类别迁移的迁移器与配套测试；
- 每个新落盘点都要先选择类别，选错只在用户机器上以「设置没保存」「缓存跑进 config」暴露；
- 「打开数据目录」只能打开其中一个根；Linux 上设置、日志、缓存都不在其中，用户与支持文档都会困惑；
- 数据目录一旦支持用户切换，布局模式会从一种变成两种（XDG 四根与自定义单根），切换时还要在两种布局之间搬运数据。

## 决策

1. **所有平台回到单一数据根**。Windows/macOS 继续是 `%LOCALAPPDATA%\Cafe Launcher`；Linux 是 `$XDG_DATA_HOME/cafe-launcher`，`XDG_DATA_HOME` 为空或相对路径时回退到 `~/.local/share/cafe-launcher`。设置、日志、下载检查点、公告状态、图片缓存、崩溃快照、日志导出与受管兼容前缀都从这一个根派生。
2. **保留 ADR-048 的小写目录名**（`Cafe Launcher` → `cafe-launcher`）。旧版兼容前缀助手一直使用这个拼写，保留它才能让兼容前缀位置不变，并消除 Linux 上大小写不同的两份数据目录。
3. **保留 ADR-048 的注入式 `GameCompatibilityPaths`**。它接收注入的 `LauncherDataRoot`，不再自行解析进程根；ADR-025 的声明表缩减继续有效。
4. **一次性迁移旧 Linux 数据根**：进程首次解析数据根时，若 `$XDG_DATA_HOME/Cafe Launcher` 存在，则把其中的条目逐项移入 `$XDG_DATA_HOME/cafe-launcher`。迁移不覆盖新根已有同名内容；失败条目留在旧根；旧根只在清空后删除。运行期套接字 `cl-signal-*.sock` 不迁移，避免移动已绑定的套接字并让新进程绑定失败。迁移只在 Unix 生产路径上尝试一次，测试覆盖目录不触发。
5. **`LauncherDataRoot` 只保留 `Root`**。删除 `ConfigRoot` / `StateRoot` / `CacheRoot`；`LogDirectory` 等知名路径都从 `Root` 派生。
6. **运行期套接字继续留在数据根**，不迁到 `$XDG_RUNTIME_DIR`。ADR-034 的单实例语义不变。

## 被否决的替代方案

**保留 ADR-048 的四类根，只在界面上把「数据目录」包装成一个逻辑概念**：用户看到的是一个目录，但实现仍要维护四根、迁移与路径选择；数据目录可切换时还要额外定义自定义布局，复杂度没有消失。

**退回 `Cafe Launcher` 原名，不做迁移**：能省掉迁移，但 Linux 上会继续与旧版兼容前缀助手使用的 `cafe-launcher` 并存两个目录，兼容前缀也会回到一个与既有位置不同的路径。

**在单一根内部再分 `config/`、`state/`、`cache/` 子目录**：用户可见的仍是一个目录，但文件位置不再符合 XDG，且多一层类别判断，收益不足以抵消复杂度。

## 后果

- Linux 升级后首次启动会把旧 `Cafe Launcher` 目录的内容并入 `cafe-launcher`；不覆盖新内容，失败项保留，旧目录只在清空后删除。
- 兼容前缀位置不变；不需要移动 Wine/Proton 前缀。
- 设置与图片缓存不再分别落在 `XDG_CONFIG_HOME` / `XDG_CACHE_HOME`。这是有意接受的偏差：备份与缓存清理的粒度变粗，换来单一数据根与后续「数据目录可切换」的简单模型。
- `LauncherDataRootTests` 钉住单一根、Unix 小写目录名与 `XDG_DATA_HOME` 回退；`LauncherDataRootMigrationTests` 钉住整根迁移、不覆盖、幂等、套接字保留与旧目录清理。
- 文档与支持说明中的 Linux 数据目录统一为 `$XDG_DATA_HOME/cafe-launcher`（通常是 `~/.local/share/cafe-launcher`）。
