## v1.0.0-beta.6

### 本地化
- **全面规范本地化键命名体系**：设置分组统一 `settingsGroup*` 前缀，日志过滤对齐 `logLevel*` 命名，启动器自更新添加 `launcher` 前缀与游戏更新区分，游戏状态补 `game` 前缀，停止下载对话框正名为 `stopDownloadTitle/Message`
- **删除重复本地化键**：`notInstalled` 合并至 `gameNotInstalled`，`belowLowestVersion` 合并至 `gameBelowLowestVersion`
- **修正设置分类描述**：分类描述文字现已与各分类实际包含的设置项一致，并在侧边导航悬停时显示为提示
- **统一翻译术语**：规范汉化管理对话框功能名称与中文翻译用词
- **移除未使用的本地化属性**：清理 7 个从未在 XAML 中绑定的 `settingsCategory*Description` 属性

### 界面
- **统一对话框操作按钮尺寸与样式**：全部对话框底部按钮采用一致的高度、间距和颜色规范
- **修复设置界面视觉问题**：修复 ColorPicker 宽度、设置页脚按钮精确对齐等三处视觉与健壮性问题
- **固定汉化管理对话框尺寸**：防止内容溢出导致布局异常
- **修复 ColorPicker 宽度异常**：恢复设置页面 ColorPicker 控件的显式宽度属性

### 修复
- **修复 `System.IO.FileNotFoundException` 错误**：发布版本运行时缺少资源文件的崩溃
- **修复远程背景相对路径解析**：自定义背景图片路径解析异常
- **修复 Banner 图片加载失败**：因 SSRF 校验和 User-Agent 缺失被 CDN 拒绝
- **消除静默异常吞没**：为关键 catch 块增加诊断日志，避免错误被无声忽略
- **修复并行测试竞态条件**：`LocalizationService` 初始化线程安全

### 安装
- **添加 NSIS 安装脚本**：支持 Windows 安装向导与发行制品打包
- **清理失效的卸载登记**：移除过时的 InstallShield 残留

### 测试与覆盖率
- **启用双项目覆盖率采集**：合并单元测试与 Headless UI 测试的覆盖率报告
- **添加项目级覆盖率阈值检查**：`verify.ps1` 中强制手写代码行覆盖率 ≥ 84%、分支覆盖率 ≥ 90%
- **补充测试覆盖**：核心业务分支、下载与远程地址安全、平台交互分支
- **修正 UI 样式契约测试**：`ColorPicker.setting-control` Width 断言与实际布局一致
