using System;
using System.IO;
using Microsoft.Win32;
using System.Windows;

namespace FamilyMang
{
    internal static class FamilyStoragePaths
    {
        public const string Root =
            @"P:\MOS-TLP\GROUPS\ALLGEMEIN\02_ATP_STANDARDS\07_BIM\02_Families";

        public static bool TryPickStorageFolder(Window owner, out string folderPath, string familyName = null)
        {
            folderPath = null;

            if (!Directory.Exists(Root))
            {
                MessageBox.Show(owner,
                    $"Корневая папка хранилища недоступна:\n{Root}",
                    "FamilyMang",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return false;
            }

            var dlg = new SaveFileDialog
            {
                Title = "Сохранение семейства в хранилище",
                InitialDirectory = Root,
                FileName = BuildSuggestedFileName(familyName),
                Filter = "Revit Family (*.rfa)|*.rfa",
                DefaultExt = "rfa",
                AddExtension = true,
                CheckPathExists = true,
                CheckFileExists = false,
                OverwritePrompt = false,
                CreatePrompt = false,
                ValidateNames = true
            };

            bool? ok = owner != null ? dlg.ShowDialog(owner) : dlg.ShowDialog();
            if (ok != true)
                return false;

            folderPath = Path.GetDirectoryName(dlg.FileName);
            if (string.IsNullOrWhiteSpace(folderPath))
                return false;

            if (!IsUnderRoot(folderPath))
            {
                MessageBox.Show(owner,
                    $"Сохранять можно только внутри хранилища семейств:\n{Root}",
                    "FamilyMang",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return false;
            }

            return true;
        }

        private static string BuildSuggestedFileName(string familyName)
        {
            var name = string.IsNullOrWhiteSpace(familyName) ? "Family" : familyName.Trim();
            foreach (char c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');

            if (!name.EndsWith(".rfa", StringComparison.OrdinalIgnoreCase))
                name += ".rfa";

            return name;
        }

        public static bool IsUnderRoot(string folderPath)
        {
            if (string.IsNullOrWhiteSpace(folderPath))
                return false;

            try
            {
                var fullRoot = Path.GetFullPath(Root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                var fullFolder = Path.GetFullPath(folderPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                if (string.Equals(fullRoot, fullFolder, StringComparison.OrdinalIgnoreCase))
                    return true;
                return fullFolder.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        public static string ToRelativePath(string folderPath)
        {
            if (!IsUnderRoot(folderPath))
                return folderPath ?? "";

            var fullRoot = Path.GetFullPath(Root);
            var fullFolder = Path.GetFullPath(folderPath);
            if (string.Equals(fullRoot, fullFolder, StringComparison.OrdinalIgnoreCase))
                return "";

            return fullFolder.Substring(fullRoot.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
    }
}
