using System.Collections.Generic;

namespace FamilyMang
{
    public class FamilyDisplayItem
    {
        public int ElementIdValue { get; set; }
        public string Name { get; set; }
        public string CategoryName { get; set; }
    }

    public class InitUploadResponseDto
    {
        public string family_id { get; set; }
        public string bucket { get; set; }
        public string object_key { get; set; }
        public string presigned_put_url { get; set; }
        public int expires_in_seconds { get; set; }
    }

    public class ExtractedFamilyData
    {
        public string FilePath { get; set; }
        public string FamilyName { get; set; }
        public string OriginalFilename { get; set; }
        public string Category { get; set; }
        public long SizeBytes { get; set; }
        public string Sha256 { get; set; }
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

        public string FamilyName
        {
            get
            {
                var name = GetMetadataValue("family_name");
                return !string.IsNullOrEmpty(name) ? name : original_filename;
            }
        }

        public string Category => GetMetadataValue("category") ?? "\u2014";

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
