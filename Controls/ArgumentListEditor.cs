using System.Windows;
using System.Windows.Controls;

namespace ArkTerminal.Controls;

public sealed class ArgumentListEditor : StackPanel
{
    private readonly StackPanel _rows = new();
    public event Action? Changed;

    public ArgumentListEditor()
    {
        Children.Add(_rows);
        var add = new Button { Content = Localize.T("Text0293"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new(0, 4, 0, 4) };
        add.Click += (_, _) => { AddRow(string.Empty); Changed?.Invoke(); };
        Children.Add(add);
    }

    public IReadOnlyList<string> Values => _rows.Children.OfType<DockPanel>().Select(row => row.Children.OfType<TextBox>().Single().Text).ToArray();
    public void Load(IEnumerable<string> values)
    {
        _rows.Children.Clear();
        foreach (var value in values) AddRow(value);
    }

    private void AddRow(string value)
    {
        var row = new DockPanel { Margin = new(0, 2, 0, 2) };
        var remove = new Button { Content = Localize.T("Text0294"), Margin = new(6, 0, 0, 0) };
        DockPanel.SetDock(remove, Dock.Right);
        remove.Click += (_, _) => { _rows.Children.Remove(row); Changed?.Invoke(); };
        row.Children.Add(remove);
        var input = new TextBox { Text = value, MinWidth = 120, Padding = new(5, 3, 5, 3) };
        input.TextChanged += (_, _) => Changed?.Invoke();
        row.Children.Add(input);
        _rows.Children.Add(row);
    }
}
