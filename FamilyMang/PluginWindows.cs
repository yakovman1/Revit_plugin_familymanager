using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Interop;

namespace FamilyMang
{
    internal static class PluginWindows
    {
        private static CatalogWindow _catalog;
        private static UploadWindow _upload;

        public static void ShowCatalog()
        {
            if (_catalog != null)
            {
                if (_catalog.IsVisible)
                {
                    if (_catalog.WindowState == WindowState.Minimized)
                        _catalog.WindowState = WindowState.Normal;
                    _catalog.Activate();
                    return;
                }

                _catalog = null;
            }

            _catalog = new CatalogWindow();
            _catalog.Closed += (s, e) => _catalog = null;
            ShowNonModal(_catalog);
        }

        public static void ShowUpload(FamilyUploadBundle bundle)
        {
            if (_upload != null)
            {
                if (_upload.IsVisible)
                {
                    if (_upload.WindowState == WindowState.Minimized)
                        _upload.WindowState = WindowState.Normal;
                    _upload.Activate();
                    return;
                }

                _upload = null;
            }

            _upload = new UploadWindow(bundle);
            _upload.Closed += (s, e) => _upload = null;
            ShowNonModal(_upload);
        }

        private static void ShowNonModal(Window window)
        {
            IntPtr hwnd = Process.GetCurrentProcess().MainWindowHandle;
            if (hwnd != IntPtr.Zero)
                new WindowInteropHelper(window) { Owner = hwnd };

            window.ShowInTaskbar = false;
            window.Show();
        }
    }
}
