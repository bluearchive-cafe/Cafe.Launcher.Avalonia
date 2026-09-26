# ADR-039：Linux 打包启动资产共用模板去重

- 状态：✅ 已接受
- 日期：2026-09-24
- 相关：`installer/linux/templates/`、`installer/linux/appimage/`、`installer/linux/debian/`、`installer/linux/rpm/`、`installer/linux/arch/`、`scripts/Build-Distribution.ps1`、`tests/Cafe.Launcher.Avalonia.Tests/InstallerContractTests.cs`

## 背景

deb、rpm 与 pacman 三套 Linux 包各自提交了一份 `cafe-launcher` wrapper 与 `cafe-launcher.desktop`，共六份文件。它们实际只有两处差异：

- wrapper 里 `CAFE_LAUNCHER_PACKAGE_FORMAT` 的取值（`deb` / `rpm` / `pacman`）。该值只判非空、仅作可诊断性用途（见 [ADR-038](ADR-038-Linux新增RPM安装包且与deb平行.md)）。
- AppImage 的 desktop 是另一形态（`Exec=Cafe.Launcher.Avalonia`，不装 `/usr/bin`），不在这三套之列。

为 KDE 补 `Comment`/`GenericName`/`Keywords`/本地化字段时，同一段内容要改三处；任何后续改动都靠人工在六份文件间保持同步，是典型的漂移源。

## 决策

1. **单一来源**：新增 `installer/linux/templates/cafe-launcher`（含 `{PACKAGE_FORMAT}` 与 `{APP_DIR}` 占位符）与 `installer/linux/templates/cafe-launcher.desktop`（包管理安装的 desktop 条目，`Exec`/`TryExec` 指向 `/usr/bin/cafe-launcher`）。
2. **构建期生成**：`scripts/Build-Distribution.ps1` 新增 `New-LinuxPackageAssets -PackageFormat <deb|rpm> -Destination <dir>`，以 LF 写出 wrapper（并 `chmod +x`）与 desktop；deb/rpm 将 `{APP_DIR}` 写为 `/opt/cafe-launcher`。deb 从生成目录拷贝，rpm 把 `--define asset_dir` 指向生成目录。不再提交各格式副本。
3. **Arch 复用同一模板**：`installer/linux/arch/PKGBUILD` 在 `prepare()` 中从标签 Git 源码树内的 `installer/linux/templates/` 生成 wrapper（标记写 `pacman`，应用目录写为 Arch 规范的 `/usr/lib/cafe-launcher`）与 desktop。配方使用 `git+...#tag=v{_realver}`，避免发布提交不可能预先写入其自身 GitHub 自动归档哈希的循环；不再从 `$startdir` 越界取文件。`release.ps1` 在创建标签前与 csproj 一起更新 `_realver`、`pkgver` 和 `.SRCINFO`。
4. **AppImage 复用同一 desktop 模板**：模板把命令行抽成 `{EXEC_BLOCK}` 占位符。包安装替换为 `Exec=cafe-launcher` + `TryExec=cafe-launcher`；AppImage 替换为仅 `Exec=Cafe.Launcher.Avalonia`，不带 `TryExec`——AppImage 没有 `/usr/bin/cafe-launcher`，带 `TryExec` 会让菜单项被永久隐藏。
5. **守卫**：`InstallerContractTests.LinuxPackages_GenerateOneWrapperAndDesktopFromSharedTemplates` 断言模板存在、六份旧副本不复存在、`New-LinuxPackageAssets` 与替换逻辑存在、`.gitattributes` 对模板强制 LF。

## 被否决的替代方案

- **保留各格式副本，只加一条同步测试。** 仍是三份文件，改一处要动三处；测试只能事后发现漂移，不能消除重复。
- **让 Arch 用 `source=('../templates/...')` 或 `$startdir` 引用共享模板。** 前者会被 makepkg 按 basename 解析而找不到文件，后者被 `namcap` 判为越过标准构建目录；模板因此只从标签源码树读取。
- **把 Arch 也改为 `Build-Distribution.ps1` 驱动（像 rpm 那样只打包已发布树）。** 会让 PKGBUILD 失去独立可构建性（AUR 配方需自足），且用户明确不选「接入 CI」这一层。
- **让 AppImage 直接复用包管理的 desktop（含 `TryExec`）。** AppImage 没有 `/usr/bin/cafe-launcher`，`TryExec` 会让菜单项被永久隐藏；且 `Exec` 语义不同。故改为把命令行抽成 `{EXEC_BLOCK}` 占位符，由同一模板生成两种形态。

## 后果

- 修改 desktop 文案、KDE 字段或 wrapper 逻辑只改一处；格式差异集中在打包时的标记替换。
- deb/rpm 构建依赖生成步骤，Arch 构建依赖标签源码树内的模板；三者都由契约测试钉住，发布 CI 另在 Arch 容器中运行 `namcap`，防止副本或非法路径回归。
- 仓库内 `installer/linux/{debian,rpm,arch}/` 各只剩格式专属文件（`control`、`.spec`、`PKGBUILD`）。
