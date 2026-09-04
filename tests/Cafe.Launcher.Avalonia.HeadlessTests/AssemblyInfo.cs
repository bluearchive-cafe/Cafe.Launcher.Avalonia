using Xunit;

// 全程序集串行执行是测试隔离的基石，以下共享静态状态都依赖它，移除前必须先按
// Collection 隔离：
// - TestAnimationSetup 清零的 AnimationTimings.ExitAnimationDuration
// - SettingsAppearanceViewModel.ApplyScheme 改写的 Application 级资源
// - BackgroundViewModel.ResizeReloadDebounce 等测试临时改写的生产静态
[assembly: CollectionBehavior(DisableTestParallelization = true)]
