using System;
using System.Collections.Generic;

namespace FamilyMang
{
    /// <summary>Марки / аннотации (Tags, Annotation) vs модельные семейства.</summary>
    public static class CatalogFamilyClassification
    {
        public const string AnnotationSectionKey = "section:annotation";
        public const string AnnotationSectionLabel = "Annotation";

        public const string FamilySectionKey = "section:family";
        public const string FamilySectionLabel = "Family";

        public static bool IsAnnotationFamily(FamilySummaryDto family)
        {
            if (family == null)
                return false;

            if (family.metadata_json != null &&
                family.metadata_json.TryGetValue("extra", out var extraObj) &&
                extraObj is Dictionary<string, object> extra &&
                extra.TryGetValue("is_annotation", out var flag))
            {
                if (flag is bool b)
                    return b;
                if (bool.TryParse(flag?.ToString(), out var parsed))
                    return parsed;
            }

            return IsAnnotationCategory(family.Category);
        }

        public static bool IsAnnotationCategory(string category) =>
            IsAnnotationCategoryName(category);

        public static bool IsAnnotationCategoryName(string category)
        {
            if (string.IsNullOrWhiteSpace(category) || category == "\u2014")
                return false;

            var name = category.Trim();

            if (name.EndsWith(" Tags", StringComparison.OrdinalIgnoreCase))
                return true;

            if (name.IndexOf("Annotation", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;

            if (name.EndsWith(" Tag", StringComparison.OrdinalIgnoreCase))
                return true;

            return false;
        }
    }
}
