// ───────────────────────────────────────────────────────────────────────────
// ClassDesignerWindow.xaml.cs — Room / Class Layout Designer
//
// (C) 2026 TAD Europe — https://tad-it.eu
//
// Allows the teacher to visually map endpoint seats in the room,
// save the layout to JSON, and verify which machines are online.
//
// All room layout data is stored locally (no DC / NETLOGON required).
// ───────────────────────────────────────────────────────────────────────────

using System.IO;
using System.Net.NetworkInformation;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;

namespace TadTeacher.ClassDesigner;

public partial class ClassDesignerWindow : Window
{
    private RoomLayout _layout;

    // The seat currently being edited (row, col)
    private (int Row, int Col)? _editTarget;

    // Map of (row,col) → the Button on screen
    private readonly Dictionary<(int, int), Button> _seatButtons = new();

    public ClassDesignerWindow()
    {
        InitializeComponent();
        _layout = RoomLayout.Load();
        ApplyLayoutToUi();
        BuildGrid();
    }

    // ─── Grid Construction ────────────────────────────────────────────

    /// <summary>Rebuild the UniformGrid from the current layout dimensions.</summary>
    private void BuildGrid()
    {
        SeatGrid.Children.Clear();
        _seatButtons.Clear();

        SeatGrid.Rows    = _layout.Rows;
        SeatGrid.Columns = _layout.Cols;

        for (int r = 0; r < _layout.Rows; r++)
        {
            for (int c = 0; c < _layout.Cols; c++)
            {
                var seat = _layout.GetSeat(r, c);
                var btn  = CreateSeatButton(r, c, seat);
                _seatButtons[(r, c)] = btn;
                SeatGrid.Children.Add(btn);
            }
        }

        UpdateOnlineCount();
    }

    private Button CreateSeatButton(int row, int col, SeatDefinition? seat)
    {
        var label = seat?.Label ?? $"{(char)('A' + col)}{row + 1}";
        var host  = seat?.Host  ?? string.Empty;

        var inner = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };

        // Status dot
        var dot = new System.Windows.Shapes.Ellipse
        {
            Width  = 8,
            Height = 8,
            Margin = new Thickness(0, 0, 0, 4),
            HorizontalAlignment = HorizontalAlignment.Center,
            Fill = DotColor(seat?.Status ?? PingStatus.Unknown)
        };

        var lblText = new TextBlock
        {
            Text = label,
            FontWeight = FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center,
            Foreground = string.IsNullOrEmpty(host) ? new SolidColorBrush(Color.FromRgb(0x48, 0x4F, 0x58))
                                                     : new SolidColorBrush(Color.FromRgb(0xC9, 0xD1, 0xD9))
        };

        var hostText = new TextBlock
        {
            Text = string.IsNullOrEmpty(host) ? "(empty)" : ShortenHost(host),
            FontSize = 10,
            Foreground = new SolidColorBrush(Color.FromRgb(0x8B, 0x94, 0x9E)),
            HorizontalAlignment = HorizontalAlignment.Center
        };

        inner.Children.Add(dot);
        inner.Children.Add(lblText);
        inner.Children.Add(hostText);

        var r = row; var c = col; // capture for lambda
        var btn = new Button
        {
            Style   = (Style)FindResource("SeatTile"),
            Content = inner,
            Tag     = (r, c)
        };
        btn.Click += (_, _) => OnSeatClicked(r, c);

        return btn;
    }

    private static Brush DotColor(PingStatus status) => status switch
    {
        PingStatus.Online  => new SolidColorBrush(Color.FromRgb(0x3F, 0xB9, 0x50)),
        PingStatus.Offline => new SolidColorBrush(Color.FromRgb(0xF8, 0x51, 0x49)),
        _                  => new SolidColorBrush(Color.FromRgb(0x48, 0x4F, 0x58))
    };

    private static string ShortenHost(string host) =>
        host.Length > 14 ? host[..11] + "..." : host;

    // ─── Seat Editing ─────────────────────────────────────────────────

    private void OnSeatClicked(int row, int col)
    {
        _editTarget = (row, col);

        var seat = _layout.GetSeat(row, col);
        TxtSeatLabel.Text = seat?.Label ?? $"{(char)('A' + col)}{row + 1}";
        TxtSeatHost.Text  = seat?.Host  ?? string.Empty;

        EditPanel.Visibility = Visibility.Visible;
        TxtStatus.Text = $"Editing seat ({row + 1},{col + 1}) — enter a label and hostname or IP";
        TxtSeatHost.Focus();
    }

    private void BtnAssign_Click(object sender, RoutedEventArgs e)
    {
        if (_editTarget == null) return;
        var (row, col) = _editTarget.Value;

        _layout.SetSeat(row, col, TxtSeatLabel.Text.Trim(), TxtSeatHost.Text.Trim());
        CloseEditPanel();
        BuildGrid();
        TxtStatus.Text = $"Assigned seat ({row + 1},{col + 1})";
    }

    private void BtnClearSeat_Click(object sender, RoutedEventArgs e)
    {
        if (_editTarget == null) return;
        var (row, col) = _editTarget.Value;
        _layout.ClearSeat(row, col);
        CloseEditPanel();
        BuildGrid();
        TxtStatus.Text = $"Cleared seat ({row + 1},{col + 1})";
    }

    private void BtnCancelEdit_Click(object sender, RoutedEventArgs e) => CloseEditPanel();

    private void CloseEditPanel()
    {
        _editTarget = null;
        EditPanel.Visibility = Visibility.Collapsed;
    }

    // ─── Toolbar Actions ──────────────────────────────────────────────

    private void BtnApplyGrid_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(TxtRows.Text, out int rows) || rows < 1 || rows > 20) rows = 4;
        if (!int.TryParse(TxtCols.Text, out int cols) || cols < 1 || cols > 20) cols = 8;

        _layout.Name = TxtRoomName.Text.Trim();
        _layout.Rows = rows;
        _layout.Cols = cols;

        // Remove seats that fall outside the new bounds
        _layout.Seats.RemoveAll(s => s.Row >= rows || s.Col >= cols);

        TxtRows.Text = rows.ToString();
        TxtCols.Text = cols.ToString();
        CloseEditPanel();
        BuildGrid();
        TxtStatus.Text = $"Grid resized to {rows}×{cols}";
    }

    private async void BtnSavePing_Click(object sender, RoutedEventArgs e)
    {
        _layout.Name = TxtRoomName.Text.Trim();

        try { _layout.Save(); } catch (Exception ex)
        {
            MessageBox.Show($"Save failed:\n{ex.Message}", "TAD.RV", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        TxtStatus.Text = "Saved! Pinging assigned endpoints...";
        BtnSavePing.IsEnabled = false;

        await PingAllSeatsAsync();

        BtnSavePing.IsEnabled = true;
    }

    private void BtnLoad_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title            = "Load Room Layout",
            Filter           = "Room Layout JSON|*.json|All Files|*.*",
            InitialDirectory = Path.GetDirectoryName(RoomLayout.DefaultPath)
        };

        if (dlg.ShowDialog(this) == true)
        {
            try
            {
                _layout = RoomLayout.Load(dlg.FileName);
                ApplyLayoutToUi();
                CloseEditPanel();
                BuildGrid();
                TxtStatus.Text = $"Loaded: {dlg.FileName}";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Load failed:\n{ex.Message}", "TAD.RV", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }

    private void BtnClearAll_Click(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show(
            "Clear all seat assignments?  This cannot be undone.",
            "TAD.RV — Confirm", MessageBoxButton.YesNo, MessageBoxImage.Question);

        if (result == MessageBoxResult.Yes)
        {
            _layout.Seats.Clear();
            CloseEditPanel();
            BuildGrid();
            TxtStatus.Text = "All seats cleared";
        }
    }

    // ─── Ping Logic ───────────────────────────────────────────────────

    private async Task PingAllSeatsAsync()
    {
        var assigned = _layout.AssignedSeats.ToList();
        if (assigned.Count == 0)
        {
            TxtStatus.Text = "No seats assigned — nothing to ping";
            return;
        }

        int online = 0;
        var tasks = assigned.Select(async seat =>
        {
            seat.Status = await PingHostAsync(seat.Host)
                ? PingStatus.Online
                : PingStatus.Offline;

            if (seat.Status == PingStatus.Online)
                Interlocked.Increment(ref online);
        });

        await Task.WhenAll(tasks);

        // Redraw to show updated status dots
        BuildGrid();

        int total = assigned.Count;
        TxtStatus.Text = $"Ping complete — {online}/{total} online";
        TxtOnlineCount.Text = $"{online}/{total} online";
    }

    private static async Task<bool> PingHostAsync(string host)
    {
        try
        {
            using var ping  = new Ping();
            var reply = await ping.SendPingAsync(host, timeout: 1500);
            return reply.Status == IPStatus.Success;
        }
        catch { return false; }
    }

    private void UpdateOnlineCount()
    {
        int online = _layout.Seats.Count(s => s.Status == PingStatus.Online);
        int total  = _layout.AssignedSeats.Count();
        TxtOnlineCount.Text = total > 0 ? $"{online}/{total} online" : "— online";
    }

    // ─── UI Synchronisation ───────────────────────────────────────────

    private void ApplyLayoutToUi()
    {
        TxtRoomName.Text = _layout.Name;
        TxtRows.Text     = _layout.Rows.ToString();
        TxtCols.Text     = _layout.Cols.ToString();
    }

    // ─── Input validation (numbers only for rows/cols) ────────────────

    private void NumberOnly_PreviewTextInput(object sender, System.Windows.Input.TextCompositionEventArgs e)
    {
        e.Handled = !e.Text.All(char.IsDigit);
    }
}
