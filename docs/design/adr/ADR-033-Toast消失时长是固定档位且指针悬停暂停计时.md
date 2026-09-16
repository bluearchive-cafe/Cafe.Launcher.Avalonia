# ADR-033：Toast 消失时长是固定档位，指针悬停即暂停计时

- 状态：✅ 已接受
- 日期：2026-09-16
- 来源：用户提出「Toast 的消失时间可以自定义（非用户侧），并且鼠标悬停时暂停计时」，随后修订为「将自定义的时间改为固定档位」
- 相关：ADR-014（Toast 自动消失继续由时长管理，不画进度条）、ADR-016（Fluent 动效层）、ADR-024（已保存设置只有一个写入方）

## 背景

消失时长此前是散落的魔数：`ToastOptions.DurationMs` 与 `ToastNotification.DurationMs` 各写一个 4000，`ToastService` 里再按严重级写死 4000 / 6000 / 8000。要整体调一遍消失时间得同时改三处，而且没有任何一处声明「这就是随包发布的时长」；调用方拿到的是「毫秒数」这个谁都能填的旋钮。

另一件事：Toast 一旦出现就只受时间支配。用户把指针停在卡片上读文案、或正要按动作按钮时，它照样到点消失。

## 决策

1. **时长是固定档位 `ToastDuration`**（`Models/`）：`Brief` / `Medium` / `Extended` 三档。档位名刻意取位置义而非「标准档」——中间那档若叫 `standard` 会读成默认值，而默认（信息、成功）恰恰是最短档；`short` / `long` 是 C# 原始类型名，`CA1720` 会直接拒绝。

2. **档位到实际时长的阶梯，以及严重级默认档位，同声明在 `ToastDurations`**：`Resolve(ToastDuration)` 给时间跨度（4s / 6s / 8s），`ForSeverity(ToastSeverity)` 给默认档位（信息与成功 → `Brief`，警告 → `Medium`，错误 → `Extended`，即既有 4000 / 6000 / 8000 一字未变）。它是 `Helpers/MotionTokens` 那一类固定时长常量，不是可注入的策略对象：**调用方只能选档位，给不出毫秒数**。这不是用户设置：不持久化、不上界面。

3. **单条 Toast 仍可选档**：`ToastOptions.Duration` 为 `ToastDuration?`，`null` 表示取严重级默认档；`ToastService.Show(message, severity, duration)` 的第三个参数同样可省。`ToastNotification.Duration` 携带**已解析的档位**（仍是 `ToastDuration`，不是毫秒），宿主用 `ToastDurations.Resolve` 把它变成等待时长——消费侧不存在第二套默认值，也不认识毫秒。

4. **指针悬停暂停计时**：指针进入卡片时中断当下这次等待，离开后**按完整时长重新计时**。暂停期间 Toast 不会消失；被暂停的 Toast 若被关闭或随宿主一起结束，计时随卡片一同结束，不会为已经离开的 Toast 再起一次等待。

5. **指针判定留在视图**：卡片自己的 `PointerEntered` / `PointerExited` 调 `ToastHostViewModel.SetToastPointerOver(id, bool)`。不采用「ViewModel 绑定 `InputElement.IsPointerOver`」的写法——那是只读样式属性，绑不回来。

## 被否决的替代方案

**保留一个可注入的时长策略（每个严重级一个可自由改写的毫秒值）**：这是本决策之前的一版实现。它把「时长」留成了自由旋钮——组合根能改成 1234ms，测试也能塞任意数字——而需求最终要的是固定档位：档位是产品节奏，不是每次调用都能重量的参数。

**让档位自己带毫秒值（`enum ToastDuration { Brief = 4000, … }`）**：枚举值立刻变成有含义的数字，序列化、日志和后续比较都会去依赖它；阶梯挪到 `ToastDurations.Resolve` 里的 switch 才是把「档位」与「实际多久」分开的那条线。

**给用户一个「通知显示时长」设置项**：时长是随包调好的节奏，不是用户要调的偏好。多一个持久化字段、一条归一化规则和一行界面，只为让极少数人微调，不划算；「非用户侧」本来就是需求的一部分。

**再补一档「永不消失」（同 Android `LENGTH_INDEFINITE`）**：带动作按钮的 Toast 已经不自动消失（宿主见到 `HasActions` 就不起计数），再开口子会让「为什么这条不消失」有两个来源。真要在档位上表达它，应当先把「不消失」统一到档位，那是另一次决策。

**暂停后接着倒计剩余时间**：要把「已经用掉多少」算准，得给宿主再塞一个时钟接缝（`TimeProvider` 之类），而它与窗口里既有的那次「悬停暂停」语义不一致——轮播恢复时是重新起一个完整间隔（`RemoteContentViewModel.UpdateCarouselPauseState`）。同一窗口里两种暂停表现不同，比「移开后还能看满一整轮」更难解释，而后者对「我正在读它」的场景是加分。

**把暂停状态放到 `ToastNotification` 上**：模型是给视图读的数据（`ToastNotification_IsPureDataWithoutAvaloniaBrush` 守着这条线），而暂停标记、恢复信号、中断源都是宿主的机制；放进模型等于让数据对象持有生命周期。

**用 `:pointerover` 样式表达暂停**：样式只能表达外观，暂停是行为，必须在 ViewModel 里落地，样式表无法取消一个待定的等待。

## 后果

- 消失时长只有一处声明（`ToastDurations` 的三档阶梯），任何 Toast 的时长都落在 4s / 6s / 8s 之一；单条 Toast 仍可在调用处选档。
- 既有节奏不变：信息与成功 4s、警告 6s、错误 8s。启动时「有可用更新」那条原本直接传 `durationMs: 8000`，现在传 `ToastDuration.Extended`，观感相同。
- 悬停期间 Toast 永不自动消失；指针移开后拿到完整时长。带动作按钮的 Toast 本来就不自动消失，不受影响。
- 键盘焦点**不**暂停计时：本次只按请求实现指针悬停。要覆盖键盘用户需另开一条决策（`GotFocus` / `LostFocus`）。
- 守卫：`ToastServiceTests`（严重级取默认档、显式档位覆盖默认、档位阶梯解析成 4/6/8 秒）、`ToastHostViewModelTests`（悬停中断待定等待、移开后按完整时长重起、悬停中被关闭后不再计时、无计时的 Toast 悬停是空操作）、`MainWindowHeadlessTests.Toast`（指针真的进入/离开卡片并到达宿主）。
- 无新增界面文案，无视觉变化，黄金基线不动。
