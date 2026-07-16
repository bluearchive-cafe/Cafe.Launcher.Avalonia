# ADR 0002：单项目功能目录

状态：已接受

应用继续保持单个 Avalonia 项目。跨层但属于同一业务能力的契约、工作流和展示模型放入 `Features/<Feature>`；通用基础设施保留在 `Services`、`Helpers` 和 `Models`。新代码不得仅因文件长度创建只转发调用的浅层包装。

当前已建立 `Shell`、`GameOperations`、`SetupWizard` 与 `Diagnostics` 功能边界。视图样式通过 `Views/Styles` 按功能拆分，并由 `MainWindow.Styles.axaml` 按固定顺序加载。
