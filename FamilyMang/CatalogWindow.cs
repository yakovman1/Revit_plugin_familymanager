using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FamilyMang
{
    public class CatalogWindow : Window
    {
        private const int PageSize = 20;

        private readonly PluginSettings _settings;

        private TextBox _urlBox;
        private TextBox _companyBox;
        private TextBlock _userLabel;
        private DataGrid _grid;
        private Button _connectBtn;
        private Button _loadBtn;
        private Button _prevBtn;
        private Button _nextBtn;
        private TextBlock _statusText;
        private TextBlock _pageText;
        private ComboBox _filterCombo;
        private Image _previewImage;
        private TextBlock _previewTitle;
        private TextBlock _previewHint;

        private int _offset;
        private int _total;
        private int _previewRequestId;
        private List<FamilySummaryDto> _cachedFamilies;
        private List<List<CatalogFamilyRow>> _primaryGroups = new List<List<CatalogFamilyRow>>();
        private readonly HashSet<string> _favoriteIds =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

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
            Width = 1120;
            Height = 640;
            MinWidth = 900;
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

            var center = BuildCenterPanel();
            root.Children.Add(center);

            Content = root;
        }

        private Grid BuildCenterPanel()
        {
            var grid = new Grid { Margin = new Thickness(12, 6, 12, 4) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(280) });

            _grid = BuildDataGrid();
            Grid.SetColumn(_grid, 0);
            grid.Children.Add(_grid);

            var preview = BuildPreviewPanel();
            Grid.SetColumn(preview, 1);
            grid.Children.Add(preview);

            return grid;
        }

        private Border BuildPreviewPanel()
        {
            var stack = new StackPanel { Margin = new Thickness(10, 0, 0, 0) };

            _previewTitle = new TextBlock
            {
                Text = "\u041f\u0440\u0435\u0432\u044c\u044e",
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 8)
            };
            stack.Children.Add(_previewTitle);

            _previewImage = new Image
            {
                Stretch = Stretch.Uniform,
                Height = 240,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            stack.Children.Add(_previewImage);

            _previewHint = new TextBlock
            {
                Text = "\u0412\u044b\u0431\u0435\u0440\u0438\u0442\u0435 \u043e\u0441\u043d\u043e\u0432\u043d\u043e\u0435 \u0441\u0435\u043c\u0435\u0439\u0441\u0442\u0432\u043e \u0432 \u0441\u043f\u0438\u0441\u043a\u0435",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 10, 0, 0),
                Foreground = new SolidColorBrush(Color.FromRgb(120, 120, 120))
            };
            stack.Children.Add(_previewHint);

            ClearPreview();

            return new Border
            {
                Child = stack,
                BorderBrush = new SolidColorBrush(Color.FromRgb(200, 200, 200)),
                BorderThickness = new Thickness(1, 0, 0, 0),
                Padding = new Thickness(4, 0, 0, 0)
            };
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
            _urlBox = CreateTextBox(); Put(grid, _urlBox, 0, 1);

            Put(grid, Label("Company ID:", 12), 0, 2);
            _companyBox = CreateTextBox(); Put(grid, _companyBox, 0, 3);

            Put(grid, Label("\u041f\u043e\u043b\u044c\u0437\u043e\u0432\u0430\u0442\u0435\u043b\u044c:"), 2, 0);
            _userLabel = new TextBlock
            {
                Text = Environment.UserName,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = new SolidColorBrush(Color.FromRgb(80, 80, 80))
            };
            Put(grid, _userLabel, 2, 1);

            Put(grid, Label("\u041f\u043e\u043a\u0430\u0437\u0430\u0442\u044c:", 12), 2, 2);
            _filterCombo = new ComboBox
            {
                MinWidth = 140,
                VerticalContentAlignment = VerticalAlignment.Center
            };
            _filterCombo.Items.Add("\u0412\u0441\u0435 \u0441\u0435\u043c\u0435\u0439\u0441\u0442\u0432\u0430");
            _filterCombo.Items.Add("\u0422\u043e\u043b\u044c\u043a\u043e \u0438\u0437\u0431\u0440\u0430\u043d\u043d\u044b\u0435");
            _filterCombo.SelectedIndex = 0;
            _filterCombo.SelectionChanged += OnFilterChanged;
            Put(grid, _filterCombo, 2, 3);

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

            dg.LoadingRow += OnGridLoadingRow;
            dg.Columns.Add(FavoriteColumn());

            dg.Columns.Add(TextCol("\u0420\u043e\u043b\u044c", "RoleDisplay", 0, 110));
            dg.Columns.Add(TextCol("\u0418\u043c\u044f \u0441\u0435\u043c\u0435\u0439\u0441\u0442\u0432\u0430", "IndentedFamilyName", 2));
            dg.Columns.Add(TextCol("\u041a\u0430\u0442\u0435\u0433\u043e\u0440\u0438\u044f", "Category", 1));
            dg.Columns.Add(TextCol("\u0412\u0435\u0440\u0441\u0438\u044f", "VersionDisplay", 0, 60));
            dg.Columns.Add(TextCol("\u0424\u0430\u0439\u043b", "original_filename", 1.5));
            dg.Columns.Add(TextCol("\u0421\u0442\u0430\u0442\u0443\u0441", "StatusDisplay", 0, 100));
            dg.Columns.Add(TextCol("\u0420\u0430\u0437\u043c\u0435\u0440", "SizeDisplay", 0, 80));

            dg.SelectionChanged += OnGridSelectionChanged;
            dg.PreviewMouseDown += OnGridPreviewMouseDown;
            dg.MouseLeftButtonUp += OnGridMouseLeftButtonUp;

            return dg;
        }

        private static DataGridTemplateColumn FavoriteColumn()
        {
            var col = new DataGridTemplateColumn
            {
                Header = "\u2605",
                Width = new DataGridLength(36)
            };

            var factory = new FrameworkElementFactory(typeof(Button));
            factory.SetValue(Button.PaddingProperty, new Thickness(0));
            factory.SetValue(Button.BorderThicknessProperty, new Thickness(0));
            factory.SetValue(Button.BackgroundProperty, Brushes.Transparent);
            factory.SetValue(Button.CursorProperty, Cursors.Hand);
            factory.SetValue(Button.FontSizeProperty, 16.0);
            factory.SetBinding(Button.ContentProperty, new Binding("FavoriteDisplay"));
            factory.AddHandler(Button.ClickEvent, new RoutedEventHandler(OnFavoriteButtonClick));

            col.CellTemplate = new DataTemplate { VisualTree = factory };
            return col;
        }

        private static void OnFavoriteButtonClick(object sender, RoutedEventArgs e)
        {
            if (!(sender is Button btn) || !(btn.DataContext is CatalogFamilyRow row))
                return;

            var window = Window.GetWindow(btn) as CatalogWindow;
            window?.ToggleFavoriteAsync(row);
            e.Handled = true;
        }

        private void OnGridLoadingRow(object sender, DataGridRowEventArgs e)
        {
            if (!(e.Row.Item is CatalogFamilyRow row))
                return;

            if (row.IsNested)
            {
                e.Row.IsEnabled = false;
                e.Row.Foreground = new SolidColorBrush(Color.FromRgb(110, 110, 110));
                e.Row.Background = new SolidColorBrush(Color.FromRgb(248, 248, 248));
            }
            else
            {
                e.Row.FontWeight = FontWeights.SemiBold;
            }
        }

        private void OnGridSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loadBtn == null)
                return;

            if (_grid.SelectedItem is CatalogFamilyRow row && row.IsSelectable)
            {
                _loadBtn.IsEnabled = true;
                _ = LoadPreviewForSelectionAsync(row);
            }
            else
            {
                _loadBtn.IsEnabled = false;
                ClearPreview();
                if (_grid.SelectedItem is CatalogFamilyRow nested && nested.IsNested)
                    _grid.SelectedItem = null;
            }
        }

        private void OnGridMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_grid.SelectedItem is CatalogFamilyRow row && row.IsSelectable)
                _ = LoadPreviewForSelectionAsync(row);
        }

        private void OnGridPreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            var dep = e.OriginalSource as DependencyObject;
            while (dep != null && !(dep is DataGridRow))
                dep = VisualTreeHelper.GetParent(dep);

            if (dep is DataGridRow row &&
                row.Item is CatalogFamilyRow item &&
                item.IsNested)
            {
                e.Handled = true;
            }
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

        private static TextBox CreateTextBox()
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
            _companyBox.Text = _settings.CompanyId;
        }

        private void PersistSettings()
        {
            _settings.ServerUrl = _urlBox.Text.Trim();
            _settings.CompanyId = _companyBox.Text.Trim();
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
            _cachedFamilies = null;
            _favoriteIds.Clear();
            await LoadPageAsync();
        }

        private async void OnFilterChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_cachedFamilies == null || _cachedFamilies.Count == 0)
                return;
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
            var companyId = _companyBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(companyId))
            {
                Status("\u0423\u043a\u0430\u0436\u0438\u0442\u0435 Company ID", true);
                return;
            }

            Busy(true);
            Status("\u0417\u0430\u0433\u0440\u0443\u0437\u043a\u0430\u2026", false);

            try
            {
                using (var auth = new JwtAuthService(_urlBox.Text.Trim(), companyId))
                using (var client = new ApiClient(auth))
                {
                    client.BaseUrl = _urlBox.Text.Trim();
                    if (_cachedFamilies == null)
                    {
                        _cachedFamilies = await client.GetAllFamiliesAsync().ConfigureAwait(true);
                        await LoadFavoritesAsync(client).ConfigureAwait(true);
                    }

                    _primaryGroups = CatalogHierarchy.BuildPrimaryGroups(_cachedFamilies);
                    var visibleGroups = FilterGroups(_primaryGroups);
                    _total = visibleGroups.Count;

                    var pageGroups = visibleGroups
                        .Skip(_offset)
                        .Take(PageSize)
                        .ToList();

                    var rows = pageGroups
                        .SelectMany(g => ApplyBundleVersion(ToRowsWithFavorites(g).ToList()))
                        .ToList();
                    _grid.ItemsSource = rows;
                    _grid.SelectedItem = null;
                    ClearPreview();
                    RefreshPagination();

                    int nestedOnPage = rows.Count(r => r.IsNested);
                    int primaryOnPage = rows.Count - nestedOnPage;
                    Status(
                        $"\u0417\u0430\u0433\u0440\u0443\u0436\u0435\u043d\u043e \u0433\u0440\u0443\u043f\u043f: {primaryOnPage} \u043e\u0441\u043d\u043e\u0432\u043d\u044b\u0445" +
                        (nestedOnPage > 0 ? $", {nestedOnPage} \u0432\u043b\u043e\u0436\u0435\u043d\u043d\u044b\u0445" : "") +
                        $" (\u0432\u0441\u0435\u0433\u043e \u0432 \u043a\u0430\u0442\u0430\u043b\u043e\u0433\u0435: {_total})",
                        false);
                }
            }
            catch (AuthException ex)
            {
                Status(ex.StatusCode == 403
                    ? "\u041e\u0448\u0438\u0431\u043a\u0430: Company ID \u043d\u0435 \u043d\u0430\u0439\u0434\u0435\u043d \u0438\u043b\u0438 \u0434\u043e\u0441\u0442\u0443\u043f \u0437\u0430\u043f\u0440\u0435\u0449\u0451\u043d"
                    : $"\u041e\u0448\u0438\u0431\u043a\u0430 \u0430\u0443\u0442\u0435\u043d\u0442\u0438\u0444\u0438\u043a\u0430\u0446\u0438\u0438: {ex.Message}", true);
                _grid.ItemsSource = null;
                _cachedFamilies = null;
                _primaryGroups.Clear();
                _total = 0;
                RefreshPagination();
            }
            catch (Exception ex)
            {
                Status($"\u041e\u0448\u0438\u0431\u043a\u0430: {ex.Message}", true);
                _grid.ItemsSource = null;
                _cachedFamilies = null;
                _primaryGroups.Clear();
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
            if (!(_grid.SelectedItem is CatalogFamilyRow row) || !row.IsSelectable)
                return;

            var selected = row.Family;
            if (selected == null)
                return;

            Busy(true);
            Status($"\u0421\u043a\u0430\u0447\u0438\u0432\u0430\u043d\u0438\u0435 {selected.original_filename}\u2026", false);

            try
            {
                using (var auth = new JwtAuthService(_urlBox.Text.Trim(), _companyBox.Text.Trim()))
                using (var client = new ApiClient(auth))
                {
                    client.BaseUrl = _urlBox.Text.Trim();
                    DownloadedFilePath = await FamilyLoader.DownloadAsync(
                        client, selected.id, selected.original_filename);
                }
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
            _loadBtn.IsEnabled = !busy &&
                _grid.SelectedItem is CatalogFamilyRow r && r.IsSelectable;
            _urlBox.IsEnabled = !busy;
            _companyBox.IsEnabled = !busy;
            if (_filterCombo != null)
                _filterCombo.IsEnabled = !busy;
            Cursor = busy ? Cursors.Wait : null;
        }

        private bool ShowFavoritesOnly =>
            _filterCombo != null && _filterCombo.SelectedIndex == 1;

        private List<List<CatalogFamilyRow>> FilterGroups(List<List<CatalogFamilyRow>> groups)
        {
            if (!ShowFavoritesOnly)
                return groups;

            return groups
                .Where(g => g.Count > 0 &&
                            g[0].Family != null &&
                            _favoriteIds.Contains(g[0].Family.id))
                .ToList();
        }

        private IEnumerable<CatalogFamilyRow> ToRowsWithFavorites(List<CatalogFamilyRow> group)
        {
            foreach (var row in group)
            {
                if (row.Family != null && !row.IsNested)
                    row.IsFavorite = _favoriteIds.Contains(row.Family.id);
                yield return row;
            }
        }

        private static IEnumerable<CatalogFamilyRow> ApplyBundleVersion(List<CatalogFamilyRow> group)
        {
            if (group == null || group.Count == 0)
                return group;

            var maxVer = group
                .Where(r => r.Family != null)
                .Select(r => r.Family.VersionNumber)
                .DefaultIfEmpty(1)
                .Max();

            var host = group.FirstOrDefault(r => !r.IsNested);
            if (host != null)
                host.BundleVersion = maxVer;

            return group;
        }

        private async Task LoadFavoritesAsync(ApiClient client)
        {
            _favoriteIds.Clear();
            try
            {
                var response = await client.GetFavoritesAsync().ConfigureAwait(true);
                if (response?.items == null)
                    return;
                foreach (var item in response.items)
                {
                    if (!string.IsNullOrWhiteSpace(item.family_id))
                        _favoriteIds.Add(item.family_id);
                }
            }
            catch
            {
                // закладки опциональны до реализации backend
            }
        }

        private async Task ToggleFavoriteAsync(CatalogFamilyRow row)
        {
            if (row?.Family == null || row.IsNested)
                return;

            var familyId = row.Family.id;
            PersistSettings();

            try
            {
                using (var auth = new JwtAuthService(_urlBox.Text.Trim(), _companyBox.Text.Trim()))
                using (var client = new ApiClient(auth))
                {
                    client.BaseUrl = _urlBox.Text.Trim();

                    if (row.IsFavorite)
                    {
                        await client.RemoveFavoriteAsync(familyId).ConfigureAwait(true);
                        _favoriteIds.Remove(familyId);
                        row.IsFavorite = false;
                    }
                    else
                    {
                        await client.AddFavoriteAsync(familyId).ConfigureAwait(true);
                        _favoriteIds.Add(familyId);
                        row.IsFavorite = true;
                    }
                }

                if (ShowFavoritesOnly)
                    await LoadPageAsync();
            }
            catch (Exception ex)
            {
                Status($"\u0417\u0430\u043a\u043b\u0430\u0434\u043a\u0438: {ex.Message}", true);
            }
        }

        private void ClearPreview()
        {
            _previewImage.Source = null;
            if (_previewTitle != null)
                _previewTitle.Text = "\u041f\u0440\u0435\u0432\u044c\u044e";
            if (_previewHint != null)
                _previewHint.Text =
                    "\u0412\u044b\u0431\u0435\u0440\u0438\u0442\u0435 \u043e\u0441\u043d\u043e\u0432\u043d\u043e\u0435 \u0441\u0435\u043c\u0435\u0439\u0441\u0442\u0432\u043e \u0432 \u0441\u043f\u0438\u0441\u043a\u0435";
        }

        private async Task LoadPreviewForSelectionAsync(CatalogFamilyRow row)
        {
            if (row?.Family == null || !row.IsSelectable)
                return;

            var family = row.Family;
            var requestId = ++_previewRequestId;

            _previewTitle.Text = family.FamilyName;
            _previewHint.Text = "\u0417\u0430\u0433\u0440\u0443\u0437\u043a\u0430 \u043f\u0440\u0435\u0432\u044c\u044e\u2026";
            _previewImage.Source = null;

            try
            {
                if (ThumbnailCache.TryGetExisting(family.id, out var cachedPath))
                {
                    if (requestId != _previewRequestId)
                        return;
                    ShowPreviewFromFile(cachedPath);
                    _previewHint.Text = family.Category;
                    return;
                }

                PersistSettings();
                using (var auth = new JwtAuthService(_urlBox.Text.Trim(), _companyBox.Text.Trim()))
                using (var client = new ApiClient(auth))
                {
                    client.BaseUrl = _urlBox.Text.Trim();
                    var url = await client.GetThumbnailUrlAsync(family.id).ConfigureAwait(true);
                    if (requestId != _previewRequestId)
                        return;

                    if (string.IsNullOrWhiteSpace(url))
                    {
                        _previewHint.Text = family.HasThumbnail
                            ? "\u041f\u0440\u0435\u0432\u044c\u044e \u043d\u0435 \u043d\u0430\u0439\u0434\u0435\u043d\u043e (404)"
                            : "\u041f\u0440\u0435\u0432\u044c\u044e \u043d\u0435\u0442. \u0412\u044b\u0433\u0440\u0443\u0437\u0438\u0442\u0435 \u0441\u0435\u043c\u0435\u0439\u0441\u0442\u0432\u043e \u0437\u0430\u043d\u043e\u0432\u043e \u043f\u043e\u0441\u043b\u0435 \u043e\u0431\u043d\u043e\u0432\u043b\u0435\u043d\u0438\u044f backend.";
                        return;
                    }

                    var bytes = await client.DownloadThumbnailBytesAsync(url).ConfigureAwait(true);
                    if (requestId != _previewRequestId)
                        return;

                    ThumbnailCache.Write(family.id, bytes);
                    ShowPreviewFromFile(ThumbnailCache.GetFilePath(family.id));
                    _previewHint.Text = family.Category;
                }
            }
            catch (Exception ex)
            {
                if (requestId != _previewRequestId)
                    return;
                _previewHint.Text = $"\u041f\u0440\u0435\u0432\u044c\u044e: {ex.Message}";
            }
        }

        private void ShowPreviewFromFile(string path)
        {
            if (!File.Exists(path))
                return;

            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.UriSource = new Uri(path, UriKind.Absolute);
            bitmap.EndInit();
            bitmap.Freeze();
            _previewImage.Source = bitmap;
        }

        #endregion
    }
}
