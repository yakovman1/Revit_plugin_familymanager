using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;

namespace FamilyMang
{
    public class UploadWindow : Window
    {
        private readonly PluginSettings _settings;

        private TextBox _urlBox;
        private TextBox _tokenBox;
        private TextBox _projectBox;
        private DataGrid _grid;
        private Button _uploadBtn;
        private TextBlock _statusText;

        public int SelectedElementId { get; private set; } = -1;

        public UploadWindow(List<FamilyDisplayItem> families)
        {
            _settings = PluginSettings.Load();
            SetupWindow();
            BuildLayout(families);
            RestoreSettings();
        }

        private void SetupWindow()
        {
            Title = "FamilyMang \u2014 \u0417\u0430\u0433\u0440\u0443\u0437\u043a\u0430 \u0441\u0435\u043c\u0435\u0439\u0441\u0442\u0432\u0430 \u0432 \u0445\u0440\u0430\u043d\u0438\u043b\u0438\u0449\u0435";
            Width = 800;
            Height = 540;
            MinWidth = 650;
            MinHeight = 380;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            FontFamily = new FontFamily("Segoe UI");
            FontSize = 13;
            Background = new SolidColorBrush(Color.FromRgb(250, 250, 250));
        }

        private void BuildLayout(List<FamilyDisplayItem> families)
        {
            var root = new DockPanel();

            var connBar = BuildConnectionBar();
            DockPanel.SetDock(connBar, Dock.Top);
            root.Children.Add(connBar);

            var actionBar = BuildActionBar();
            DockPanel.SetDock(actionBar, Dock.Bottom);
            root.Children.Add(actionBar);

            var grid = BuildDataGrid();
            grid.ItemsSource = families;
            root.Children.Add(grid);

            Content = root;
        }

        #region UI Construction

        private Border BuildConnectionBar()
        {
            var grid = new Grid { Margin = new Thickness(12, 10, 12, 6) };

            grid.ColumnDefinitions.Add(Col(GridUnitType.Auto));
            grid.ColumnDefinitions.Add(Col(GridUnitType.Star, 1));
            grid.ColumnDefinitions.Add(Col(GridUnitType.Auto));
            grid.ColumnDefinitions.Add(Col(GridUnitType.Star, 1));

            grid.RowDefinitions.Add(new RowDefinition());
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(6) });
            grid.RowDefinitions.Add(new RowDefinition());

            Put(grid, Lbl("\u0421\u0435\u0440\u0432\u0435\u0440:"), 0, 0);
            _urlBox = Txt(); Put(grid, _urlBox, 0, 1);

            Put(grid, Lbl("Project ID:", 12), 0, 2);
            _projectBox = Txt(); Put(grid, _projectBox, 0, 3);

            Put(grid, Lbl("\u0422\u043e\u043a\u0435\u043d:"), 2, 0);
            _tokenBox = Txt();
            Grid.SetColumnSpan(_tokenBox, 3);
            Put(grid, _tokenBox, 2, 1);

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
            _grid = new DataGrid
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

            _grid.Columns.Add(new DataGridTextColumn
            {
                Header = "\u0418\u043c\u044f \u0441\u0435\u043c\u0435\u0439\u0441\u0442\u0432\u0430",
                Binding = new Binding("Name"),
                Width = new DataGridLength(2, DataGridLengthUnitType.Star)
            });
            _grid.Columns.Add(new DataGridTextColumn
            {
                Header = "\u041a\u0430\u0442\u0435\u0433\u043e\u0440\u0438\u044f",
                Binding = new Binding("CategoryName"),
                Width = new DataGridLength(1, DataGridLengthUnitType.Star)
            });

            _grid.SelectionChanged += (s, e) =>
            {
                if (_uploadBtn != null) _uploadBtn.IsEnabled = _grid.SelectedItem != null;
            };

            return _grid;
        }

        private DockPanel BuildActionBar()
        {
            var panel = new DockPanel { Margin = new Thickness(12, 6, 12, 8) };

            var closeBtn = Btn("\u0417\u0430\u043a\u0440\u044b\u0442\u044c", false);
            closeBtn.Click += (s, e) => { PersistSettings(); DialogResult = false; };
            DockPanel.SetDock(closeBtn, Dock.Right);
            panel.Children.Add(closeBtn);

            _uploadBtn = Btn("\u0417\u0430\u0433\u0440\u0443\u0437\u0438\u0442\u044c \u0432 \u0445\u0440\u0430\u043d\u0438\u043b\u0438\u0449\u0435", true);
            _uploadBtn.IsEnabled = false;
            _uploadBtn.Margin = new Thickness(0, 0, 8, 0);
            _uploadBtn.Click += OnUploadClick;
            DockPanel.SetDock(_uploadBtn, Dock.Right);
            panel.Children.Add(_uploadBtn);

            _statusText = new TextBlock
            {
                Text = "\u0412\u044b\u0431\u0435\u0440\u0438\u0442\u0435 \u0441\u0435\u043c\u0435\u0439\u0441\u0442\u0432\u043e \u0438\u0437 \u0441\u043f\u0438\u0441\u043a\u0430",
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Foreground = new SolidColorBrush(Color.FromRgb(120, 120, 120))
            };
            panel.Children.Add(_statusText);

            return panel;
        }

        #endregion

        #region Helpers

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

        private static TextBlock Lbl(string text, double left = 0) =>
            new TextBlock
            {
                Text = text,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(left, 0, 8, 0)
            };

        private static TextBox Txt() =>
            new TextBox
            {
                VerticalContentAlignment = VerticalAlignment.Center,
                Padding = new Thickness(4, 3, 4, 3)
            };

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

        private void OnUploadClick(object sender, RoutedEventArgs e)
        {
            if (!(_grid.SelectedItem is FamilyDisplayItem item)) return;

            if (string.IsNullOrWhiteSpace(_projectBox.Text))
            {
                _statusText.Text = "\u0423\u043a\u0430\u0436\u0438\u0442\u0435 Project ID";
                _statusText.Foreground = new SolidColorBrush(Color.FromRgb(200, 40, 40));
                return;
            }

            SelectedElementId = item.ElementIdValue;
            PersistSettings();
            DialogResult = true;
        }

        #endregion
    }
}
