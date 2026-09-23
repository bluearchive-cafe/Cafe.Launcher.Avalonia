# ADR-038：Linux 新增 RPM 安装包，与 deb 平行

- 状态：✅ 已接受
- 日期：2026-09-23
- 相关：`installer/linux/rpm/`、`installer/linux/debian/`、`scripts/Build-Distribution.ps1`、`.github/workflows/release.yml`、`tests/Cafe.Launcher.Avalonia.Tests/InstallerContractTests.cs`

## 背景

Linux 发行包此前是 `.tar.gz`、AppImage 与 `.deb`。RPM 系发行版（Fedora、RHEL / Alma / Rocky、openSUSE）的用户要么解压 tar.gz，要么自己转换 deb，缺少与 `.deb` 对等的安装包。

已有 deb 路径的形态是本决定的模板：

- 手写 `installer/linux/debian/control` 模板，构建时替换 `{VERSION}`，并把版本前缀的 `-` 转成 `~`（`1.1.0-beta.11` → `1.1.0~beta.11`，`~` 保证预发布排在正式版之前）。
- 构建根 `artifacts/bundle/linux-x64/deb-root`，布局为 `/opt/cafe-launcher`（整份自包含发布树）+ `/usr/bin/cafe-launcher`（wrapper）+ `/usr/share/applications` + `/usr/share/icons/hicolor/...`。
- wrapper 导出 `CAFE_LAUNCHER_PACKAGE_FORMAT=deb`，让 `GameInstallationPath.GetDefaultGamePath` 走包管理安装的分支（默认游戏目录取用户主目录，而不是 `/opt` 的兄弟目录）。
- 收尾用 `dpkg-deb --build --root-owner-group` 打包，再以 `dpkg-deb --info` 校验元数据。

发布工作流在 ubuntu 上执行：`build` job 用 pwsh 调 `scripts/Build-Distribution.ps1`，随后 bash 步骤断言产物清单、`dpkg-deb --extract` 校验布局、真的 `apt-get install` 本地 deb 并跑 `cafe-launcher --version`（`--version` 在 Avalonia 启动前返回，故可无头运行）。

用户对本次范围的要求是：**只增加 `.rpm` 产物，与 `.deb` 平行**——不建 DNF 仓库、repodata，也不涉及仓库托管。

## 决策

1. **新增 `installer/linux/rpm/`，与 `installer/linux/debian/` 同构**：`cafe-launcher.spec`（含 `{VERSION}` 占位符，构建时替换）、`cafe-launcher` wrapper（导出 `CAFE_LAUNCHER_PACKAGE_FORMAT=rpm`）、`cafe-launcher.desktop`。
2. **spec 不含 Source**，只做打包：构建时以 `-D app_dir / -D asset_dir / -D icon_dir` 指向已发布的发布树与资产目录；`%install` 只做拷贝与权限设置；`%files` 拥有 `/opt/cafe-launcher` 整棵树、`%{_bindir}/cafe-launcher`、desktop 文件、两个尺寸的图标与 `%license`。
3. **预发布版本转换与 deb 完全一致**（`-` → `~`），两套包对同一 `VersionPrefix` 得到语义相同的版本串。
4. **关闭 debug 包与 `__os_install_post`**（`%global debug_package %{nil}`、`%global __os_install_post %{nil}`）：载荷已是自包含的已发布 .NET 应用，且发行工作流所在的 Debian 系主机的 rpm 不自带 `find-debuginfo`。
5. **显式声明 X11 相关依赖**：`libX11.so.6()(64bit)`、`libICE.so.6()(64bit)`、`libSM.so.6()(64bit)`、`libfontconfig.so.1()(64bit)`、`libxkbcommon.so.0()(64bit)`，与 `debian/control` 的 `Depends` 一一对应，按 soname 声明以跨发行版可移植。
6. **`scripts/Build-Distribution.ps1` 在 `if ($IsLinux)` 内、deb 块之后**插入 rpm 块：`rpmbuild -bb`（`-D` 一律传绝对路径，因为 rpm 的 `%mkbuilddir` 会切换工作目录），要求 `RPMS/` 下恰好一个 rpm，复制为 `Cafe.Launcher.Avalonia_<tag>_linux-x64.rpm`，再用 `rpm -qp --queryformat` 校验元数据。
7. **发布工作流同步四处**：依赖步骤加 `rpm`；在同一 bash 步骤里断言恰好一个 rpm、`%{NAME}` = `cafe-launcher`、`%{ARCH}` = `x86_64`，随后 `rpm --root "$RUNNER_TEMP/rpm-root" --initdb` + `-ivh --nodeps --noscripts` 装进临时根目录，校验可执行位与 `sh -n` wrapper，并跑 `--version` 冒烟；下载链接表、`SHA256SUMS` 计数（6 → 7）与两处 release 附件列表同步加入 rpm。
8. **两套包共用同一布局与 wrapper 契约**：`/opt/cafe-launcher` + `/usr/bin/cafe-launcher`，只有 `CAFE_LAUNCHER_PACKAGE_FORMAT` 的取值不同（该值只判非空，仅用于可诊断性）。

## 被否决的替代方案

- **只发 `.deb`，RPM 系用户自行用 `alien` 转换。** 转换产物的元数据与依赖不可靠，且把一步易错的手工操作推给用户。
- **建立并托管 DNF / YUM 仓库（repodata）。** 用户明确只要 `.rpm` 产物；仓库还牵出托管位置与更新节奏的决定——deb 一侧同样没有自建 apt 仓库。
- **用 `fpm` 之类的第三方打包器一次产出 deb 与 rpm。** 引入新的工具链依赖且元数据不受控；现有 deb 走的是手写模板 + `dpkg-deb`，rpm 沿用同构脚本更一致，也能被契约测试逐行钉住。
- **RPM 改用 `/usr/lib/cafe-launcher` 等另一套布局。** 两套包布局不同会让文档、wrapper 与默认游戏目录逻辑分叉。
- **不写显式 `Requires`，依赖 `elfdeps` 自动生成。** Avalonia 的 X11 后端经 dlopen / P-Invoke 使用这些库，elfdeps 看不到它们，自动生成的依赖会漏。
- **在 CI 里用 Fedora 容器真装一遍。** 现有 `build` job 是单个 ubuntu-24.04；`rpm --root` 临时根目录安装已能验证载荷、布局与启动，成本低得多。

## 后果

- RPM 系发行版多一个可直接安装的 `.rpm`，与 deb 同布局、同 wrapper；`CHANGELOG_RELEASE.md`、`README.md` 平台表与发布下载表同步提及。
- 本地构建需要 `rpmbuild`（CI 由 `rpm` 包提供）；非 Linux 主机上 rpm 块与 deb 块一样被跳过，不影响 Windows / macOS 构建。
- 守卫：`InstallerContractTests` 的产物名断言、`LinuxRpmPackage_UsesRpmbuildLayoutAndValidatedMetadata`、`ReleaseWorkflow_AttachesPackagesAndKeepsBannerInSourceRepository`（两处附件各一次）与 `ReleaseWorkflow_PublishesChecksumManifestForEveryDistributionPackage`（计数 7）。
- 未做：仓库托管（repodata）、RPM GPG 签名。`docs/design/desktop-notifications-integration-plan.md` 的冷启动集成目前只覆盖 `.deb`，该计划落地时需把 RPM 安装视为同一类「包管理安装」。
- 已知限制：未在真实 RPM 发行版上实装验证，冒烟只在临时根目录内完成；spec 按保守写法（不使用更新版 rpm 才有的特性）编写，以兼容 ubuntu-24.04 自带的 rpm 4.18。
