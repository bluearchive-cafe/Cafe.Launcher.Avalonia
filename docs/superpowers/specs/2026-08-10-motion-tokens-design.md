# Motion Token 设计

## 目标

建立统一、可发现的 Motion Token 层，供 Avalonia XAML 动画与 C# 动画生命周期共同使用；并使多个 Toast 以“最新在顶部、旧项平滑补位”的方式自然地进入与退出。除 Toast 堆叠行为外，既有动效继续保持当前时长、缓动、位移和可访问性语义。

## 范围

本次迁移以下既有动效：

- 覆盖层、表面、设置内容和底栏的进入/退出动画；
- Toast 的进入、退出、自动关闭进度过渡与多项堆叠重排；
- 轮播图 CrossFade；
- 覆盖层与 Toast 退出生命周期使用的 C# 时长。

不在范围内：

- 改变 Toast 以外的任何时长、位移、曲线或视觉方向；
- 修改 `Controls/LoadingOverlay.axaml` 或减少动画策略；
- 新增用户可配置的速度、全局开关或动画类型；
- 添加连接动画、缩放、弹跳或新的动效场景。

## 方案

采用混合 Token 层。

1. `App.axaml` 定义 `LauncherMotion*` 资源，供样式直接引用：
   - 时长：Faster 50ms、Fast 167ms、Content 200ms、Normal 250ms、OverlayDelay 50ms；
   - 位移：Toast 6px、Content 6px、Surface 8px、Bottom 12px；
   - 缓动：Enter 的 `ExponentialEaseOut`、Exit 的 `ExponentialEaseIn`、进度的 `LinearEasing`。
2. 新建 `Helpers/MotionTokens.cs`，为非 XAML 使用方公开等价的 `TimeSpan` 常量：`FasterDuration`、`FastDuration`、`ContentDuration`、`NormalDuration`、`OverlayDelay`。
3. `AnimationTimings.ExitAnimationDuration` 改为返回 `MotionTokens.FastDuration`，保留现有可测试的设置能力，避免影响退出生命周期测试。
4. XAML 使用 `StaticResource LauncherMotion*` 替换现有字面量；`RemoteContentViewModel` 使用 `MotionTokens.NormalDuration` 替换轮播的 250ms。
5. `ToastHostViewModel` 将新 Toast 插入 `ActiveToasts` 的索引 0，使最新通知位于堆叠顶部。
6. 新建局部 UI 行为（命名为 `ToastStackMotion`）只处理 Toast 项的纵向重排：记录同一项重排前后的 Y 坐标，先以旧坐标保持其视觉位置，再在 `MotionTokens.FastDuration` 内用进入曲线回到 0。它不管理 Toast 生命周期、自动关闭或横向进出动画。

Toast 项模板拆分为两层：内层 toast 卡片继续独占 X 轴进入/退出；外层堆叠项包装器由 `ToastStackMotion` 独占 Y 轴补位。两个变换轴彼此独立，因此新项进入、旧项退出与其余项补位能够同时发生而不相互覆盖。

XAML Token 和 C# Token 的值会由测试同时锁定。XAML 保持项目既有的 `Launcher*` 资源模式；C# 只承担代码层无权访问资源字典的时长需求，不引入标记扩展或运行时资源查找。

## 行为与兼容性

迁移必须是视觉等价的：

- 覆盖层淡入和退出保持 167ms；表面进入仍为延迟 50ms 后的 250ms；
- 内容与 Toast 进入保持 200ms；Toast 和覆盖层退出保持 167ms；
- 进入使用 `ExponentialEaseOut`，退出使用 `ExponentialEaseIn`，进度值使用线性 50ms；
- 位移仍为 Toast X=6、内容 Y=6、表面 Y=8、底栏 Y=12；堆叠项补位的起始 Y 偏移等于其重排前后真实布局坐标差，不额外施加固定距离或上限；
- `MotionVisibility`、`ToastHostViewModel` 的减少动画分支继续使用即时退出，不会新增等待；
- 轮播完整动效仍是 250ms CrossFade，减少动画仍是零时长。

Toast 堆叠规则：

- 新 Toast 始终添加至顶部；不使用错峰，以保持通知出现的即时性；
- 新卡片只播放既有横向进入；已有卡片仅在自身 Y 坐标变化时播放同向的纵向补位；
- 退出卡片在 `MotionVisibility` 完成退出前维持在当前堆叠位置；其实际移除后，其他卡片一起补位；
- 首次布局、未发生位置变化的项和减少动画模式下不播放补位，后者直接重排；
- 任意时刻至多运行一段项级补位；新的布局以最新坐标为终点，避免连续通知造成回弹、跳跃或累积位移。

## 测试策略

先新增失败的 UI 合同测试，要求：

1. `App.axaml` 包含完整的 Motion 时长、距离和缓动资源；
2. `MainWindow.Styles.axaml` 与 `Toast.axaml` 的所有动效属性均引用 Token，不再保留本次覆盖范围内的时长、缓动或位移字面量；
3. `MotionTokens` 的代码时长与预期值一致；
4. `AnimationTimings.ExitAnimationDuration` 继续默认等于快速 Token。
5. Toast 覆盖层使用顶部插入的堆叠集合、外层 `ToastStackMotion` 包装器和与 Token 绑定的 Y 轴补位；减少动画路径禁用该补位。

随后先让上述测试失败，再实施最小迁移使其通过，并运行 `./dev.ps1 ui`。保留既有 `MotionVisibility`、Toast 与轮播行为测试的断言意图；为顶部插入、并发 Toast 的独立退出和减少动画直接重排补充聚焦回归测试。

## 验收标准

- 所有现有动效值从 XAML 与 C# 散点迁移为命名 Token；
- 当前全动效与减少动画行为保持不变；
- 多个 Toast 按最新在顶部排序；新项、退出项与其余项补位在视觉上连续且不发生横向/纵向变换抢占；
- 减少动画时多个 Toast 的位置改变立即完成；
- 不新增依赖或设置项；
- UI 合同、单元与 Headless UI 测试通过；
- 捕获到的设置和恢复对话框动效在下一次 Kagami 审计中保持同样的方向和层级。
