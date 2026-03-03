using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;

namespace FamilyMang
{
    public class CatalogWindow : Window
    {
        private const int PageSize = 20;

        private readonly ApiClient _client = new ApiClient();
        private readonly PluginSettings _settings;

        private TextBox _urlBox;
        private TextBox _tokenBox;
        private TextBox _projectBox;
        private DataGrid _grid;
        private Button _connectBtn;
        private Button _loadBtn;
        private Button _prevBtn;
        private Button _nextBtn;
        private TextBlock _statusText;
        private TextBlock _pageText;

        private int _offset;
        private int _total;

        public string DownloadedFilePath { get; private set; }

        public CatalogWindow()
        {
            _settings = PluginSettings.Load();
            SetupWindow();
            BuildLayout();
            RestoreSettings();
        }

        private void SetupWindow()
        {
            Title = "FamilyMang \u2014 \u041a\u0430\u0442\u0430\u043b\u043e\u0433 \u0441\u0435\u043c\u0435\u0439\u0441\u0442\u0432";
            Width = 960;
            Height = 620;
            MinWidth = 760;
            MinHeight = 450;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            FontFamily = new FontFamily("Segoe UI");
            FontSize = 13;
            Background = new SolidColorBrush(Color.FromRgb(250, 250, 250));
        }

        #region UI Construction

        private void BuildLayout()
        {
            var root = new DockPanel();

            var connBar = BuildConnectionBar();
            DockPanel.SetDock(connBar, Dock.Top);
            root.Children.Add(connBar);

            var actionBar = BuildActionBar();
            DockPanel.SetDock(actionBar, Dock.Bottom);
            root.Children.Add(actionBar);

            var pageBar = BuildPaginationBar();
            DockPanel.SetDock(pageBar, Dock.Bottom);
            root.Children.Add(pageBar);

            _grid = BuildDataGrid();
            root.Children.Add(_grid);

            Content = root;
        }

        private Border BuildConnectionBar()
        {
            var grid = new Grid { Margin = new Thickness(12, 10, 12, 6) };

            grid.ColumnDefinitions.Add(Col(GridUnitType.Auto));
            grid.ColumnDefinitions.Add(Col(GridUnitType.Star, 1));
            grid.ColumnDefinitions.Add(Col(GridUnitType.Auto));
            grid.ColumnDefinitions.Add(Col(GridUnitType.Star, 1));
            grid.ColumnDefinitions.Add(Col(GridUnitType.Auto));

            grid.RowDefinitions.Add(new RowDefinition());
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(6) });
            grid.RowDefinitions.Add(new RowDefinition());

            Put(grid, Label("\u0421\u0435\u0440\u0432\u0435\u0440:"), 0, 0);
            _urlBox = TextInput(); Put(grid, _urlBox, 0, 1);

            Put(grid, Label("Project ID:", 12), 0, 2);
            _projectBox = TextInput(); Put(grid, _projectBox, 0, 3);

            Put(grid, Label("\u0422\u043e\u043a\u0435\u043d:"), 2, 0);
            _tokenBox = TextInput();
            Grid.SetColumnSpan(_tokenBox, 3);
            Put(grid, _tokenBox, 2, 1);

            _connectBtn = Btn("\u0417\u0430\u0433\u0440\u0443\u0437\u0438\u0442\u044c \u0441\u043f\u0438\u0441\u043e\u043a", true);
            _connectBtn.Margin = new Thickness(8, 0, 0, 0);
            _connectBtn.Click += OnConnectClick;
            Grid.SetRowSpan(_connectBtn, 3);
            Put(grid, _connectBtn, 0, 4);

            return new Border
            {
                Child = grid,
                BorderBrush = new SolidColorBrush(Color.FromRgb(220, 220, 220)),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(0, 0, 0, 6)
            };
        }

        private DataGrid BuildDataGrid()
        {
            var dg = new DataGrid
            {
                AutoGenerateColumns = false,
                IsReadOnly = true,
                SelectionMode = DataGridSelectionMode.Single,
                SelectionUnit = DataGridSelectionUnit.FullRow,
                GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
                HeadersVisibility = DataGridHeadersVisibility.Column,
                HorizontalGridLinesBrush = new SolidColorBrush(Color.FromRgb(230, 230, 230)),
                RowBackground = Brushes.White,
                AlternatingRowBackground = new SolidColorBrush(Color.FromRgb(247, 249, 252)),
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(Color.FromRgb(200, 200, 200)),
                Margin = new Thickness(12, 6, 12, 4),
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            };

            dg.Columns.Add(TextCol("\u0418\u043c\u044f \u0441\u0435\u043c\u0435\u0439\u0441\u0442\u0432\u0430", "FamilyName", 2));
            dg.Columns.Add(TextCol("\u041a\u0430\u0442\u0435\u0433\u043e\u0440\u0438\u044f", "Category", 1));
            dg.Columns.Add(TextCol("\u0424\u0430\u0439\u043b", "original_filename", 1.5));
            dg.Columns.Add(TextCol("\u0421\u0442\u0430\u0442\u0443\u0441", "StatusDisplay", 0, 100));
            dg.Columns.Add(TextCol("\u0420\u0430\u0437\u043c\u0435\u0440", "SizeDisplay", 0, 80));

            dg.SelectionChanged += (s, e) =>
            {
                if (_loadBtn != null) _loadBtn.IsEnabled = dg.SelectedItem != null;
            };

            return dg;
        }

        private StackPanel BuildPaginationBar()
        {
            var panel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(12, 2, 12, 2)
            };

            _prevBtn = Btn("\u25C4 \u041d\u0430\u0437\u0430\u0434", false);
            _prevBtn.IsEnabled = false;
            _prevBtn.Click += OnPrevClick;
            panel.Children.Add(_prevBtn);

            _pageText = new TextBlock
            {
                Text = "\u0421\u0442\u0440. 0 / 0   (\u0432\u0441\u0435\u0433\u043e: 0)",
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(16, 0, 16, 0),
                Foreground = new SolidColorBrush(Color.FromRgb(100, 100, 100))
            };
            panel.Children.Add(_pageText);

            _nextBtn = Btn("\u0414\u0430\u043b\u0435\u0435 \u25BA", false);
            _nextBtn.IsEnabled = false;
            _nextBtn.Click += OnNextClick;
            panel.Children.Add(_nextBtn);

            return panel;
        }

        private DockPanel BuildActionBar()
        {
            var panel = new DockPanel { Margin = new Thickness(12, 6, 12, 8) };

            var closeBtn = Btn("\u0417\u0430\u043a\u0440\u044b\u0442\u044c", false);
            closeBtn.Click += (s, e) => SaveAndClose(false);
            DockPanel.SetDock(closeBtn, Dock.Right);
            panel.Children.Add(closeBtn);

            _loadBtn = Btn("\u0417\u0430\u0433\u0440\u0443\u0437\u0438\u0442\u044c \u0432 \u043f\u0440\u043e\u0435\u043a\u0442", true);
            _loadBtn.IsEnabled = false;
            _loadBtn.Margin = new Thickness(0, 0, 8, 0);
            _loadBtn.Click += OnLoadClick;
            DockPanel.SetDock(_loadBtn, Dock.Right);
            panel.Children.Add(_loadBtn);

            _statusText = new TextBlock
            {
                Text = "\u0412\u0432\u0435\u0434\u0438\u0442\u0435 \u043f\u0430\u0440\u0430\u043c\u0435\u0442\u0440\u044b \u043f\u043e\u0434\u043a\u043b\u044e\u0447\u0435\u043d\u0438\u044f \u0438 \u043d\u0430\u0436\u043c\u0438\u0442\u0435 \u00ab\u0417\u0430\u0433\u0440\u0443\u0437\u0438\u0442\u044c \u0441\u043f\u0438\u0441\u043e\u043a\u00bb",
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Foreground = new SolidColorBrush(Color.FromRgb(120, 120, 120))
            };
            panel.Children.Add(_statusText);

            return panel;
        }

        #endregion

        #region Helpers for UI construction

        private static ColumnDefinition Col(GridUnitType type, double val = 1)
        {
            return type == GridUnitType.Auto
                ? new ColumnDefinition { Width = GridLength.Auto }
                : new ColumnDefinition { Width = new GridLength(val, type) };
        }

        private static void Put(Grid g, UIElement el, int row, int col)
        {
            Grid.SetRow(el, row);
            Grid.SetColumn(el, col);
            g.Children.Add(el);
        }

        private static TextBlock Label(string text, double leftMargin = 0)
        {
            return new TextBlock
            {
                Text = text,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(leftMargin, 0, 8, 0)
            };
        }

        private static TextBox TextInput()
        {
            return new TextBox
            {
                VerticalContentAlignment = VerticalAlignment.Center,
                Padding = new Thickness(4, 3, 4, 3)
            };
        }

        private static Button Btn(string text, bool primary)
        {
            var b = new Button
            {
                Content = text,
                Padding = new Thickness(16, 6, 16, 6),
                Cursor = Cursors.Hand,
                FontWeight = primary ? FontWeights.SemiBold : FontWeights.Normal
            };
            if (primary)
            {
                b.Background = new SolidColorBrush(Color.FromRgb(0, 120, 212));
                b.Foreground = Brushes.White;
                b.BorderBrush = new SolidColorBrush(Color.FromRgb(0, 100, 180));
            }
            return b;
        }

        private static DataGridTextColumn TextCol(
            string header, string binding, double star = 0, double px = 0)
        {
            var width = star > 0
                ? new DataGridLength(star, DataGridLengthUnitType.Star)
                : new DataGridLength(px);
            return new DataGridTextColumn
            {
                Header = header,
                Binding = new Binding(binding),
                Width = width
            };
        }

        #endregion

        #region Logic

        private void RestoreSettings()
        {
            _urlBox.Text = _settings.ServerUrl;
            _tokenBox.Text = _settings.JwtToken;
            _projectBox.Text = _settings.ProjectId;
        }

        private void PersistSettings()
        {
            _settings.ServerUrl = _urlBox.Text.Trim();
            _settings.JwtToken = _tokenBox.Text.Trim();
            _settings.ProjectId = _projectBox.Text.Trim();
            _settings.Save();
        }

        private void SaveAndClose(bool result)
        {
            PersistSettings();
            DialogResult = result;
        }

        private async void OnConnectClick(object sender, RoutedEventArgs e)
        {
            PersistSettings();
            _offset = 0;
            await LoadPageAsync();
        }

        private async void OnPrevClick(object sender, RoutedEventArgs e)
        {
            _offset = Math.Max(0, _offset - PageSize);
            await LoadPageAsync();
        }

        private async void OnNextClick(object sender, RoutedEventArgs e)
        {
            _offset += PageSize;
            await LoadPageAsync();
        }

        private async Task LoadPageAsync()
        {
            var projectId = _projectBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(projectId))
            {
                Status("\u0423\u043a\u0430\u0436\u0438\u0442\u0435 Project ID", true);
                return;
            }

            Busy(true);
            Status("\u0417\u0430\u0433\u0440\u0443\u0437\u043a\u0430\u2026", false);

            try
            {
                _client.BaseUrl = _urlBox.Text.Trim();
                _client.Token = _tokenBox.Text.Trim();

                var result = await _client.GetFamiliesAsync(projectId, PageSize, _offset);
                _total = result.total;
                _grid.ItemsSource = result.items;
                RefreshPagination();
                Status($"\u0417\u0430\u0433\u0440\u0443\u0436\u0435\u043d\u043e {result.items.Count} \u0438\u0437 {_total}", false);
            }
            catch (Exception ex)
            {
                Status($"\u041e\u0448\u0438\u0431\u043a\u0430: {ex.Message}", true);
                _grid.ItemsSource = null;
                _total = 0;
                RefreshPagination();
            }
            finally
            {
                Busy(false);
            }
        }

        private async void OnLoadClick(object sender, RoutedEventArgs e)
        {
            if (!(_grid.SelectedItem is FamilySummaryDto selected)) return;

            Busy(true);
            Status($"\u0421\u043a\u0430\u0447\u0438\u0432\u0430\u043d\u0438\u0435 {selected.original_filename}\u2026", false);

            try
            {
                DownloadedFilePath = await FamilyLoader.DownloadAsync(
                    _client, selected.id, selected.original_filename);
                SaveAndClose(true);
            }
            catch (Exception ex)
            {
                Status($"\u041e\u0448\u0438\u0431\u043a\u0430 \u0441\u043a\u0430\u0447\u0438\u0432\u0430\u043d\u0438\u044f: {ex.Message}", true);
                Busy(false);
            }
        }

        private void RefreshPagination()
        {
            int pages = _total == 0 ? 0 : (int)Math.Ceiling((double)_total / PageSize);
            int current = _total == 0 ? 0 : _offset / PageSize + 1;
            _pageText.Text = $"\u0421\u0442\u0440. {current} / {pages}   (\u0432\u0441\u0435\u0433\u043e: {_total})";
            _prevBtn.IsEnabled = _offset > 0;
            _nextBtn.IsEnabled = _offset + PageSize < _total;
        }

        private void Status(string text, bool error)
        {
            _statusText.Text = text;
            _statusText.Foreground = error
                ? new SolidColorBrush(Color.FromRgb(200, 40, 40))
                : new SolidColorBrush(Color.FromRgb(120, 120, 120));
        }

        private void Busy(bool busy)
        {
            _connectBtn.IsEnabled = !busy;
            _loadBtn.IsEnabled = !busy && _grid.SelectedItem != null;
            _urlBox.IsEnabled = !busy;
            _tokenBox.IsEnabled = !busy;
            _projectBox.IsEnabled = !busy;
            Cursor = busy ? Cursors.Wait : null;
        }

        #endregion
    }
}
