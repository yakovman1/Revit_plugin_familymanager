using System;
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
        public bool IsAnnotationSection { get; set; }
        public bool IsFamilySection { get; set; }
        public bool IsAnnotationCategory { get; set; }
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
        private const string AnnotationCategoryPrefix = "ann:";

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

        public static bool IsAnnotationGroup(IReadOnlyList<CatalogFamilyRow> group)
        {
            if (group == null || group.Count == 0)
                return false;
            var host = group.FirstOrDefault(r => !r.IsNested) ?? group[0];
            return CatalogFamilyClassification.IsAnnotationFamily(host?.Family);
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

        private static string MakeAnnotationCategoryKey(string category) =>
            AnnotationCategoryPrefix + NormalizeCategory(category);

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

            var annotationGroups = source.Where(IsAnnotationGroup).ToList();
            var modelGroups = source.Where(g => !IsAnnotationGroup(g)).ToList();

            if (annotationGroups.Count > 0)
            {
                var annotationNode = new CategoryFolderItem
                {
                    Key = CatalogFamilyClassification.AnnotationSectionKey,
                    DisplayName = CatalogFamilyClassification.AnnotationSectionLabel,
                    IsAnnotationSection = true,
                    Count = annotationGroups.Count
                };

                foreach (var categoryGroup in annotationGroups
                             .GroupBy(GetHostCategoryKey, StringComparer.OrdinalIgnoreCase)
                             .OrderBy(g => g.Key, StringComparer.CurrentCultureIgnoreCase))
                {
                    annotationNode.Children.Add(new CategoryFolderItem
                    {
                        Key = MakeAnnotationCategoryKey(categoryGroup.Key),
                        DisplayName = categoryGroup.Key,
                        CategoryName = categoryGroup.Key,
                        IsAnnotationCategory = true,
                        Count = categoryGroup.Count()
                    });
                }

                tree.Add(annotationNode);
            }

            if (modelGroups.Count > 0)
            {
                var familyNode = new CategoryFolderItem
                {
                    Key = CatalogFamilyClassification.FamilySectionKey,
                    DisplayName = CatalogFamilyClassification.FamilySectionLabel,
                    IsFamilySection = true,
                    Count = modelGroups.Count
                };

                AppendModelCategoryChildren(familyNode, modelGroups);
                tree.Add(familyNode);
            }

            return tree;
        }

        private static void AppendModelCategoryChildren(
            CategoryFolderItem familyNode,
            List<List<CatalogFamilyRow>> modelGroups)
        {
            foreach (var categoryGroup in modelGroups
                         .GroupBy(GetHostCategoryKey, StringComparer.OrdinalIgnoreCase)
                         .OrderBy(g => g.Key, StringComparer.CurrentCultureIgnoreCase))
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

                familyNode.Children.Add(categoryNode);
            }
        }

        public static List<List<CatalogFamilyRow>> FilterByCategory(
            IEnumerable<List<CatalogFamilyRow>> groups,
            string filterKey)
        {
            var list = groups?.ToList() ?? new List<List<CatalogFamilyRow>>();
            if (string.IsNullOrWhiteSpace(filterKey) ||
                string.Equals(filterKey, AllKey, StringComparison.OrdinalIgnoreCase))
                return list;

            if (string.Equals(filterKey, CatalogFamilyClassification.AnnotationSectionKey,
                    StringComparison.OrdinalIgnoreCase))
                return list.Where(IsAnnotationGroup).ToList();

            if (string.Equals(filterKey, CatalogFamilyClassification.FamilySectionKey,
                    StringComparison.OrdinalIgnoreCase))
                return list.Where(g => !IsAnnotationGroup(g)).ToList();

            if (filterKey.StartsWith(AnnotationCategoryPrefix, StringComparison.OrdinalIgnoreCase))
            {
                var annCategory = filterKey.Substring(AnnotationCategoryPrefix.Length);
                return list.Where(g =>
                    IsAnnotationGroup(g) &&
                    string.Equals(GetHostCategoryKey(g), annCategory, StringComparison.OrdinalIgnoreCase)).ToList();
            }

            ParseFilterKey(filterKey, out var category, out var manufacturer);

            return list.Where(g =>
            {
                if (IsAnnotationGroup(g))
                    return false;

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
