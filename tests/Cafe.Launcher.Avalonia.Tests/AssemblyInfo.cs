using Xunit;

// 全程序集串行执行是测试隔离的基石，以下共享静态状态都依赖它，移除前必须先按
// Collection 隔离（见 LocalizationServiceTestIsolation）：
// - TestLocalizationHelper.Initialize 写入的 LocalizationService 测试资源（"最后者胜"）
// - TestAnimationSetup 清零的 AnimationTimings.ExitAnimationDuration
// - SettingsAppearanceViewModel.ApplyScheme 改写的 Application 级资源
// - CultureInfo 的临时全局修改（LocalizationServiceTests / FileSizeFormatterTests 等）
[assembly: CollectionBehavior(DisableTestParallelization = true)]
