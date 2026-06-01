using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace FamilyMang
{
    public sealed class CategoryFolderItem
    {
        public string Key { get; set; }
        public string DisplayName { get; set; }
        public int Count { get; set; }
        public string CategoryName { get; set; }
        public string ManufacturerName { get; set; }
        public List<CategoryFolderItem> Children { get; set; } = new List<CategoryFolderItem>();

        public bool IsAll => Key == CatalogCategories.AllKey;
        public bool IsManufacturerNode => !string.IsNullOrWhiteSpace(ManufacturerName);

        public string FolderLabel => DisplayName + " (" + Count + ")";
    }

    public static class CatalogCategories
    {
        public const string AllKey = "*";
        public const string ManufacturerParameterName = "ADSK_\u0417\u0430\u0432\u043e\u0434-\u0438\u0437\u0433\u043e\u0442\u043e\u0432\u0438\u0442\u0435\u043b\u044c";

        private const string UncategorizedLabel = "\u0411\u0435\u0437 \u043a\u0430\u0442\u0435\u0433\u043e\u0440\u0438\u0438";
        private const string CategoryPrefix = "cat:";
        private const string ManufacturerPrefix = "mfr:";

        public static string NormalizeCategory(string category)
        {
            if (string.IsNullOrWhiteSpace(category) || category == "\u2014")
                return UncategorizedLabel;
            return category.Trim();
        }

        public static string NormalizeManufacturer(string manufacturer)
        {
            if (string.IsNullOrWhiteSpace(manufacturer))
                return null;
            var text = manufacturer.Trim();
            return text.Length == 0 ? null : text;
        }

        public static string GetHostCategoryKey(IReadOnlyList<CatalogFamilyRow> group)
        {
            if (group == null || group.Count == 0)
                return UncategorizedLabel;
            var host = group.FirstOrDefault(r => !r.IsNested) ?? group[0];
            return NormalizeCategory(host?.Family?.Category);
        }

        public static string GetHostManufacturerKey(IReadOnlyList<CatalogFamilyRow> group)
        {
            if (group == null || group.Count == 0)
                return null;
            var host = group.FirstOrDefault(r => !r.IsNested) ?? group[0];
            return NormalizeManufacturer(host?.Family?.Manufacturer);
        }

        public static string MakeFilterKey(string category, string manufacturer = null)
        {
            var cat = NormalizeCategory(category);
            var mfr = NormalizeManufacturer(manufacturer);
            if (mfr == null)
                return CategoryPrefix + cat;
            return CategoryPrefix + cat + "|" + ManufacturerPrefix + mfr;
        }

        public static List<CategoryFolderItem> BuildFolderTree(IEnumerable<List<CatalogFamilyRow>> groups)
        {
            var source = groups?.Where(g => g != null && g.Count > 0).ToList()
                           ?? new List<List<CatalogFamilyRow>>();

            var tree = new List<CategoryFolderItem>
            {
                new CategoryFolderItem
                {
                    Key = AllKey,
                    DisplayName = "\u0412\u0441\u0435 \u043a\u0430\u0442\u0435\u0433\u043e\u0440\u0438\u0438",
                    Count = source.Count
                }
            };

            var byCategory = source
                .GroupBy(GetHostCategoryKey, StringComparer.OrdinalIgnoreCase)
                .OrderBy(g => g.Key, StringComparer.CurrentCultureIgnoreCase);

            foreach (var categoryGroup in byCategory)
            {
                var categoryName = categoryGroup.Key;
                var categoryNode = new CategoryFolderItem
                {
                    Key = MakeFilterKey(categoryName),
                    DisplayName = categoryName,
                    CategoryName = categoryName,
                    Count = categoryGroup.Count()
                };

                var byManufacturer = categoryGroup
                    .Select(g => new { Group = g, Mfr = GetHostManufacturerKey(g) })
                    .Where(x => x.Mfr != null)
                    .GroupBy(x => x.Mfr, StringComparer.OrdinalIgnoreCase)
                    .OrderBy(g => g.Key, StringComparer.CurrentCultureIgnoreCase);

                foreach (var mfrGroup in byManufacturer)
                {
                    categoryNode.Children.Add(new CategoryFolderItem
                    {
                        Key = MakeFilterKey(categoryName, mfrGroup.Key),
                        DisplayName = mfrGroup.Key,
                        CategoryName = categoryName,
                        ManufacturerName = mfrGroup.Key,
                        Count = mfrGroup.Count()
                    });
                }

                tree.Add(categoryNode);
            }

            return tree;
        }

        public static List<List<CatalogFamilyRow>> FilterByCategory(
            IEnumerable<List<CatalogFamilyRow>> groups,
            string filterKey)
        {
            var list = groups?.ToList() ?? new List<List<CatalogFamilyRow>>();
            if (string.IsNullOrWhiteSpace(filterKey) ||
                string.Equals(filterKey, AllKey, StringComparison.OrdinalIgnoreCase))
                return list;

            ParseFilterKey(filterKey, out var category, out var manufacturer);

            return list.Where(g =>
            {
                if (!string.Equals(GetHostCategoryKey(g), category, StringComparison.OrdinalIgnoreCase))
                    return false;

                if (manufacturer == null)
                    return true;

                return string.Equals(
                    GetHostManufacturerKey(g), manufacturer, StringComparison.OrdinalIgnoreCase);
            }).ToList();
        }

        private static void ParseFilterKey(string filterKey, out string category, out string manufacturer)
        {
            category = UncategorizedLabel;
            manufacturer = null;

            if (string.IsNullOrWhiteSpace(filterKey))
                return;

            var parts = filterKey.Split('|');
            foreach (var part in parts)
            {
                if (part.StartsWith(CategoryPrefix, StringComparison.OrdinalIgnoreCase))
                    category = part.Substring(CategoryPrefix.Length);
                else if (part.StartsWith(ManufacturerPrefix, StringComparison.OrdinalIgnoreCase))
                    manufacturer = part.Substring(ManufacturerPrefix.Length);
            }
        }
    }
}
