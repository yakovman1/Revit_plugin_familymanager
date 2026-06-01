using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace FamilyMang
{
    /// <summary>Клиентский поиск по загруженному каталогу (без запросов к API).</summary>
    public static class CatalogSearch
    {
        public static string NormalizeQuery(string query)
        {
            if (string.IsNullOrWhiteSpace(query))
                return null;
            var text = query.Trim();
            return text.Length == 0 ? null : text;
        }

        public static List<List<CatalogFamilyRow>> FilterGroups(
            IEnumerable<List<CatalogFamilyRow>> groups,
            string query)
        {
            var normalized = NormalizeQuery(query);
            var list = groups?.ToList() ?? new List<List<CatalogFamilyRow>>();
            if (normalized == null)
                return list;

            var tokens = SplitTokens(normalized);
            if (tokens.Count == 0)
                return list;

            return list
                .Where(g => g != null && g.Count > 0 && GroupMatchesAllTokens(g, tokens))
                .ToList();
        }

        /// <summary>Раскрыть группу в гриде, если совпадение только у вложенного семейства.</summary>
        public static bool ShouldAutoExpandGroup(IReadOnlyList<CatalogFamilyRow> group, string query)
        {
            var normalized = NormalizeQuery(query);
            if (normalized == null || group == null || group.Count == 0)
                return false;

            var tokens = SplitTokens(normalized);
            if (tokens.Count == 0)
                return false;

            if (GroupMatchesAllTokens(group, tokens) &&
                group.Any(r => !r.IsNested && r.Family != null && FamilyMatchesAllTokens(r.Family, tokens)))
                return false;

            return group.Any(r =>
                r.IsNested && r.Family != null && FamilyMatchesAllTokens(r.Family, tokens));
        }

        private static bool FamilyMatchesAllTokens(FamilySummaryDto family, List<string> tokens)
        {
            foreach (var token in tokens)
            {
                if (!FamilyMatchesToken(family, token))
                    return false;
            }

            return true;
        }

        private static List<string> SplitTokens(string query)
        {
            return query
                .Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(t => t.Trim())
                .Where(t => t.Length > 0)
                .ToList();
        }

        private static bool GroupMatchesAllTokens(IReadOnlyList<CatalogFamilyRow> group, List<string> tokens)
        {
            foreach (var token in tokens)
            {
                if (!GroupMatchesToken(group, token))
                    return false;
            }

            return true;
        }

        private static bool GroupMatchesToken(IReadOnlyList<CatalogFamilyRow> group, string token)
        {
            foreach (var row in group)
            {
                if (row?.Family != null && FamilyMatchesToken(row.Family, token))
                    return true;
            }

            return false;
        }

        private static bool FamilyMatchesToken(FamilySummaryDto family, string token)
        {
            if (family == null || string.IsNullOrEmpty(token))
                return false;

            return Contains(family.FamilyName, token)
                   || Contains(family.Category, token)
                   || Contains(family.Manufacturer, token)
                   || Contains(family.original_filename, token)
                   || Contains(Path.GetFileNameWithoutExtension(family.original_filename ?? ""), token)
                   || Contains(family.StatusDisplay, token);
        }

        private static bool Contains(string haystack, string needle)
        {
            if (string.IsNullOrEmpty(haystack) || string.IsNullOrEmpty(needle))
                return false;
            return haystack.IndexOf(needle, StringComparison.CurrentCultureIgnoreCase) >= 0;
        }
    }
}
