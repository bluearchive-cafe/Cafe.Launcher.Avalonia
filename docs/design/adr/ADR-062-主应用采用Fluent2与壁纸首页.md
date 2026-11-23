# ADR-062 主应用采用 Fluent 2 与壁纸首页

- 状态：已接受（迁移范围由 [ADR-063](ADR-063-全应用统一Fluent2视觉.md) 扩展至全应用）
- 日期：2026-11-14

## 决策

- 首页左栏采用 FluentAvaloniaUI 3.1.0 图标和局部 FluentAvaloniaTheme。全局替换会改变下拉框、展开器、选项卡与菜单的模板部件，首批沿用基础 FluentTheme，现有 Launcher 资源、控件模板与动态色管线继续承载其余界面。
- 图片与视频在窗口根层填充。左上显示官方游戏 Logo，左栏底部进入设置，左下查看资讯，右下使用分体操作与任务卡。首页连接已有的游戏和资讯命令。
- 隔离崩溃报告进程继续按 [ADR-020](ADR-020-崩溃窗口独立Fluent设计系统.md) 加载自身的 FluentTheme 与 Crash 资源。
- 蔚蓝档案日服 Logo 来自[日服官方 4.5 周年网站](https://4th-half-anniversary.bluearchive.jp/assets/header-logo-75c0f3f9.png)，权利归原权利人。

## 验收

覆盖最小窗口与默认窗口、浅深主题及键盘焦点。单游戏版本的安装、更新、启动、暂停与重试继续使用既有业务契约。
