# 设置页信息架构重组设计

## 目标

设置页重组的首要目标是让用户更快找到指定设置。

本次只调整设置页的信息架构、导航和布局，不改变任何设置值、持久化 JSON 键、
保存行为、预览行为或游戏操作语义。

## 视觉基准

继续使用项目现有 Fluent 主题、`Launcher*` 设计 token、Material Icons 和设置行样式。
不引入新的颜色、圆角、阴影、字体或图标库。

设置弹窗尺寸调整为：

- `MaxWidth="900"`
- `MaxHeight="592"`
- 左侧导航宽度 `176`

主窗口最小宽度为 1024，因此设置弹窗不需要窄屏折叠导航。

## 总体布局

设置弹窗保持三层结构：

1. 顶部标题栏；
2. 中部设置工作区；
3. 底部固定的保存与取消操作区。

中部设置工作区改为两列：

- 左列：固定分类导航；
- 右列：紧凑状态摘要和当前分类内容。

状态摘要固定显示在右侧内容顶部，不属于任何设置分类。

## 分类导航

左侧分类顺序固定为：

1. 常规
2. 游戏
3. 下载与网络
4. 外观
5. 通知与内容
6. 高级
7. 关于

一次只显示一个分类的内容。点击分类不保存设置，也不丢弃其他分类中的编辑。

分类导航使用具备选择语义的 Avalonia 控件，必须支持：

- 鼠标点击；
- 上下方向键直接改变选择；
- Tab 从分类导航进入当前内容。

选中项使用现有 accent brush 表达，不增加直接颜色。

## 设置项目映射

### 常规

- 语言
- 关闭行为
- 动态效果

### 游戏

- 游戏路径
- 启动检查
- 游戏管理：修复、卸载

### 下载与网络

- 代理
- 下载源
- 下载限速
- 更新通道

### 外观

- 主题
- 主题色模式
- 主题色调色板
- 自定义主题色
- 背景来源
- 背景适配
- 背景填充色
- 自定义背景图片或目录操作

条件可见性保持当前规则：

- 调色板仅在对应主题色模式下显示；
- 自定义主题色仅在对应模式下显示；
- 背景填充色仅在 `Settings.Appearance.IsBackgroundFitSelected` 时显示；
- 自定义背景操作仅在自定义背景来源下显示。

### 通知与内容

- Toast 通知
- 远程内容卡片

### 高级

- 日志级别

### 关于

- 产品说明
- 版本信息
- 检查更新
- 官方网站
- GitHub 仓库
- 查看日志
- 导出日志
- 打开数据目录
- 版权与免责声明

## 状态摘要

现有完整状态面板改为固定的紧凑摘要，显示：

- 当前状态标题；
- 版本；
- 网络状态；
- 磁盘空间；
- 当前操作说明。

游戏程序和启动检查仍需可访问，但不占用固定摘要的主要横向空间：

- 在有值时放入状态摘要的第二行；
- 使用文本省略和 Tooltip 保留完整内容。

状态异常、加载失败和操作说明继续使用现有语义 brush，不改变错误处理。

## 分类状态

设置分类只属于当前应用会话：

- 第一次打开设置时选择“常规”；
- 在同一次应用会话中关闭并重新打开设置，恢复上次选择；
- 不写入 `settings.json`；
- 应用退出后不保留。

建议在 `SettingsViewModel` 中保存精确分类 code，而不是让 View 维护选择状态。

分类 code 固定为：

- `general`
- `game`
- `download-network`
- `appearance`
- `notifications-content`
- `advanced`
- `about`

未知 code 必须恢复为 `general`。

## 草稿与保存

所有分类继续共享同一个 `SettingsEditor.Current`：

- 切换分类不执行 `Commit()`；
- 切换分类不执行 `ApplySnapshot()`；
- 切换分类不触发 `SettingsSaved`；
- 外观预览继续即时生效；
- 点击保存时一次性保存所有分类的修改；
- 点击取消时恢复全部分类的已保存值；
- 关闭存在未保存修改的设置页时，继续显示现有确认弹窗。

保存期间分类导航和内容区一起禁用，避免保存中切换。

## XAML 组织

主设置 overlay 保留标题、状态摘要和底部操作区。

七个分类内容从 `MainWindowSettingsOverlay.axaml` 拆分为独立
`UserControl`：

- `SettingsGeneralSection.axaml`
- `SettingsGameSection.axaml`
- `SettingsDownloadNetworkSection.axaml`
- `SettingsAppearanceSection.axaml`
- `SettingsNotificationsContentSection.axaml`
- `SettingsAdvancedSection.axaml`
- `SettingsAboutSection.axaml`

这些 View：

- 继续使用 `MainWindowViewModel` 作为 `x:DataType`；
- 不创建新的 ViewModel；
- 不使用反射式 ViewLocator；
- 不复制设置状态；
- 只负责对应分类的 XAML 组合。

设置 overlay 通过当前分类的布尔状态控制七个 View 的 `IsVisible`。一次只允许一个
分类 View 可见。

## 本地化

新增的分类名称和分类说明必须加入 `en`、`zh-Hans`、`ja` 三个语言文件，并接入
`LocalizedStrings`。

能够精确表达相同含义的现有本地化键继续复用，不创建同义重复键。

## 无障碍

- 分类导航暴露 ListBox 或等价选择语义；
- 每个分类项具有本地化 `AutomationProperties.Name`；
- 当前分类标题作为内容区标题；
- 状态摘要文本保持可读顺序；
- 保存和取消仍位于稳定的 Tab 顺序末尾；
- 分类切换后焦点保持在导航项，不强制跳到内容区；
- 减少动态效果模式下不为分类切换增加动画。

## 测试

### ViewModel

- 默认分类为 `general`；
- 七个精确 code 均可选择；
- 未知 code 恢复为 `general`；
- 同一会话重新打开设置时保持选择；
- 切换分类不改变 `SettingsEditor` 草稿；
- 切换分类不触发保存；
- 保存和取消仍作用于全部分类。

### XAML 合约

- 七个 section View 均存在且使用 compiled binding；
- 每个现有设置绑定只出现于正确分类；
- overlay 不再包含原始长列表；
- 分类导航和固定 footer 存在；
- 不出现直接颜色、裸圆角或未 token 化图标尺寸。

### Headless

- 默认只显示常规分类；
- 选择每个分类时仅对应 View 可见；
- 分类切换后修改值仍保留；
- 保存中导航被禁用；
- 状态摘要和 footer 始终可见；
- 键盘能够改变分类选择。

## 完成条件

- 所有现有设置仍可访问和保存；
- 任一设置最多通过一次分类选择即可找到；
- 设置页不再依赖整页长滚动定位；
- Debug 和 Release 构建均为 0 warnings、0 errors；
- 逻辑测试与 Headless 测试 0 failed；
- `.\verify.ps1` 通过。
