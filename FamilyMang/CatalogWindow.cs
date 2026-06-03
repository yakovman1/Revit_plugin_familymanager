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
using System.Windows.Threading;

namespace FamilyMang
{
    public class CatalogWindow : Window
    {
        private const int PageSize = 20;

        private readonly PluginSettings _settings;

        private TextBox _urlBox;
        private TextBlock _userLabel;
        private DataGrid _grid;
        private Button _connectBtn;
        private Button _loadBtn;
        private Button _deleteBtn;
        private Button _prevBtn;
        private Button _nextBtn;
        private TextBlock _statusText;
        private TextBlock _pageText;
        private ComboBox _filterCombo;
        private TextBox _searchBox;
        private Button _clearSearchBtn;
        private DispatcherTimer _searchDebounceTimer;
        private TreeView _categoryTree;
        private TabControl _modeTabs;
        private TabItem _deleteTab;
        private bool _canDeleteFamilies;
        private string _selectedCategoryKey = CatalogCategories.AllKey;
        private bool _suppressCategoryTreeEvent;
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
        private readonly HashSet<string> _expandedHostIds =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private List<List<CatalogFamilyRow>> _currentPageGroups = new List<List<CatalogFamilyRow>>();
        private string _selectedHostFamilyId;

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
            Title = "FamilyMang \u2014 \u041a\u0430\u0442\u0430\u043b\u043e\u0433 \u0438 \u0443\u0434\u0430\u043b\u0435\u043d\u0438\u0435 \u0441\u0435\u043c\u0435\u0439\u0441\u0442\u0432";
            Width = 1280;
            Height = 640;
            MinWidth = 1020;
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

            var header = BrandingHeader.Create("FamilyMang \u2014 \u041a\u0430\u0442\u0430\u043b\u043e\u0433 \u0438 \u0443\u0434\u0430\u043b\u0435\u043d\u0438\u0435 \u0441\u0435\u043c\u0435\u0439\u0441\u0442\u0432");
            DockPanel.SetDock(header, Dock.Top);
            root.Children.Add(header);

            var connBar = BuildConnectionBar();
            DockPanel.SetDock(connBar, Dock.Top);
            root.Children.Add(connBar);

            _modeTabs = BuildModeTabs();
            DockPanel.SetDock(_modeTabs, Dock.Top);
            root.Children.Add(_modeTabs);

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

        private TabControl BuildModeTabs()
        {
            var tabs = new TabControl
            {
                Margin = new Thickness(12, 0, 12, 0)
            };
            tabs.Items.Add(new TabItem { Header = "\u041a\u0430\u0442\u0430\u043b\u043e\u0433" });
            _deleteTab = new TabItem
            {
                Header = "\u0423\u0434\u0430\u043b\u0435\u043d\u0438\u0435",
                Visibility = Visibility.Collapsed
            };
            tabs.Items.Add(_deleteTab);
            tabs.SelectionChanged += OnModeTabChanged;
            return tabs;
        }

        private void UpdateDeleteTabAccess(bool canDelete)
        {
            _canDeleteFamilies = canDelete;
            if (_deleteTab == null)
                return;

            _deleteTab.Visibility = canDelete ? Visibility.Visible : Visibility.Collapsed;

            if (!canDelete && _modeTabs != null && _modeTabs.SelectedItem == _deleteTab)
                _modeTabs.SelectedIndex = 0;
        }

        private bool IsDeleteMode => _canDeleteFamilies && _modeTabs != null && _modeTabs.SelectedItem == _deleteTab;

        private void OnModeTabChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loadBtn == null || _deleteBtn == null)
                return;

            ApplyModeUi();
            OnGridSelectionChanged(_grid, e);
        }

        private void ApplyModeUi()
        {
            bool deleteMode = IsDeleteMode;

            _loadBtn.Visibility = deleteMode ? Visibility.Collapsed : Visibility.Visible;
            _deleteBtn.Visibility = deleteMode ? Visibility.Visible : Visibility.Collapsed;

            if (_previewTitle != null)
                _previewTitle.Text = deleteMode ? "\u0423\u0434\u0430\u043b\u0435\u043d\u0438\u0435" : "\u041f\u0440\u0435\u0432\u044c\u044e";

            if (_previewImage != null)
                _previewImage.Visibility = deleteMode ? Visibility.Collapsed : Visibility.Visible;

            if (deleteMode)
            {
                ClearPreview();
                if (_previewHint != null)
                {
                    _previewHint.Text =
                        "\u0412\u044b\u0431\u0435\u0440\u0438\u0442\u0435 \u043e\u0441\u043d\u043e\u0432\u043d\u043e\u0435 \u0441\u0435\u043c\u0435\u0439\u0441\u0442\u0432\u043e \u0438 \u043d\u0430\u0436\u043c\u0438\u0442\u0435 \u00ab\u0423\u0434\u0430\u043b\u0438\u0442\u044c\u00bb.\n" +
                        "\u0411\u0443\u0434\u0443\u0442 \u0443\u0434\u0430\u043b\u0435\u043d\u044b \u0437\u0430\u043f\u0438\u0441\u0438 \u0432 \u0411\u0414, \u0444\u0430\u0439\u043b\u044b \u0432 S3 \u0438 \u0432\u0441\u0435 \u0432\u043b\u043e\u0436\u0435\u043d\u043d\u044b\u0435 \u0441\u0435\u043c\u0435\u0439\u0441\u0442\u0432\u0430 \u0441\u043e\u0441\u0442\u0430\u0432\u0430.";
                }
            }
        }

        private Grid BuildCenterPanel()
        {
            var grid = new Grid { Margin = new Thickness(12, 6, 12, 4) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(240) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(280) });

            var categoryPanel = BuildCategoryTreePanel();
            Grid.SetColumn(categoryPanel, 0);
            grid.Children.Add(categoryPanel);

            var listPanel = new Grid();
            listPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            listPanel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            var searchBar = BuildSearchBar();
            Grid.SetRow(searchBar, 0);
            listPanel.Children.Add(searchBar);

            _grid = BuildDataGrid();
            _grid.Margin = new Thickness(0, 4, 0, 4);
            Grid.SetRow(_grid, 1);
            listPanel.Children.Add(_grid);

            listPanel.Margin = new Thickness(8, 0, 8, 4);
            Grid.SetColumn(listPanel, 1);
            grid.Children.Add(listPanel);

            var preview = BuildPreviewPanel();
            Grid.SetColumn(preview, 2);
            grid.Children.Add(preview);

            return grid;
        }

        private Border BuildSearchBar()
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var label = Label("\u041f\u043e\u0438\u0441\u043a:");
            label.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(label, 0);
            grid.Children.Add(label);

            _searchBox = CreateTextBox();
            _searchBox.Margin = new Thickness(8, 0, 4, 0);
            _searchBox.ToolTip =
                "\u0418\u043c\u044f \u0441\u0435\u043c\u0435\u0439\u0441\u0442\u0432\u0430, \u0444\u0430\u0439\u043b, \u043a\u0430\u0442\u0435\u0433\u043e\u0440\u0438\u044f, \u0437\u0430\u0432\u043e\u0434.\n" +
                "\u041d\u0435\u0441\u043a\u043e\u043b\u044c\u043a\u043e \u0441\u043b\u043e\u0432 \u2014 \u0432\u0441\u0435 \u0441\u043b\u043e\u0432\u0430 \u0434\u043e\u043b\u0436\u043d\u044b \u0432\u0441\u0442\u0440\u0435\u0447\u0430\u0442\u044c\u0441\u044f.";
            _searchBox.TextChanged += OnSearchTextChanged;
            Grid.SetColumn(_searchBox, 1);
            grid.Children.Add(_searchBox);

            _clearSearchBtn = Btn("\u2715", false);
            _clearSearchBtn.Padding = new Thickness(8, 4, 8, 4);
            _clearSearchBtn.Margin = new Thickness(0);
            _clearSearchBtn.IsEnabled = false;
            _clearSearchBtn.ToolTip = "\u041e\u0447\u0438\u0441\u0442\u0438\u0442\u044c \u043f\u043e\u0438\u0441\u043a";
            _clearSearchBtn.Click += OnClearSearchClick;
            Grid.SetColumn(_clearSearchBtn, 2);
            grid.Children.Add(_clearSearchBtn);

            _searchDebounceTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(280)
            };
            _searchDebounceTimer.Tick += OnSearchDebounceTick;

            return new Border
            {
                Child = grid,
                Padding = new Thickness(0, 0, 0, 2)
            };
        }

        private Border BuildCategoryTreePanel()
        {
            var root = new DockPanel();

            var header = new TextBlock
            {
                Text = "\u041a\u0430\u0442\u0435\u0433\u043e\u0440\u0438\u0438",
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 8),
                Foreground = new SolidColorBrush(Color.FromRgb(60, 60, 60))
            };
            DockPanel.SetDock(header, Dock.Top);
            root.Children.Add(header);

            _categoryTree = new TreeView
            {
                BorderThickness = new Thickness(0),
                Background = Brushes.White,
                Padding = new Thickness(4, 2, 4, 2)
            };
            _categoryTree.SelectedItemChanged += OnCategoryTreeSelectionChanged;

            var scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = _categoryTree
            };
            root.Children.Add(scroll);

            return new Border
            {
                Child = root,
                BorderBrush = new SolidColorBrush(Color.FromRgb(200, 200, 200)),
                BorderThickness = new Thickness(0, 0, 1, 0),
                Padding = new Thickness(0, 0, 8, 0),
                Background = new SolidColorBrush(Color.FromRgb(252, 252, 252))
            };
        }

        private void RebuildCategoryTree(List<List<CatalogFamilyRow>> allGroups)
        {
            if (_categoryTree == null)
                return;

            _suppressCategoryTreeEvent = true;
            var folders = CatalogCategories.BuildFolderTree(allGroups);
            _categoryTree.Items.Clear();

            foreach (var folder in folders)
                _categoryTree.Items.Add(CreateFolderTreeItem(folder));

            if (!string.IsNullOrWhiteSpace(_selectedCategoryKey) &&
                !TrySelectTreeItemByKey(_selectedCategoryKey))
            {
                _selectedCategoryKey = CatalogCategories.AllKey;
                TrySelectTreeItemByKey(_selectedCategoryKey);
            }
            else if (_categoryTree.Items.Count > 0 && _categoryTree.SelectedItem == null)
            {
                _selectedCategoryKey = CatalogCategories.AllKey;
                TrySelectTreeItemByKey(_selectedCategoryKey);
            }

            _suppressCategoryTreeEvent = false;
        }

        private TreeViewItem CreateFolderTreeItem(CategoryFolderItem folder)
        {
            var item = new TreeViewItem
            {
                Header = CreateCategoryHeader(folder),
                Tag = folder,
                Padding = new Thickness(2, 4, 2, 4),
                IsExpanded = folder.IsAll || folder.IsAnnotationSection || folder.IsFamilySection ||
                               folder.Children.Count > 0
            };

            foreach (var child in folder.Children)
                item.Items.Add(CreateFolderTreeItem(child));

            return item;
        }

        private bool TrySelectTreeItemByKey(string filterKey)
        {
            foreach (TreeViewItem root in _categoryTree.Items)
            {
                if (TrySelectTreeItemByKey(root, filterKey))
                    return true;
            }

            return false;
        }

        private static bool TrySelectTreeItemByKey(TreeViewItem node, string filterKey)
        {
            if (node.Tag is CategoryFolderItem folder &&
                string.Equals(folder.Key, filterKey, StringComparison.OrdinalIgnoreCase))
            {
                node.IsSelected = true;
                node.BringIntoView();
                return true;
            }

            foreach (TreeViewItem child in node.Items)
            {
                if (TrySelectTreeItemByKey(child, filterKey))
                    return true;
            }

            return false;
        }

        private void ClearCategoryTree()
        {
            if (_categoryTree != null)
                _categoryTree.Items.Clear();
        }

        private static StackPanel CreateCategoryHeader(CategoryFolderItem folder)
        {
            var icon = folder.IsAll ? "\uD83D\uDCC1 "
                : folder.IsAnnotationSection ? "\uD83C\uDFF7 "
                : folder.IsFamilySection ? "\uD83C\uDFE0 "
                : folder.IsAnnotationCategory ? "\u25A6 "
                : folder.IsManufacturerNode ? "\uD83C\uDFED "
                : "\uD83D\uDCC2 ";

            var panel = new StackPanel { Orientation = Orientation.Horizontal };
            panel.Children.Add(new TextBlock
            {
                Text = icon,
                Margin = new Thickness(0, 0, 6, 0),
                VerticalAlignment = VerticalAlignment.Center
            });
            panel.Children.Add(new TextBlock
            {
                Text = folder.FolderLabel,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                ToolTip = folder.IsManufacturerNode
                    ? folder.CategoryName + " \u2192 " + folder.DisplayName
                    : folder.DisplayName
            });
            return panel;
        }

        private async void OnCategoryTreeSelectionChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (_suppressCategoryTreeEvent)
                return;

            if (_categoryTree?.SelectedItem is TreeViewItem node &&
                node.Tag is CategoryFolderItem folder)
            {
                if (string.Equals(_selectedCategoryKey, folder.Key, StringComparison.OrdinalIgnoreCase))
                    return;
                _selectedCategoryKey = folder.Key;
            }
            else
            {
                return;
            }

            if (_cachedFamilies == null || _cachedFamilies.Count == 0)
                return;

            _offset = 0;
            await LoadPageAsync();
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

            Put(grid, Label("\u041f\u043e\u043b\u044c\u0437\u043e\u0432\u0430\u0442\u0435\u043b\u044c:", 12), 0, 2);
            _userLabel = new TextBlock
            {
                Text = Environment.UserName,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = new SolidColorBrush(Color.FromRgb(80, 80, 80))
            };
            Put(grid, _userLabel, 0, 3);

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
                AlternatingRowBackground = Brushes.White,
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(Color.FromRgb(200, 200, 200)),
                Margin = new Thickness(12, 6, 12, 4),
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                RowStyle = BuildGridRowStyle()
            };

            dg.Resources[SystemColors.HighlightBrushKey] =
                new SolidColorBrush(Color.FromRgb(228, 228, 228));
            dg.Resources[SystemColors.HighlightTextBrushKey] = Brushes.Black;
            dg.Resources[SystemColors.InactiveSelectionHighlightBrushKey] =
                new SolidColorBrush(Color.FromRgb(240, 240, 240));
            dg.Resources[SystemColors.InactiveSelectionHighlightTextBrushKey] = Brushes.Black;

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

        private static Style BuildGridRowStyle()
        {
            var style = new Style(typeof(DataGridRow));
            style.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.White));
            style.Setters.Add(new Setter(Control.ForegroundProperty, new SolidColorBrush(Color.FromRgb(30, 30, 30))));

            var selected = new Trigger
            {
                Property = DataGridRow.IsSelectedProperty,
                Value = true
            };
            selected.Setters.Add(new Setter(Control.BackgroundProperty,
                new SolidColorBrush(Color.FromRgb(228, 228, 228))));
            selected.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.Black));
            selected.Setters.Add(new Setter(Control.BorderBrushProperty,
                new SolidColorBrush(Color.FromRgb(90, 90, 90))));
            selected.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(3, 0, 0, 0)));
            style.Triggers.Add(selected);

            return style;
        }

        private void OnGridLoadingRow(object sender, DataGridRowEventArgs e)
        {
            if (!(e.Row.Item is CatalogFamilyRow row))
                return;

            ApplyRowVisual(e.Row, row);
        }

        private void ApplyRowVisual(DataGridRow gridRow, CatalogFamilyRow row)
        {
            if (row.IsNested)
            {
                gridRow.IsEnabled = false;
                gridRow.FontWeight = FontWeights.Normal;
                gridRow.FontSize = 12.5;
                gridRow.Foreground = new SolidColorBrush(Color.FromRgb(100, 100, 100));
                gridRow.Background = new SolidColorBrush(Color.FromRgb(250, 250, 250));
                return;
            }

            gridRow.FontWeight = FontWeights.SemiBold;
            gridRow.FontSize = 13;

            var isSelected = !string.IsNullOrEmpty(_selectedHostFamilyId) &&
                             row.Family != null &&
                             string.Equals(row.Family.id, _selectedHostFamilyId, StringComparison.OrdinalIgnoreCase);

            if (isSelected)
            {
                gridRow.Background = new SolidColorBrush(Color.FromRgb(228, 228, 228));
                gridRow.Foreground = Brushes.Black;
                gridRow.BorderBrush = new SolidColorBrush(Color.FromRgb(90, 90, 90));
                gridRow.BorderThickness = new Thickness(3, 0, 0, 0);
            }
            else
            {
                gridRow.Background = Brushes.White;
                gridRow.Foreground = new SolidColorBrush(Color.FromRgb(30, 30, 30));
                gridRow.BorderThickness = new Thickness(0);
            }
        }

        private void RefreshGridRowVisuals()
        {
            if (_grid == null)
                return;

            foreach (var item in _grid.Items)
            {
                var container = _grid.ItemContainerGenerator.ContainerFromItem(item) as DataGridRow;
                if (container?.Item is CatalogFamilyRow row)
                    ApplyRowVisual(container, row);
            }
        }

        private void OnGridSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loadBtn == null)
                return;

            if (_grid.SelectedItem is CatalogFamilyRow row && row.IsHostRow)
            {
                _selectedHostFamilyId = row.Family?.id;
                UpdatePrimaryActionButtons(row);
                RefreshGridRowVisuals();
                if (!IsDeleteMode)
                    _ = LoadPreviewForSelectionAsync(row);
                else
                    UpdateDeletePreview(row);
            }
            else
            {
                _selectedHostFamilyId = null;
                UpdatePrimaryActionButtons(null);
                RefreshGridRowVisuals();
                ClearPreview();
            }
        }

        private void UpdatePrimaryActionButtons(CatalogFamilyRow row)
        {
            bool hostSelected = row != null && row.IsHostRow;
            if (IsDeleteMode)
            {
                _deleteBtn.IsEnabled = hostSelected;
                _loadBtn.IsEnabled = false;
            }
            else
            {
                _loadBtn.IsEnabled = hostSelected;
                _deleteBtn.IsEnabled = false;
            }
        }

        private void UpdateDeletePreview(CatalogFamilyRow row)
        {
            if (_previewHint == null || row?.Family == null)
                return;

            var nested = row.NestedCount;
            _previewHint.Text =
                $"\u0421\u0435\u043c\u0435\u0439\u0441\u0442\u0432\u043e: {row.Family.FamilyName}\n" +
                $"\u041a\u0430\u0442\u0435\u0433\u043e\u0440\u0438\u044f: {row.Category}\n" +
                $"\u0412\u0435\u0440\u0441\u0438\u044f: {row.VersionDisplay}\n\n" +
                (nested > 0
                    ? $"\u0411\u0443\u0434\u0435\u0442 \u0443\u0434\u0430\u043b\u0435\u043d\u043e: 1 \u043e\u0441\u043d\u043e\u0432\u043d\u043e\u0435 + {nested} \u0432\u043b\u043e\u0436\u0435\u043d\u043d\u044b\u0445."
                    : "\u0411\u0443\u0434\u0435\u0442 \u0443\u0434\u0430\u043b\u0435\u043d\u043e \u043e\u0434\u043d\u043e \u043e\u0441\u043d\u043e\u0432\u043d\u043e\u0435 \u0441\u0435\u043c\u0435\u0439\u0441\u0442\u0432\u043e.");
        }

        private void OnGridMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (TryGetRowFromClick(e, out var row) && row.IsHostRow)
            {
                if (row.NestedCount > 0 && !string.IsNullOrWhiteSpace(row.Family?.id))
                    ToggleHostExpanded(row.Family.id);

                _selectedHostFamilyId = row.Family?.id;
                _grid.SelectedItem = row;
                UpdatePrimaryActionButtons(row);
                RefreshGridRowVisuals();
                if (!IsDeleteMode)
                    _ = LoadPreviewForSelectionAsync(row);
                else
                    UpdateDeletePreview(row);
            }
        }

        private void OnGridPreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (TryGetRowFromClick(e, out var row) && row.IsNested)
                e.Handled = true;
        }

        private bool TryGetRowFromClick(MouseButtonEventArgs e, out CatalogFamilyRow row)
        {
            row = null;
            var dep = e.OriginalSource as DependencyObject;
            while (dep != null && !(dep is DataGridRow))
                dep = VisualTreeHelper.GetParent(dep);

            if (dep is DataGridRow gridRow && gridRow.Item is CatalogFamilyRow item)
            {
                row = item;
                return true;
            }

            return false;
        }

        private void ToggleHostExpanded(string hostFamilyId)
        {
            if (string.IsNullOrWhiteSpace(hostFamilyId))
                return;

            if (_expandedHostIds.Contains(hostFamilyId))
                _expandedHostIds.Remove(hostFamilyId);
            else
                _expandedHostIds.Add(hostFamilyId);

            RebindCurrentPageGrid(hostFamilyId);
        }

        private void RebindCurrentPageGrid(string selectHostFamilyId = null)
        {
            if (_currentPageGroups == null || _currentPageGroups.Count == 0)
                return;

            var rows = FlattenGroupsForGrid(_currentPageGroups);
            _grid.ItemsSource = rows;

            if (!string.IsNullOrWhiteSpace(selectHostFamilyId))
            {
                var selected = rows.FirstOrDefault(r =>
                    r.IsHostRow &&
                    string.Equals(r.Family?.id, selectHostFamilyId, StringComparison.OrdinalIgnoreCase));
                _grid.SelectedItem = selected;
                _selectedHostFamilyId = selected?.Family?.id;
            }

            RefreshGridRowVisuals();
        }

        private List<CatalogFamilyRow> FlattenGroupsForGrid(IEnumerable<List<CatalogFamilyRow>> groups)
        {
            var result = new List<CatalogFamilyRow>();
            foreach (var group in groups)
            {
                var prepared = ApplyBundleVersion(ToRowsWithFavorites(group.ToList()));
                var host = prepared.FirstOrDefault(r => r.IsHostRow);
                if (host?.Family == null)
                    continue;

                var nested = prepared.Where(r => r.IsNested).ToList();
                var hostId = host.Family.id;

                host.HostFamilyId = hostId;
                host.NestedCount = nested.Count;
                host.IsExpanded = nested.Count > 0 && _expandedHostIds.Contains(hostId);

                foreach (var child in nested)
                    child.HostFamilyId = hostId;

                result.Add(host);
                if (host.IsExpanded)
                    result.AddRange(nested);
            }

            return result;
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

            _deleteBtn = Btn("\u0423\u0434\u0430\u043b\u0438\u0442\u044c", true);
            _deleteBtn.IsEnabled = false;
            _deleteBtn.Margin = new Thickness(0, 0, 8, 0);
            _deleteBtn.Visibility = Visibility.Collapsed;
            _deleteBtn.Background = new SolidColorBrush(Color.FromRgb(200, 50, 50));
            _deleteBtn.BorderBrush = new SolidColorBrush(Color.FromRgb(160, 40, 40));
            _deleteBtn.Click += OnDeleteClick;
            DockPanel.SetDock(_deleteBtn, Dock.Right);
            panel.Children.Add(_deleteBtn);

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
        }

        private void PersistSettings()
        {
            _settings.ServerUrl = _urlBox.Text.Trim();
            _settings.Save();
        }

        private void SaveAndClose(bool result)
        {
            PersistSettings();
            Close();
        }

        private async void OnConnectClick(object sender, RoutedEventArgs e)
        {
            PersistSettings();
            _offset = 0;
            _cachedFamilies = null;
            _favoriteIds.Clear();
            _expandedHostIds.Clear();
            _selectedCategoryKey = CatalogCategories.AllKey;
            _searchDebounceTimer?.Stop();
            if (_searchBox != null)
                _searchBox.Text = "";
            if (_clearSearchBtn != null)
                _clearSearchBtn.IsEnabled = false;
            await LoadPageAsync();
        }

        private async void OnFilterChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_cachedFamilies == null || _cachedFamilies.Count == 0)
                return;
            _offset = 0;
            RebuildCategoryTree(GetGroupsForCategoryTree());
            await LoadPageAsync();
        }

        private void OnSearchTextChanged(object sender, TextChangedEventArgs e)
        {
            if (_clearSearchBtn != null)
                _clearSearchBtn.IsEnabled = !string.IsNullOrWhiteSpace(_searchBox?.Text);

            if (_cachedFamilies == null || _cachedFamilies.Count == 0)
                return;

            _searchDebounceTimer?.Stop();
            _searchDebounceTimer?.Start();
        }

        private async void OnSearchDebounceTick(object sender, EventArgs e)
        {
            _searchDebounceTimer?.Stop();
            if (_cachedFamilies == null || _cachedFamilies.Count == 0)
                return;

            _offset = 0;
            RebuildCategoryTree(GetGroupsForCategoryTree());
            await LoadPageAsync();
        }

        private async void OnClearSearchClick(object sender, RoutedEventArgs e)
        {
            if (_searchBox == null)
                return;

            _searchDebounceTimer?.Stop();
            _searchBox.Text = "";
            _clearSearchBtn.IsEnabled = false;

            if (_cachedFamilies == null || _cachedFamilies.Count == 0)
                return;

            _offset = 0;
            RebuildCategoryTree(GetGroupsForCategoryTree());
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
            Busy(true);
            Status("\u0417\u0430\u0433\u0440\u0443\u0437\u043a\u0430\u2026", false);

            try
            {
                using (var auth = new JwtAuthService(_urlBox.Text.Trim()))
                using (var client = new ApiClient(auth))
                {
                    client.BaseUrl = _urlBox.Text.Trim();
                    await auth.GetTokenAsync().ConfigureAwait(true);
                    UpdateDeleteTabAccess(auth.HasPermission(FamilyPermissions.DeleteFamilies));

                    var isNewCatalogLoad = _cachedFamilies == null;
                    if (isNewCatalogLoad)
                    {
                        _cachedFamilies = await client.GetAllFamiliesAsync().ConfigureAwait(true);
                        await LoadFavoritesAsync(client).ConfigureAwait(true);
                    }

                    _primaryGroups = CatalogHierarchy.BuildPrimaryGroups(_cachedFamilies);
                    if (isNewCatalogLoad)
                        RebuildCategoryTree(GetGroupsForCategoryTree());
                    var visibleGroups = FilterGroups(_primaryGroups);
                    _total = visibleGroups.Count;

                    _currentPageGroups = visibleGroups
                        .Skip(_offset)
                        .Take(PageSize)
                        .ToList();

                    ApplySearchExpansionHints(_currentPageGroups);
                    var rows = FlattenGroupsForGrid(_currentPageGroups);
                    _grid.ItemsSource = rows;
                    _grid.SelectedItem = null;
                    _selectedHostFamilyId = null;
                    ClearPreview();
                    RefreshPagination();

                    int nestedOnPage = rows.Count(r => r.IsNested);
                    int primaryOnPage = rows.Count - nestedOnPage;
                    var filterHint = FormatActiveFiltersHint();
                    var accessHint = BuildCatalogAccessHint(auth, _cachedFamilies);
                    Status(
                        $"\u0417\u0430\u0433\u0440\u0443\u0436\u0435\u043d\u043e \u0433\u0440\u0443\u043f\u043f: {primaryOnPage} \u043e\u0441\u043d\u043e\u0432\u043d\u044b\u0445" +
                        (nestedOnPage > 0 ? $", {nestedOnPage} \u0432\u043b\u043e\u0436\u0435\u043d\u043d\u044b\u0445" : "") +
                        $" (\u0432\u0441\u0435\u0433\u043e: {_total}{filterHint}){accessHint}",
                        false);
                }
            }
            catch (AuthException ex)
            {
                UpdateDeleteTabAccess(false);
                Status(ex.StatusCode == 403
                    ? "\u0414\u043e\u0441\u0442\u0443\u043f \u0437\u0430\u043f\u0440\u0435\u0449\u0451\u043d: \u0434\u043e\u0431\u0430\u0432\u044c\u0442\u0435 " + Environment.UserName +
                      " \u0432 whitelist FamilyMang"
                    : $"\u041e\u0448\u0438\u0431\u043a\u0430 \u0430\u0443\u0442\u0435\u043d\u0442\u0438\u0444\u0438\u043a\u0430\u0446\u0438\u0438: {ex.Message}", true);
                _grid.ItemsSource = null;
                _cachedFamilies = null;
                _primaryGroups.Clear();
                _total = 0;
                ClearCategoryTree();
                RefreshPagination();
            }
            catch (Exception ex)
            {
                UpdateDeleteTabAccess(false);
                Status($"\u041e\u0448\u0438\u0431\u043a\u0430: {ex.Message}", true);
                _grid.ItemsSource = null;
                _cachedFamilies = null;
                _primaryGroups.Clear();
                _total = 0;
                ClearCategoryTree();
                RefreshPagination();
            }
            finally
            {
                Busy(false);
            }
        }

        private async Task ReloadCatalogFromServerAsync(string statusMessage = null)
        {
            if (!string.IsNullOrWhiteSpace(statusMessage))
                Status(statusMessage, false);

            _cachedFamilies = null;
            await LoadPageAsync();
        }

        private async void OnLoadClick(object sender, RoutedEventArgs e)
        {
            if (!(_grid.SelectedItem is CatalogFamilyRow row) || !row.IsHostRow)
                return;

            var selected = row.Family;
            if (selected == null)
                return;

            Busy(true);
            Status($"\u0421\u043a\u0430\u0447\u0438\u0432\u0430\u043d\u0438\u0435 {selected.original_filename}\u2026", false);

            try
            {
                using (var auth = new JwtAuthService(_urlBox.Text.Trim()))
                using (var client = new ApiClient(auth))
                {
                    client.BaseUrl = _urlBox.Text.Trim();
                    var rfaPath = await FamilyLoader.DownloadAsync(
                        client, selected.id, selected.original_filename).ConfigureAwait(true);

                    FamilyLoadHandler.Schedule(rfaPath, (ok, msg) =>
                    {
                        Dispatcher.BeginInvoke(new Action(async () =>
                        {
                            if (!ok)
                            {
                                Status(msg, true);
                                Busy(false);
                                return;
                            }

                            await ReloadCatalogFromServerAsync(msg + " Обновление списка…");
                        }));
                    });
                }
            }
            catch (Exception ex)
            {
                Status($"\u041e\u0448\u0438\u0431\u043a\u0430 \u0441\u043a\u0430\u0447\u0438\u0432\u0430\u043d\u0438\u044f: {ex.Message}", true);
                Busy(false);
            }
        }

        private async void OnDeleteClick(object sender, RoutedEventArgs e)
        {
            if (!IsDeleteMode)
                return;

            if (!(_grid.SelectedItem is CatalogFamilyRow row) || !row.IsHostRow || row.Family == null)
                return;

            var family = row.Family;
            var familyName = family.FamilyName ?? family.original_filename ?? family.id;
            var nestedCount = row.NestedCount;

            var message =
                $"\u0423\u0434\u0430\u043b\u0438\u0442\u044c \u00ab{familyName}\u00bb \u0438\u0437 \u0411\u0414 \u0438 S3?\n\n" +
                (nestedCount > 0
                    ? $"\u0411\u0443\u0434\u0443\u0442 \u0443\u0434\u0430\u043b\u0435\u043d\u044b \u0437\u0430\u043f\u0438\u0441\u0438: 1 \u043e\u0441\u043d\u043e\u0432\u043d\u043e\u0435 + {nestedCount} \u0432\u043b\u043e\u0436\u0435\u043d\u043d\u044b\u0445.\n\n"
                    : "") +
                "\u0414\u0435\u0439\u0441\u0442\u0432\u0438\u0435 \u043d\u0435\u043e\u0431\u0440\u0430\u0442\u0438\u043c\u043e.";

            if (MessageBox.Show(this, message, "FamilyMang \u2014 \u043f\u043e\u0434\u0442\u0432\u0435\u0440\u0436\u0434\u0435\u043d\u0438\u0435",
                    MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes)
                return;

            Busy(true);
            Status($"\u0423\u0434\u0430\u043b\u0435\u043d\u0438\u0435 \u00ab{familyName}\u00bb\u2026", false);

            try
            {
                using (var auth = new JwtAuthService(_urlBox.Text.Trim()))
                using (var client = new ApiClient(auth))
                {
                    client.BaseUrl = _urlBox.Text.Trim();
                    var result = await client.DeleteFamilyAsync(family.id).ConfigureAwait(true);

                    RemoveDeletedFamiliesFromCache(result?.deleted_family_ids);
                    _offset = 0;
                    await LoadPageAsync();

                    int deletedCount = result?.deleted_family_ids?.Count ?? 0;
                    MessageBox.Show(this,
                        $"\u0421\u0435\u043c\u0435\u0439\u0441\u0442\u0432\u043e \u00ab{familyName}\u00bb \u0443\u0434\u0430\u043b\u0435\u043d\u043e.\n" +
                        $"\u0417\u0430\u043f\u0438\u0441\u0435\u0439 \u0432 \u0411\u0414: {deletedCount}\n" +
                        $"S3 \u043e\u0431\u044a\u0435\u043a\u0442\u043e\u0432: {result?.deleted_s3_objects?.Count ?? 0}",
                        "FamilyMang",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                Status($"\u041e\u0448\u0438\u0431\u043a\u0430 \u0443\u0434\u0430\u043b\u0435\u043d\u0438\u044f: {ex.Message}", true);
                MessageBox.Show(this, ex.Message, "FamilyMang \u2014 \u043e\u0448\u0438\u0431\u043a\u0430",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                Busy(false);
            }
        }

        private void RemoveDeletedFamiliesFromCache(IList<string> deletedFamilyIds)
        {
            if (_cachedFamilies == null || deletedFamilyIds == null || deletedFamilyIds.Count == 0)
                return;

            var ids = new HashSet<string>(deletedFamilyIds, StringComparer.OrdinalIgnoreCase);
            _cachedFamilies.RemoveAll(f => f != null && ids.Contains(f.id));

            foreach (var id in ids)
                _favoriteIds.Remove(id);
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
            _urlBox.IsEnabled = !busy;
            if (_filterCombo != null)
                _filterCombo.IsEnabled = !busy;
            if (_searchBox != null)
                _searchBox.IsEnabled = !busy;
            if (_clearSearchBtn != null)
                _clearSearchBtn.IsEnabled = !busy && !string.IsNullOrWhiteSpace(_searchBox?.Text);
            if (_categoryTree != null)
                _categoryTree.IsEnabled = !busy;
            if (_modeTabs != null)
                _modeTabs.IsEnabled = !busy;

            if (!busy)
            {
                var row = _grid?.SelectedItem as CatalogFamilyRow;
                UpdatePrimaryActionButtons(row != null && row.IsHostRow ? row : null);
            }
            else
            {
                if (_loadBtn != null) _loadBtn.IsEnabled = false;
                if (_deleteBtn != null) _deleteBtn.IsEnabled = false;
            }

            Cursor = busy ? Cursors.Wait : null;
        }

        private bool ShowFavoritesOnly =>
            _filterCombo != null && _filterCombo.SelectedIndex == 1;

        private static string FormatFilterHint(string filterKey)
        {
            if (string.IsNullOrWhiteSpace(filterKey) ||
                string.Equals(filterKey, CatalogCategories.AllKey, StringComparison.OrdinalIgnoreCase))
                return "";

            if (filterKey.IndexOf("|mfr:", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                var parts = filterKey.Split('|');
                string cat = "", mfr = "";
                foreach (var part in parts)
                {
                    if (part.StartsWith("cat:", StringComparison.OrdinalIgnoreCase))
                        cat = part.Substring(4);
                    else if (part.StartsWith("mfr:", StringComparison.OrdinalIgnoreCase))
                        mfr = part.Substring(4);
                }

                return string.IsNullOrEmpty(mfr)
                    ? " \u2022 " + cat
                    : " \u2022 " + cat + " / " + mfr;
            }

            if (string.Equals(filterKey, CatalogFamilyClassification.AnnotationSectionKey,
                    StringComparison.OrdinalIgnoreCase))
                return " \u2022 " + CatalogFamilyClassification.AnnotationSectionLabel;

            if (string.Equals(filterKey, CatalogFamilyClassification.FamilySectionKey,
                    StringComparison.OrdinalIgnoreCase))
                return " \u2022 " + CatalogFamilyClassification.FamilySectionLabel;

            if (filterKey.StartsWith("ann:", StringComparison.OrdinalIgnoreCase))
                return " \u2022 Annotation / " + filterKey.Substring(4);

            if (filterKey.StartsWith("cat:", StringComparison.OrdinalIgnoreCase))
                return " \u2022 " + filterKey.Substring(4);

            return " \u2022 " + filterKey;
        }

        private string CurrentSearchQuery =>
            CatalogSearch.NormalizeQuery(_searchBox?.Text);

        private List<List<CatalogFamilyRow>> GetGroupsForCategoryTree()
        {
            var groups = _primaryGroups;
            if (ShowFavoritesOnly)
                groups = FilterFavoritesOnly(groups);
            return CatalogSearch.FilterGroups(groups, CurrentSearchQuery);
        }

        private List<List<CatalogFamilyRow>> FilterGroups(List<List<CatalogFamilyRow>> groups)
        {
            var result = groups;

            if (ShowFavoritesOnly)
                result = FilterFavoritesOnly(result);

            result = CatalogSearch.FilterGroups(result, CurrentSearchQuery);
            result = CatalogCategories.FilterByCategory(result, _selectedCategoryKey);
            return result;
        }

        private List<List<CatalogFamilyRow>> FilterFavoritesOnly(List<List<CatalogFamilyRow>> groups)
        {
            return groups
                .Where(g => g.Count > 0 &&
                            g[0].Family != null &&
                            _favoriteIds.Contains(g[0].Family.id))
                .ToList();
        }

        private string BuildCatalogAccessHint(JwtAuthService auth, List<FamilySummaryDto> families)
        {
            var parts = new List<string>();
            if (!string.IsNullOrEmpty(auth?.WindowsUser))
                parts.Add("Windows: " + auth.WindowsUser);

            parts.Add("\u043e\u0431\u0449\u0438\u0439 \u043a\u0430\u0442\u0430\u043b\u043e\u0433");

            var projectIds = (families ?? new List<FamilySummaryDto>())
                .Select(f => f?.project_id)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (projectIds.Count == 1)
                parts.Add("project_id: " + ShortId(projectIds[0]));
            else if (projectIds.Count > 1)
                parts.Add("\u26a0 \u0440\u0430\u0437\u043d\u044b\u0435 project_id \u0432 \u043a\u0430\u0442\u0430\u043b\u043e\u0433\u0435");

            if ((families == null || families.Count == 0) && _total == 0 &&
                string.IsNullOrEmpty(CurrentSearchQuery) && !ShowFavoritesOnly)
            {
                parts.Add(
                    "\u041f\u0443\u0441\u0442\u043e: \u0434\u043e\u0431\u0430\u0432\u044c\u0442\u0435 " + Environment.UserName +
                    " \u0432 whitelist FamilyMang \u0438\u043b\u0438 \u0436\u0434\u0438\u0442\u0435 backend rev 9");
            }

            return parts.Count == 0 ? "" : " \u2022 " + string.Join(" \u2022 ", parts);
        }

        private static string ShortId(string id)
        {
            if (string.IsNullOrEmpty(id) || id.Length <= 12)
                return id ?? "";
            return id.Substring(0, 8) + "\u2026";
        }

        private string FormatActiveFiltersHint()
        {
            var parts = new List<string>();
            var category = FormatFilterHint(_selectedCategoryKey);
            if (!string.IsNullOrEmpty(category))
                parts.Add(category.Trim().TrimStart('\u2022').Trim());

            var search = CurrentSearchQuery;
            if (!string.IsNullOrEmpty(search))
                parts.Add("\u043f\u043e\u0438\u0441\u043a: \u00ab" + search + "\u00bb");

            if (ShowFavoritesOnly)
                parts.Add("\u0438\u0437\u0431\u0440\u0430\u043d\u043d\u044b\u0435");

            return parts.Count == 0 ? "" : " \u2022 " + string.Join(" \u2022 ", parts);
        }

        private void ApplySearchExpansionHints(IEnumerable<List<CatalogFamilyRow>> groups)
        {
            var query = CurrentSearchQuery;
            if (string.IsNullOrEmpty(query))
                return;

            foreach (var group in groups ?? Enumerable.Empty<List<CatalogFamilyRow>>())
            {
                if (!CatalogSearch.ShouldAutoExpandGroup(group, query))
                    continue;

                var host = group.FirstOrDefault(r => !r.IsNested);
                if (host?.Family?.id != null)
                    _expandedHostIds.Add(host.Family.id);
            }
        }

        private List<CatalogFamilyRow> ToRowsWithFavorites(List<CatalogFamilyRow> group)
        {
            if (group == null)
                return new List<CatalogFamilyRow>();

            foreach (var row in group)
            {
                if (row.Family != null && !row.IsNested)
                    row.IsFavorite = _favoriteIds.Contains(row.Family.id);
            }

            return group;
        }

        private static List<CatalogFamilyRow> ApplyBundleVersion(List<CatalogFamilyRow> group)
        {
            if (group == null || group.Count == 0)
                return group ?? new List<CatalogFamilyRow>();

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
                using (var auth = new JwtAuthService(_urlBox.Text.Trim()))
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
                using (var auth = new JwtAuthService(_urlBox.Text.Trim()))
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
