namespace Cafe.Launcher.Avalonia.ViewModels;

/// <summary>Marks presentation state that can be displayed by the modal host.</summary>
public interface IModalContentViewModel
{
}

/// <summary>
/// 语言切换后的呈现刷新（D10）：实现者把全部本地化显示名与文案按当前语言重写一遍。
/// Shell 在语言变化时遍历呈现族里实现本接口的成员，此前 4 种方法名、7 个扇出目标
/// 与向导的事件自订阅收敛为这一个契约。
/// </summary>
public interface ILanguageAwarePresentation
{
    void RefreshLocalizedText();
}
