using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Autodesk.Revit.DB;

namespace FamilyMang
{
    public static class FamilyExtractor
    {
        private static readonly string TempDir =
            Path.Combine(Path.GetTempPath(), "FamilyMang", "upload");

        public static List<FamilyDisplayItem> CollectFamilies(Document doc)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(Family))
                .Cast<Family>()
                .Where(f => !f.IsInPlace)
                .Select(f => new FamilyDisplayItem
                {
                    ElementIdValue = f.Id.IntegerValue,
                    Name = f.Name,
                    CategoryName = f.FamilyCategory?.Name ?? "\u2014"
                })
                .OrderBy(f => f.CategoryName)
                .ThenBy(f => f.Name)
                .ToList();
        }

        public static ExtractedFamilyData ExtractAndSave(Document doc, int familyElementId)
        {
            var family = doc.GetElement(new ElementId(familyElementId)) as Family;
            if (family == null)
                throw new InvalidOperationException("Семейство не найдено в документе.");

            Directory.CreateDirectory(TempDir);
            string fileName = SanitizeName(family.Name) + ".rfa";
            string filePath = Path.Combine(TempDir, fileName);

            Document famDoc = doc.EditFamily(family);
            if (famDoc == null)
                throw new InvalidOperationException(
                    $"Не удалось открыть семейство «{family.Name}» для чтения.");

            try
            {
                famDoc.SaveAs(filePath, new SaveAsOptions { OverwriteExistingFile = true });

                FamilyManager fm = famDoc.FamilyManager;

                var parameters = ExtractParameters(fm);
                var types = ExtractTypes(fm);

                var fi = new FileInfo(filePath);
                string sha256 = ComputeSha256(filePath);

                return new ExtractedFamilyData
                {
                    FilePath = filePath,
                    FamilyName = family.Name,
                    OriginalFilename = fileName,
                    Category = family.FamilyCategory?.Name,
                    SizeBytes = fi.Length,
                    Sha256 = sha256,
                    Parameters = parameters,
                    Types = types
                };
            }
            finally
            {
                famDoc.Close(false);
            }
        }

        private static List<Dictionary<string, object>> ExtractParameters(FamilyManager fm)
        {
            var list = new List<Dictionary<string, object>>();

            foreach (FamilyParameter fp in fm.Parameters)
            {
                var p = new Dictionary<string, object>
                {
                    { "name", fp.Definition.Name },
                    { "is_instance", fp.IsInstance },
                    { "is_shared", fp.IsShared },
                    { "storage_type", fp.StorageType.ToString().ToLowerInvariant() }
                };

                if (fp.IsShared)
                    p["shared_guid"] = fp.GUID.ToString();

                try
                {
                    p["spec"] = fp.Definition.GetDataType().TypeId ?? "";
                }
                catch
                {
                    p["spec"] = "";
                }

                list.Add(p);
            }
            return list;
        }

        private static List<Dictionary<string, object>> ExtractTypes(FamilyManager fm)
        {
            var list = new List<Dictionary<string, object>>();

            foreach (FamilyType ft in fm.Types)
            {
                if (string.IsNullOrEmpty(ft.Name)) continue;

                var values = new Dictionary<string, object>();

                foreach (FamilyParameter fp in fm.Parameters)
                {
                    if (!ft.HasValue(fp)) continue;

                    string val = ReadParamValue(ft, fp);
                    if (val != null)
                        values[fp.Definition.Name] = val;
                }

                list.Add(new Dictionary<string, object>
                {
                    { "type_name", ft.Name },
                    { "values", values }
                });
            }
            return list;
        }

        private static string ReadParamValue(FamilyType ft, FamilyParameter fp)
        {
            switch (fp.StorageType)
            {
                case StorageType.Double:
                    return ft.AsDouble(fp)?.ToString("G") ?? "";
                case StorageType.Integer:
                    return ft.AsInteger(fp)?.ToString() ?? "";
                case StorageType.String:
                    return ft.AsString(fp) ?? "";
                case StorageType.ElementId:
                    var eid = ft.AsElementId(fp);
                    return eid != null && eid != ElementId.InvalidElementId
                        ? eid.IntegerValue.ToString()
                        : "";
                default:
                    return null;
            }
        }

        private static string ComputeSha256(string path)
        {
            using (var sha = SHA256.Create())
            using (var fs = File.OpenRead(path))
            {
                byte[] hash = sha.ComputeHash(fs);
                return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
            }
        }

        private static string SanitizeName(string name)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            return name;
        }
    }
}
