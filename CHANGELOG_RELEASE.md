## v1.0.0-beta.4

本版本重点修复与官方启动器共用同一游戏目录时的兼容性问题，并恢复窗口最小尺寸限制。

### 修复与改进

- **与官方启动器共用兼容性（重要）** — 修正本地 `manifest.json` 文件项 `vc` 完整性哈希的字段顺序为 `path, hash, size`，与官方启动器一致。此前按错误顺序计算 `vc`，导致用官方启动器修复或安装后，重写版会把整份清单判为「损坏」、误入修复界面，却又提示「没有需要修复的文件」，形成死循环。
- **启动校验放行（fail-open）** — `remoteManifest` 启动校验模式下，当无法获取远程清单（地址为空、网络失败，或服务端换包后旧版本清单被下架）时改为放行启动，与官方行为一致，消除「启动被拦截 ↔ 无文件可修复」的死锁。
- **默认游戏路径对齐官方** — 默认安装路径改为启动器自身目录下的 `YostarGames\BlueArchive_JP`（对齐官方 `dirname(exe)`），避免两个启动器把游戏安装到不同位置。
- **卸载清理残留** — 卸载完成后顺带删除位于 `%LOCALAPPDATA%\Cafe Launcher\` 的 `download_state.json` 续传标记，不再残留陈旧状态。

### 界面优化

- **窗口最小尺寸限制** — 为可调整大小的主窗口恢复 `1024×640` 最小尺寸（保留可缩放，初始尺寸仍为 1300×754），避免窗口被缩到布局不可用；窗口大小/位置不跨会话持久化。

### 技术变更（面向开发者）

- **共用兼容契约文档化与守卫** — 在 `CLAUDE.md` / `AGENTS.md` 记录 `ManifestFile` 字段顺序 wire 契约、`OfficialHashService` 三类 `vc`（清单文件 `path, hash, size`／清单信息 `name, version, basis`／游戏配置 `tag, name, params, version`）的字段顺序、启动校验 fail-open 与默认路径对齐；新增 `OfficialHashServiceTests`，以官方启动器真实产出的 `vc` 值作跨启动器兼容回归守卫。
- **清理无用本地化键** — 移除启动校验改为 fail-open 后不再引用的 `remoteManifestUrlRequestFailed` / `remoteManifestUrlEmpty` / `remoteManifestDownloadFailed` 本地化键。
- **测试大幅补充** — 新增 Avalonia Headless 与平台行为测试、ViewModel 交互状态与命令测试、远程资源与网络安全测试、游戏安装/更新/修复测试，以及窗口缩放与最小尺寸契约测试。
