using System;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FamilyMang
{
    /// <summary>Шапка с логотипом ATP tlp (файл Assets/AtpTlpLogo.png рядом с DLL).</summary>
    public static class BrandingHeader
    {
        private const string LogoFileName = "AtpTlpLogo.png";

        public static Border Create(string subtitle = null)
        {
            var panel = new DockPanel
            {
                Margin = new Thickness(12, 8, 12, 6),
                LastChildFill = true
            };

            var logo = new Image
            {
                Source = TryLoadLogo(),
                Stretch = Stretch.Uniform,
                MaxHeight = 56,
                MaxWidth = 420,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Left
            };
            DockPanel.SetDock(logo, Dock.Left);
            panel.Children.Add(logo);

            if (!string.IsNullOrWhiteSpace(subtitle))
            {
                var text = new TextBlock
                {
                    Text = subtitle,
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    FontSize = 14,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(Color.FromRgb(60, 60, 60)),
                    Margin = new Thickness(16, 0, 0, 0)
                };
                panel.Children.Add(text);
            }

            return new Border
            {
                Child = panel,
                Background = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(220, 220, 220)),
                BorderThickness = new Thickness(0, 0, 0, 1)
            };
        }

        private static ImageSource TryLoadLogo()
        {
            try
            {
                var path = ResolveLogoPath();
                if (string.IsNullOrEmpty(path) || !File.Exists(path))
                    return null;

                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri(path, UriKind.Absolute);
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.EndInit();
                bitmap.Freeze();
                return bitmap;
            }
            catch
            {
                return null;
            }
        }

        private static string ResolveLogoPath()
        {
            var asmDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            if (string.IsNullOrEmpty(asmDir))
                return null;

            var candidates = new[]
            {
                Path.Combine(asmDir, "Assets", LogoFileName),
                Path.Combine(asmDir, LogoFileName)
            };

            foreach (var path in candidates)
            {
                if (File.Exists(path))
                    return path;
            }

            return null;
        }
    }
}
