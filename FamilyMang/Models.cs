using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;

namespace FamilyMang
{
    public class FamilyDisplayItem
    {
        public int ElementIdValue { get; set; }
        public string Name { get; set; }
        public string CategoryName { get; set; }
        public bool IsPrimary { get; set; }
        public string RoleDisplay { get; set; }
    }

    public class FamilyUploadBundle
    {
        public bool IsFamilyEditor { get; set; }
        public string HostFamilyName { get; set; }
        public string HostCategoryName { get; set; }
        public List<FamilyDisplayItem> Items { get; set; } = new List<FamilyDisplayItem>();

        public FamilyDisplayItem Primary =>
            Items.FirstOrDefault(i => i.IsPrimary);

        public IEnumerable<FamilyDisplayItem> Nested =>
            Items.Where(i => !i.IsPrimary);
    }

    public class ExtractedUploadBundle
    {
        public ExtractedFamilyData Primary { get; set; }
        public List<ExtractedFamilyData> Nested { get; set; } = new List<ExtractedFamilyData>();
        public List<string> NestedErrors { get; set; } = new List<string>();
    }

    public class InitUploadResponseDto
    {
        public string family_id { get; set; }
        public string bucket { get; set; }
        public string object_key { get; set; }
        public string presigned_put_url { get; set; }
        public int expires_in_seconds { get; set; }
        public int version { get; set; } = 1;
        public bool is_new { get; set; } = true;
        public bool unchanged { get; set; }
        public string thumbnail_object_key { get; set; }
        public string presigned_thumbnail_put_url { get; set; }
    }

    public class FamilyUploadResult
    {
        public string FamilyId { get; set; }
        public int Version { get; set; }
        public bool IsNew { get; set; }
        public bool Unchanged { get; set; }
        public string ThumbnailNote { get; set; }
    }

    public class ExtractedFamilyData
    {
        public string FilePath { get; set; }
        public string FamilyName { get; set; }
        public string OriginalFilename { get; set; }
        public string Category { get; set; }
        public long SizeBytes { get; set; }
        public string Sha256 { get; set; }
        public bool IsPrimary { get; set; }
        public bool IsAnnotation { get; set; }
        public List<Dictionary<string, object>> Parameters { get; set; }
        public List<Dictionary<string, object>> Types { get; set; }
    }

    public class FamilySummaryDto
    {
        public string id { get; set; }
        public string project_id { get; set; }
        public string status { get; set; }
        public string bucket { get; set; }
        public string object_key { get; set; }
        public string original_filename { get; set; }
        public string sha256 { get; set; }
        public object size_bytes { get; set; }
        public Dictionary<string, object> metadata_json { get; set; }
        public string created_at { get; set; }
        public string updated_at { get; set; }
        public string uploaded_at { get; set; }
        public string etag { get; set; }
        public object version { get; set; }
        public bool has_thumbnail { get; set; }

        public bool HasThumbnail => has_thumbnail;

        public int VersionNumber
        {
            get
            {
                if (version != null && int.TryParse(version.ToString(), out var v) && v > 0)
                    return v;
                return 1;
            }
        }

        public string VersionDisplay => "v" + VersionNumber;

        public string FamilyName
        {
            get
            {
                var name = GetMetadataValue("family_name");
                return !string.IsNullOrEmpty(name) ? name : original_filename;
            }
        }

        public string Category => GetMetadataValue("category") ?? "\u2014";

        /// <summary>ADSK_Завод-изготовитель из metadata.types[].values (первый непустой тип).</summary>
        public string Manufacturer => GetManufacturerParameterValue();

        private string GetManufacturerParameterValue()
        {
            if (metadata_json == null || !metadata_json.ContainsKey("types"))
                return null;

            foreach (var typeEntry in EnumerateMetadataObjects(metadata_json["types"]))
            {
                if (!(typeEntry is Dictionary<string, object> typeDict))
                    continue;
                if (!typeDict.TryGetValue("values", out var valuesObj))
                    continue;

                var value = ReadParameterFromValues(valuesObj, CatalogCategories.ManufacturerParameterName);
                if (!string.IsNullOrWhiteSpace(value))
                    return value.Trim();
            }

            return null;
        }

        private static string ReadParameterFromValues(object valuesObj, string parameterName)
        {
            if (!(valuesObj is Dictionary<string, object> values))
                return null;

            if (values.TryGetValue(parameterName, out var direct))
                return direct?.ToString();

            foreach (var pair in values)
            {
                if (string.Equals(pair.Key, parameterName, StringComparison.OrdinalIgnoreCase))
                    return pair.Value?.ToString();
            }

            return null;
        }

        private static IEnumerable<object> EnumerateMetadataObjects(object listObj)
        {
            if (listObj == null)
                yield break;

            if (listObj is object[] array)
            {
                foreach (var item in array)
                    yield return item;
                yield break;
            }

            if (listObj is ArrayList list)
            {
                foreach (var item in list)
                    yield return item;
                yield break;
            }

            if (listObj is IEnumerable enumerable && !(listObj is string))
            {
                foreach (var item in enumerable)
                    yield return item;
            }
        }

        public string SizeDisplay
        {
            get
            {
                if (size_bytes == null) return "\u2014";
                if (!long.TryParse(size_bytes.ToString(), out var bytes)) return "\u2014";
                if (bytes < 1024) return $"{bytes} B";
                if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
                return $"{bytes / (1024.0 * 1024.0):F1} MB";
            }
        }

        public string StatusDisplay
        {
            get
            {
                switch (status)
                {
                    case "initiated": return "\u0418\u043d\u0438\u0446\u0438\u0430\u043b\u0438\u0437\u0438\u0440\u043e\u0432\u0430\u043d";
                    case "uploaded":  return "\u0417\u0430\u0433\u0440\u0443\u0436\u0435\u043d";
                    case "parsed":    return "\u041e\u0431\u0440\u0430\u0431\u043e\u0442\u0430\u043d";
                    case "ready":     return "\u0413\u043e\u0442\u043e\u0432";
                    case "failed":    return "\u041e\u0448\u0438\u0431\u043a\u0430";
                    default:          return status ?? "\u2014";
                }
            }
        }

        private string GetMetadataValue(string key)
        {
            if (metadata_json != null && metadata_json.ContainsKey(key))
                return metadata_json[key]?.ToString();
            return null;
        }

        public Dictionary<string, object> ExtraMetadata
        {
            get
            {
                if (metadata_json == null || !metadata_json.ContainsKey("extra"))
                    return null;
                return metadata_json["extra"] as Dictionary<string, object>;
            }
        }

        public bool IsPrimaryFamily
        {
            get
            {
                if (!string.IsNullOrEmpty(ParentFamilyId))
                    return false;

                var extra = ExtraMetadata;
                if (extra == null)
                    return true;

                if (extra.TryGetValue("is_primary", out var flag))
                {
                    if (flag is bool b)
                        return b;
                    var text = flag?.ToString()?.Trim().ToLowerInvariant();
                    if (text == "true") return true;
                    if (text == "false") return false;
                }

                if (extra.TryGetValue("role", out var role))
                {
                    var roleText = role?.ToString()?.Trim().ToLowerInvariant();
                    if (roleText == "nested")
                        return false;
                    if (roleText == "host")
                        return true;
                }

                return true;
            }
        }

        public string ParentFamilyId
        {
            get
            {
                var extra = ExtraMetadata;
                if (extra == null || !extra.ContainsKey("parent_family_id"))
                    return null;

                var value = extra["parent_family_id"]?.ToString()?.Trim();
                return string.IsNullOrEmpty(value) ? null : value;
            }
        }
    }

    public class FavoriteItemDto
    {
        public string family_id { get; set; }
        public string created_at { get; set; }
    }

    public class ListFavoritesResponseDto
    {
        public List<FavoriteItemDto> items { get; set; } = new List<FavoriteItemDto>();
    }

    public class AddFavoriteResponseDto
    {
        public string family_id { get; set; }
        public string created_at { get; set; }
    }

    public class ThumbnailUrlResponseDto
    {
        public string presigned_get_url { get; set; }
        public int expires_in_seconds { get; set; }
    }

    public class ThumbnailInitResponseDto
    {
        public string presigned_put_url { get; set; }
        public string thumbnail_object_key { get; set; }
        public int expires_in_seconds { get; set; }
    }

    /// <summary>Строка каталога: основное семейство или вложенное под ним.</summary>
    public class CatalogFamilyRow : INotifyPropertyChanged
    {
        private bool _isFavorite;
        private bool _isExpanded;
        private int _nestedCount;

        public FamilySummaryDto Family { get; set; }
        public bool IsNested { get; set; }
        public bool IsHostRow => !IsNested;
        public string HostFamilyId { get; set; }
        public bool IsSelectable => IsHostRow;

        public int NestedCount
        {
            get => _nestedCount;
            set
            {
                if (_nestedCount == value)
                    return;
                _nestedCount = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ExpandGlyph));
                OnPropertyChanged(nameof(IndentedFamilyName));
                OnPropertyChanged(nameof(RoleDisplay));
            }
        }

        public bool IsExpanded
        {
            get => _isExpanded;
            set
            {
                if (_isExpanded == value)
                    return;
                _isExpanded = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ExpandGlyph));
                OnPropertyChanged(nameof(IndentedFamilyName));
            }
        }

        public string ExpandGlyph =>
            !IsHostRow || NestedCount <= 0
                ? ""
                : IsExpanded ? "\u25BC " : "\u25B6 ";

        public bool IsFavorite
        {
            get => _isFavorite;
            set
            {
                if (_isFavorite == value)
                    return;
                _isFavorite = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(FavoriteDisplay));
            }
        }

        public string FavoriteDisplay => IsFavorite ? "\u2605" : "\u2606";

        public event PropertyChangedEventHandler PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        public string RoleDisplay
        {
            get
            {
                if (IsNested)
                    return "\u0412\u043b\u043e\u0436\u0435\u043d\u043d\u043e\u0435";
                if (NestedCount > 0)
                    return "\u041e\u0441\u043d\u043e\u0432\u043d\u043e\u0435 (" + NestedCount + ")";
                return "\u041e\u0441\u043d\u043e\u0432\u043d\u043e\u0435";
            }
        }

        public string FamilyName => Family?.FamilyName ?? "\u2014";
        public string IndentedFamilyName =>
            IsNested ? "        " + FamilyName : ExpandGlyph + FamilyName;

        public string Category => Family?.Category ?? "\u2014";
        public string original_filename => Family?.original_filename ?? "\u2014";
        public string StatusDisplay => Family?.StatusDisplay ?? "\u2014";
        public string SizeDisplay => Family?.SizeDisplay ?? "\u2014";

        /// <summary>Для host: максимальная версия в группе (host + nested).</summary>
        public int? BundleVersion { get; set; }

        public string VersionDisplay
        {
            get
            {
                if (BundleVersion.HasValue && !IsNested)
                    return "v" + BundleVersion.Value;
                return Family?.VersionDisplay ?? "v1";
            }
        }
    }

    public static class CatalogHierarchy
    {
        /// <summary>Группы для пагинации: каждая группа = основное + его вложенные.</summary>
        public static List<List<CatalogFamilyRow>> BuildPrimaryGroups(IEnumerable<FamilySummaryDto> items)
        {
            var idToCanonical = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var all = DeduplicateLatest(items, idToCanonical);
            if (all.Count == 0)
                return new List<List<CatalogFamilyRow>>();

            var rootsByName = all
                .Where(f => f.IsPrimaryFamily && string.IsNullOrEmpty(f.ParentFamilyId))
                .ToDictionary(f => NormalizeKey(f.FamilyName), f => f, StringComparer.OrdinalIgnoreCase);

            var nestedByParent = new Dictionary<string, List<FamilySummaryDto>>(StringComparer.OrdinalIgnoreCase);
            var roots = new List<FamilySummaryDto>();

            foreach (var family in all)
            {
                var parentId = ResolveCanonicalParentId(family, rootsByName, idToCanonical);
                if (!string.IsNullOrEmpty(parentId) && !string.Equals(parentId, family.id, StringComparison.OrdinalIgnoreCase))
                {
                    if (!nestedByParent.ContainsKey(parentId))
                        nestedByParent[parentId] = new List<FamilySummaryDto>();
                    nestedByParent[parentId].Add(family);
                }
                else if (family.IsPrimaryFamily)
                {
                    roots.Add(family);
                }
            }

            var rootIds = new HashSet<string>(roots.Select(r => r.id), StringComparer.OrdinalIgnoreCase);
            foreach (var family in all)
            {
                if (family.IsPrimaryFamily || !string.IsNullOrEmpty(ResolveCanonicalParentId(family, rootsByName, idToCanonical)))
                    continue;
                if (!rootIds.Contains(family.id))
                    roots.Add(family);
            }

            roots = roots
                .GroupBy(r => HostKey(r), StringComparer.OrdinalIgnoreCase)
                .Select(g => PickNewest(g))
                .OrderByDescending(f => f.VersionNumber)
                .ThenByDescending(f => ParseDateOrMin(f.updated_at))
                .ThenByDescending(f => ParseDateOrMin(f.created_at))
                .ToList();

            var groups = new List<List<CatalogFamilyRow>>();
            foreach (var root in roots)
            {
                var group = new List<CatalogFamilyRow> { ToRow(root, false) };
                if (nestedByParent.TryGetValue(root.id, out var nested))
                {
                    foreach (var child in PickUniqueNewestNested(nested))
                        group.Add(ToRow(child, true));
                }
                groups.Add(group);
            }

            var assignedNested = new HashSet<string>(
                groups.SelectMany(g => g.Where(r => r.IsNested).Select(r => r.Family.id)),
                StringComparer.OrdinalIgnoreCase);

            foreach (var pair in nestedByParent)
            {
                var unassigned = PickUniqueNewestNested(
                    pair.Value.Where(n => !assignedNested.Contains(n.id))).ToList();
                if (unassigned.Count == 0)
                    continue;

                var syntheticRoot = all.FirstOrDefault(f =>
                    string.Equals(f.id, pair.Key, StringComparison.OrdinalIgnoreCase));
                var group = syntheticRoot != null
                    ? new List<CatalogFamilyRow> { ToRow(syntheticRoot, false) }
                    : new List<CatalogFamilyRow>();

                foreach (var n in unassigned)
                    group.Add(ToRow(n, true));

                if (group.Count > 0)
                    groups.Add(group);
            }

            return groups
                .GroupBy(g => HostKey(g[0].Family), StringComparer.OrdinalIgnoreCase)
                .Select(duplicateGroups =>
                {
                    var bestRoot = PickNewest(duplicateGroups.Select(gr => gr[0].Family));
                    return duplicateGroups.First(gr => string.Equals(
                        gr[0].Family.id, bestRoot.id, StringComparison.OrdinalIgnoreCase));
                })
                .OrderByDescending(g => g[0].Family.VersionNumber)
                .ThenByDescending(g => ParseDateOrMin(g[0].Family.updated_at))
                .ThenByDescending(g => ParseDateOrMin(g[0].Family.created_at))
                .ToList();
        }

        private static string ResolveCanonicalParentId(
            FamilySummaryDto family,
            Dictionary<string, FamilySummaryDto> rootsByName,
            Dictionary<string, string> idToCanonical)
        {
            if (!string.IsNullOrEmpty(family.ParentFamilyId) &&
                idToCanonical.TryGetValue(family.ParentFamilyId, out var canonical))
                return canonical;

            var parentName = GetParentFamilyName(family);
            if (!string.IsNullOrEmpty(parentName) &&
                rootsByName.TryGetValue(NormalizeKey(parentName), out var root))
                return root.id;

            return family.ParentFamilyId;
        }

        private static List<FamilySummaryDto> DeduplicateLatest(
            IEnumerable<FamilySummaryDto> items,
            Dictionary<string, string> idToCanonical)
        {
            var all = items?.Where(f => f != null).ToList() ?? new List<FamilySummaryDto>();
            if (all.Count <= 1)
            {
                foreach (var f in all)
                    idToCanonical[f.id] = f.id;
                return all;
            }

            var winnersByKey = new Dictionary<string, FamilySummaryDto>(StringComparer.OrdinalIgnoreCase);

            foreach (var family in all)
            {
                var key = GetLogicalKey(family);

                if (!winnersByKey.TryGetValue(key, out var current))
                {
                    winnersByKey[key] = family;
                    idToCanonical[family.id] = family.id;
                    continue;
                }

                if (IsNewerThan(family, current))
                {
                    idToCanonical[current.id] = family.id;
                    idToCanonical[family.id] = family.id;
                    winnersByKey[key] = family;
                }
                else
                {
                    idToCanonical[family.id] = current.id;
                }
            }

            return winnersByKey.Values.ToList();
        }

        private static FamilySummaryDto PickNewest(IEnumerable<FamilySummaryDto> items) =>
            items.OrderByDescending(f => f.VersionNumber)
                .ThenByDescending(f => ParseDateOrMin(f.updated_at))
                .ThenByDescending(f => ParseDateOrMin(f.created_at))
                .ThenByDescending(f => f.id, StringComparer.OrdinalIgnoreCase)
                .First();

        private static IEnumerable<FamilySummaryDto> PickUniqueNewestNested(IEnumerable<FamilySummaryDto> nested) =>
            nested.GroupBy(n => NestedKey(n), StringComparer.OrdinalIgnoreCase)
                .Select(g => PickNewest(g))
                .OrderBy(f => f.FamilyName, StringComparer.CurrentCultureIgnoreCase);

        private static DateTime ParseDateOrMin(string value) =>
            DateTime.TryParse(value, out var dt) ? dt : DateTime.MinValue;

        private static string GetLogicalKey(FamilySummaryDto family)
        {
            if (!string.IsNullOrEmpty(family.ParentFamilyId) || !family.IsPrimaryFamily)
                return NestedKey(family);
            return HostKey(family);
        }

        private static string HostKey(FamilySummaryDto f) =>
            "h:" + NormalizeKey(f.FamilyName) + "|" + NormalizeKey(f.Category);

        private static string NestedKey(FamilySummaryDto f) =>
            "n:" + NormalizeKey(f.FamilyName) + "|" + NormalizeKey(f.Category) + "|" +
            NormalizeKey(GetParentFamilyName(f));

        private static string GetParentFamilyName(FamilySummaryDto f)
        {
            var extra = f.ExtraMetadata;
            if (extra != null && extra.TryGetValue("parent_family_name", out var name))
            {
                var text = name?.ToString()?.Trim();
                if (!string.IsNullOrEmpty(text))
                    return text;
            }
            return f.ParentFamilyId ?? "";
        }

        private static string NormalizeKey(string value) =>
            string.IsNullOrWhiteSpace(value) ? "" : value.Trim();

        private static bool IsNewerThan(FamilySummaryDto candidate, FamilySummaryDto current)
        {
            if (candidate.VersionNumber != current.VersionNumber)
                return candidate.VersionNumber > current.VersionNumber;

            if (DateTime.TryParse(candidate.updated_at, out var u1) &&
                DateTime.TryParse(current.updated_at, out var u2) &&
                u1 != u2)
                return u1 > u2;

            if (DateTime.TryParse(candidate.created_at, out var c1) &&
                DateTime.TryParse(current.created_at, out var c2) &&
                c1 != c2)
                return c1 > c2;

            return string.Compare(candidate.id, current.id, StringComparison.OrdinalIgnoreCase) > 0;
        }

        private static CatalogFamilyRow ToRow(FamilySummaryDto family, bool isNested) =>
            new CatalogFamilyRow { Family = family, IsNested = isNested };
    }

    public class DeleteFamilyResponseDto
    {
        public bool ok { get; set; }
        public List<string> deleted_family_ids { get; set; } = new List<string>();
        public List<string> deleted_s3_objects { get; set; } = new List<string>();
    }

    public class ListFamiliesResponseDto
    {
        public List<FamilySummaryDto> items { get; set; } = new List<FamilySummaryDto>();
        public int total { get; set; }
    }

    public class DownloadUrlResponseDto
    {
        public string presigned_get_url { get; set; }
        public int expires_in_seconds { get; set; }
    }
}
