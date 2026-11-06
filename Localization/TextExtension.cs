using System.ComponentModel;
using System.Windows.Data;
using System.Windows.Markup;
using ArkTerminal.Core.Localization;

namespace ArkTerminal.Localization;

public sealed class LocalizationBindingSource : INotifyPropertyChanged
{
    public static LocalizationBindingSource Instance { get; } = new();

    private LocalizationBindingSource() => Localize.Changed += () => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));

    public event PropertyChangedEventHandler? PropertyChanged;
    public string this[string key] => Localize.T(key);
}

[MarkupExtensionReturnType(typeof(BindingExpression))]
public sealed class TextExtension : MarkupExtension
{
    public TextExtension() { }
    public TextExtension(string key) => Key = key;

    [ConstructorArgument("key")]
    public string Key { get; set; } = string.Empty;

    public override object ProvideValue(IServiceProvider serviceProvider) =>
        new Binding($"[{Key}]") { Source = LocalizationBindingSource.Instance, Mode = BindingMode.OneWay }.ProvideValue(serviceProvider);
}
