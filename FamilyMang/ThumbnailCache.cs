using System;
using System.IO;

namespace FamilyMang
{
    public static class ThumbnailCache
    {
        private static readonly string CacheDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "FamilyMang", "cache", "thumbnails");

        public static string GetFilePath(string familyId) =>
            Path.Combine(CacheDir, SanitizeId(familyId) + ".png");

        public static bool TryGetExisting(string familyId, out string path)
        {
            path = GetFilePath(familyId);
            if (File.Exists(path) && new FileInfo(path).Length > 0)
                return true;
            path = null;
            return false;
        }

        public static void Write(string familyId, byte[] bytes)
        {
            Directory.CreateDirectory(CacheDir);
            File.WriteAllBytes(GetFilePath(familyId), bytes);
        }

        private static string SanitizeId(string familyId)
        {
            if (string.IsNullOrWhiteSpace(familyId))
                return "unknown";
            foreach (var c in Path.GetInvalidFileNameChars())
                familyId = familyId.Replace(c, '_');
            return familyId;
        }
    }
}
