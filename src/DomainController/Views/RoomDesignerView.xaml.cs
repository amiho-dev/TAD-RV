using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TADBridge.Shared.Classrooms;

namespace TADDomainController.Views;

public partial class RoomDesignerView : UserControl
{
    private RoomLayout _layout;
    private (int Row, int Col)? _editTarget;

    public RoomDesignerView()
    {
        InitializeComponent();
        _layout = RoomLayout.Load();
        TxtSyncPath.Text = RoomLayoutSync.ResolveSyncPath();
        ApplyLayoutToUi();
        BuildGrid();
    }

    private void BuildGrid()
    {
        SlotGrid.Children.Clear();
        SlotGrid.Rows = _layout.Rows;
        SlotGrid.Columns = _layout.Cols;

        for (int row = 0; row < _layout.Rows; row++)
        {
            for (int col = 0; col < _layout.Cols; col++)
            {
                var item = _layout.GetItem(row, col);
                SlotGrid.Children.Add(CreateSlotButton(row, col, item));
            }
        }
    }

    private Button CreateSlotButton(int row, int col, RoomItemDefinition? item)
    {
        var kind = item?.Kind ?? RoomItemKind.Seat;
        var label = string.IsNullOrWhiteSpace(item?.Label)
            ? DefaultLabelFor(row, col, kind)
            : item!.Label;
        var host = item?.Host ?? string.Empty;

        var inner = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };

        inner.Children.Add(new TextBlock
        {
            Text = kind == RoomItemKind.Table ? "Table" : "Seat",
            FontSize = 10,
            Foreground = new SolidColorBrush(Color.FromRgb(0x8B, 0x94, 0x9E)),
            HorizontalAlignment = HorizontalAlignment.Center
        });

        inner.Children.Add(new TextBlock
        {
            Text = label,
            FontWeight = FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center,
            Foreground = new SolidColorBrush(Color.FromRgb(0xC9, 0xD1, 0xD9))
        });

        inner.Children.Add(new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(host) ? "(unassigned)" : ShortenHost(host),
            FontSize = 10,
            Foreground = new SolidColorBrush(Color.FromRgb(0x8B, 0x94, 0x9E)),
            HorizontalAlignment = HorizontalAlignment.Center
        });

        var button = new Button
        {
            Style = (Style)FindResource("SlotTile"),
            Content = inner,
            Background = kind == RoomItemKind.Table
                ? new SolidColorBrush(Color.FromRgb(0x2A, 0x1F, 0x13))
                : new SolidColorBrush(Color.FromRgb(0x16, 0x1B, 0x22))
        };

        int capturedRow = row;
        int capturedCol = col;
        button.Click += (_, _) => OnSlotClicked(capturedRow, capturedCol);

        return button;
    }

    private void OnSlotClicked(int row, int col)
    {
        _editTarget = (row, col);

        var item = _layout.GetItem(row, col);
        var kind = item?.Kind ?? RoomItemKind.Seat;

        CmbItemKind.SelectedIndex = kind == RoomItemKind.Table ? 1 : 0;
        TxtSlotLabel.Text = string.IsNullOrWhiteSpace(item?.Label)
            ? DefaultLabelFor(row, col, kind)
            : item!.Label;
        TxtSlotHost.Text = item?.Host ?? string.Empty;

        EditPanel.Visibility = Visibility.Visible;
        TxtStatus.Text = $"Editing slot ({row + 1},{col + 1})";
    }

    private void BtnAssign_Click(object sender, RoutedEventArgs e)
    {
        if (_editTarget == null) return;

        var (row, col) = _editTarget.Value;
        var kind = SelectedKind();

        var label = TxtSlotLabel.Text.Trim();
        if (string.IsNullOrWhiteSpace(label))
            label = DefaultLabelFor(row, col, kind);

        _layout.SetItem(row, col, label, TxtSlotHost.Text.Trim(), kind);

        CloseEditPanel();
        BuildGrid();
        TxtStatus.Text = $"Assigned slot ({row + 1},{col + 1})";
    }

    private void BtnClearSlot_Click(object sender, RoutedEventArgs e)
    {
        if (_editTarget == null) return;

        var (row, col) = _editTarget.Value;
        _layout.ClearItem(row, col);

        CloseEditPanel();
        BuildGrid();
        TxtStatus.Text = $"Cleared slot ({row + 1},{col + 1})";
    }

    private void BtnCancelEdit_Click(object sender, RoutedEventArgs e)
    {
        CloseEditPanel();
    }

    private void BtnApplyGrid_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(TxtRows.Text, out int rows) || rows < 1 || rows > 30) rows = 4;
        if (!int.TryParse(TxtCols.Text, out int cols) || cols < 1 || cols > 30) cols = 8;

        _layout.Name = TxtRoomName.Text.Trim();
        _layout.Rows = rows;
        _layout.Cols = cols;
        _layout.Items.RemoveAll(i => i.Row >= rows || i.Col >= cols);

        TxtRows.Text = rows.ToString();
        TxtCols.Text = cols.ToString();

        CloseEditPanel();
        BuildGrid();
        TxtStatus.Text = $"Grid resized to {rows}x{cols}";
    }

    private void BtnSaveSync_Click(object sender, RoutedEventArgs e)
    {
        _layout.Name = TxtRoomName.Text.Trim();

        try
        {
            _layout.Save();
            TxtStatus.Text = "Layout saved and synced to Admin controllers";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Save failed:\n{ex.Message}", "TAD.RV", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void BtnReload_Click(object sender, RoutedEventArgs e)
    {
        _layout = RoomLayout.Load();
        ApplyLayoutToUi();
        CloseEditPanel();
        BuildGrid();
        TxtStatus.Text = "Reloaded synced layout";
    }

    private void BtnClearAll_Click(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show(
            "Clear all slot assignments? This cannot be undone.",
            "TAD.RV - Confirm",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result != MessageBoxResult.Yes) return;

        _layout.Items.Clear();
        CloseEditPanel();
        BuildGrid();
        TxtStatus.Text = "All slots cleared";
    }

    private void CloseEditPanel()
    {
        _editTarget = null;
        EditPanel.Visibility = Visibility.Collapsed;
    }

    private void ApplyLayoutToUi()
    {
        TxtRoomName.Text = _layout.Name;
        TxtRows.Text = _layout.Rows.ToString();
        TxtCols.Text = _layout.Cols.ToString();
    }

    private RoomItemKind SelectedKind()
    {
        if (CmbItemKind.SelectedItem is ComboBoxItem item)
        {
            var tag = item.Tag?.ToString();
            if (string.Equals(tag, "Table", StringComparison.OrdinalIgnoreCase))
                return RoomItemKind.Table;
        }

        return RoomItemKind.Seat;
    }

    private static string DefaultLabelFor(int row, int col, RoomItemKind kind)
    {
        if (kind == RoomItemKind.Table)
            return $"T{(row * 100) + (col + 1)}";

        return $"{(char)('A' + col)}{row + 1}";
    }

    private static string ShortenHost(string host) =>
        host.Length > 14 ? host[..11] + "..." : host;

    private void NumberOnly_PreviewTextInput(object sender, System.Windows.Input.TextCompositionEventArgs e)
    {
        e.Handled = !e.Text.All(char.IsDigit);
    }
}
