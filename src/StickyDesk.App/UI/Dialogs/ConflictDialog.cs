namespace StickyDesk;

internal sealed class ConflictDialog : Form
{
    public ConflictDialog(string fileName)
    {
        Text = "StickyDesk — конфликт версий";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        MaximizeBox = MinimizeBox = false;
        ShowInTaskbar = false;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(470, 315);
        Font = new Font("Segoe UI", 10);
        Padding = new Padding(20);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 80));
        var explanation = new Label { Dock = DockStyle.Fill, Text = $"«{fileName}» изменён другой программой.\nВ стикере тоже есть несохранённый текст.\nВыберите, какую версию использовать.", AutoSize = false };
        layout.Controls.Add(explanation);
        AddButton(layout, "Загрузить внешнюю версию", DialogResult.Yes);
        AddButton(layout, "Сохранить версию стикера в исходный файл", DialogResult.No);
        AddButton(layout, "Сохранить текст стикера в отдельный файл…", DialogResult.Retry);
        var cancel = AddButton(layout, "Решить позже", DialogResult.Cancel);
        CancelButton = cancel;
        Controls.Add(layout);
    }

    private static Button AddButton(TableLayoutPanel layout, string label, DialogResult result)
    {
        var button = new Button { Text = label, DialogResult = result, Dock = DockStyle.Fill, Height = 36, Margin = new Padding(0, 3, 0, 3) };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 43));
        layout.Controls.Add(button);
        return button;
    }
}
