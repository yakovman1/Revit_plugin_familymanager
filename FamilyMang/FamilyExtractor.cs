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

        public static FamilyUploadBundle CollectUploadBundle(Document doc)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));

            if (doc.IsFamilyDocument)
                return CollectFromFamilyEditor(doc);

            return CollectFromProject(doc);
        }

        /// <summary>
        /// Извлечение всех .rfa — только на главном потоке Revit (до HTTP).
        /// </summary>
        public static void SaveBundleToStorageFolder(Document doc, FamilyUploadBundle bundle, string destinationFolder)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            if (bundle?.Primary == null)
                throw new InvalidOperationException("Не найдено основное семейство.");

            if (!FamilyStoragePaths.IsUnderRoot(destinationFolder))
                throw new InvalidOperationException("Папка назначения должна находиться внутри хранилища семейств.");

            Directory.CreateDirectory(destinationFolder);

            if (doc.IsFamilyDocument && bundle.Primary.IsPrimary)
                SaveHostFamilyToFolder(doc, destinationFolder);
            else
                SaveFamilyItemToFolder(doc, bundle.Primary, destinationFolder);

            // На сетевой диск P: — только host; nested выгружаются в БД/S3 через ExtractBundle.
        }

        private static void SaveHostFamilyToFolder(Document doc, string destinationFolder)
        {
            var family = doc.OwnerFamily;
            if (family == null)
                throw new InvalidOperationException("Редактор семейств не содержит OwnerFamily.");

            string destPath = BuildDestinationPath(doc, family, destinationFolder);
            doc.SaveAs(destPath, new SaveAsOptions { OverwriteExistingFile = true });
        }

        private static void SaveFamilyItemToFolder(Document doc, FamilyDisplayItem item, string destinationFolder)
        {
            var family = doc.GetElement(new ElementId(item.ElementIdValue)) as Family;
            if (family == null)
                throw new InvalidOperationException($"Семейство «{item.Name}» не найдено в документе.");

            string destPath = BuildDestinationPath(doc, family, destinationFolder, item.Name);

            Document famDoc = doc.EditFamily(family);
            if (famDoc == null)
                throw new InvalidOperationException($"Не удалось открыть семейство «{item.Name}» для сохранения.");

            try
            {
                famDoc.SaveAs(destPath, new SaveAsOptions { OverwriteExistingFile = true });
            }
            finally
            {
                famDoc.Close(false);
            }
        }

        private static string BuildDestinationPath(Document doc, Family family, string destinationFolder, string nameOverride = null)
        {
            string displayName = nameOverride;
            if (string.IsNullOrWhiteSpace(displayName))
            {
                displayName = IsHostFamily(doc, family)
                    ? ResolveHostFamilyName(doc, family)
                    : family.Name;
            }

            string fileName = SanitizeName(displayName) + ".rfa";
            return Path.Combine(destinationFolder, fileName);
        }

        private static bool IsHostFamily(Document doc, Family family)
        {
            return doc.IsFamilyDocument && doc.OwnerFamily != null && doc.OwnerFamily.Id == family.Id;
        }

        public static ExtractedUploadBundle ExtractBundle(Document doc, FamilyUploadBundle bundle)
        {
            var primaryItem = bundle.Primary;
            if (primaryItem == null)
                throw new InvalidOperationException("Не найдено основное семейство.");

            var result = new ExtractedUploadBundle
            {
                Primary = ExtractAndSave(doc, primaryItem)
            };

            foreach (var nestedItem in bundle.Nested)
            {
                try
                {
                    result.Nested.Add(ExtractAndSave(doc, nestedItem));
                }
                catch (Exception ex)
                {
                    result.NestedErrors.Add($"{nestedItem.Name}: {ex.Message}");
                }
            }

            return result;
        }

        private static FamilyUploadBundle CollectFromFamilyEditor(Document doc)
        {
            var owner = doc.OwnerFamily;
            if (owner == null)
                throw new InvalidOperationException("Не удалось определить основное семейство редактора.");

            string hostName = ResolveHostFamilyName(doc, owner);

            var primaryItem = ToDisplayItem(owner, isPrimary: true);
            primaryItem.Name = hostName;
            primaryItem.CategoryName = owner.FamilyCategory?.Name ?? "\u2014";

            var items = new List<FamilyDisplayItem> { primaryItem };

            var nested = new FilteredElementCollector(doc)
                .OfClass(typeof(Family))
                .Cast<Family>()
                .Where(f => f.Id != owner.Id && IsUploadableFamily(f))
                .OrderBy(f => f.FamilyCategory?.Name ?? "")
                .ThenBy(f => f.Name)
                .Select(f => ToDisplayItem(f, isPrimary: false));

            items.AddRange(nested);

            return new FamilyUploadBundle
            {
                IsFamilyEditor = true,
                HostFamilyName = hostName,
                HostCategoryName = primaryItem.CategoryName,
                Items = items
            };
        }

        public static string ResolveHostFamilyName(Document doc, Family owner)
        {
            if (owner != null && !string.IsNullOrWhiteSpace(owner.Name))
                return owner.Name.Trim();

            if (!string.IsNullOrWhiteSpace(doc.PathName))
            {
                var fromPath = Path.GetFileNameWithoutExtension(doc.PathName)?.Trim();
                if (!string.IsNullOrWhiteSpace(fromPath))
                    return fromPath;
            }

            if (!string.IsNullOrWhiteSpace(doc.Title))
                return doc.Title.Trim();

            return "\u0421\u0435\u043c\u0435\u0439\u0441\u0442\u0432\u043e \u0431\u0435\u0437 \u0438\u043c\u0435\u043d\u0438";
        }

        private static FamilyUploadBundle CollectFromProject(Document doc)
        {
            var items = new FilteredElementCollector(doc)
                .OfClass(typeof(Family))
                .Cast<Family>()
                .Where(IsUploadableFamily)
                .OrderBy(f => f.FamilyCategory?.Name ?? "")
                .ThenBy(f => f.Name)
                .Select(f => ToDisplayItem(f, isPrimary: false))
                .ToList();

            return new FamilyUploadBundle
            {
                IsFamilyEditor = false,
                Items = items
            };
        }

        /// <summary>
        /// Системные аннотации (заголовки уровней/разрезов и т.п.) не редактируются через EditFamily — не показываем и не выгружаем.
        /// </summary>
        private static bool IsUploadableFamily(Family family)
        {
            if (family == null || family.IsInPlace)
                return false;

            try
            {
                return family.IsEditable;
            }
            catch
            {
                return false;
            }
        }

        private static FamilyDisplayItem ToDisplayItem(Family family, bool isPrimary)
        {
            return new FamilyDisplayItem
            {
                ElementIdValue = family.Id.IntegerValue,
                Name = string.IsNullOrWhiteSpace(family.Name) ? "\u2014" : family.Name,
                CategoryName = family.FamilyCategory?.Name ?? "\u2014",
                IsPrimary = isPrimary,
                RoleDisplay = isPrimary ? "\u041e\u0441\u043d\u043e\u0432\u043d\u043e\u0435" : "\u0412\u043b\u043e\u0436\u0435\u043d\u043d\u043e\u0435"
            };
        }

        public static ExtractedFamilyData ExtractAndSave(Document doc, FamilyDisplayItem item)
        {
            if (doc.IsFamilyDocument && item.IsPrimary)
                return ExtractHostFromFamilyEditor(doc);

            if (doc.IsFamilyDocument)
                return ExtractNestedFromFamilyEditor(doc, item.ElementIdValue);

            return ExtractFromProject(doc, item.ElementIdValue);
        }

        private static ExtractedFamilyData ExtractHostFromFamilyEditor(Document doc)
        {
            var family = doc.OwnerFamily;
            if (family == null)
                throw new InvalidOperationException("Редактор семейств не содержит OwnerFamily.");

            Directory.CreateDirectory(TempDir);
            string familyName = ResolveHostFamilyName(doc, family);
            string fileName = SanitizeName(familyName) + ".rfa";
            string filePath = Path.Combine(TempDir, fileName);

            // Копия с диска безопаснее, чем SaveAs открытого документа
            if (TryCopyExistingFamilyFile(doc, filePath))
            {
                // ok
            }
            else
            {
                doc.SaveAs(filePath, new SaveAsOptions { OverwriteExistingFile = true });
            }

            var data = BuildExtractedData(family, doc.FamilyManager, filePath, fileName, isPrimary: true);
            data.FamilyName = familyName;
            return data;
        }

        private static bool TryCopyExistingFamilyFile(Document doc, string destPath)
        {
            try
            {
                var sourcePath = doc.PathName;
                if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
                    return false;

                File.Copy(sourcePath, destPath, overwrite: true);
                return new FileInfo(destPath).Length > 0;
            }
            catch
            {
                return false;
            }
        }

        private static ExtractedFamilyData ExtractNestedFromFamilyEditor(Document doc, int familyElementId)
        {
            var family = doc.GetElement(new ElementId(familyElementId)) as Family;
            if (family == null)
                throw new InvalidOperationException("Вложенное семейство не найдено в редакторе.");

            return ExtractViaEditFamily(doc, family, isPrimary: false);
        }

        private static ExtractedFamilyData ExtractFromProject(Document doc, int familyElementId)
        {
            var family = doc.GetElement(new ElementId(familyElementId)) as Family;
            if (family == null)
                throw new InvalidOperationException("Семейство не найдено в документе.");

            return ExtractViaEditFamily(doc, family, isPrimary: false);
        }

        private static ExtractedFamilyData ExtractViaEditFamily(Document doc, Family family, bool isPrimary)
        {
            Directory.CreateDirectory(TempDir);
            string displayName = string.IsNullOrWhiteSpace(family.Name) ? "nested" : family.Name;
            string fileName = SanitizeName(displayName) + ".rfa";
            string filePath = Path.Combine(TempDir, fileName);

            Document famDoc = doc.EditFamily(family);
            if (famDoc == null)
                throw new InvalidOperationException(
                    $"Не удалось открыть семейство «{displayName}» для чтения.");

            try
            {
                famDoc.SaveAs(filePath, new SaveAsOptions { OverwriteExistingFile = true });
                var data = BuildExtractedData(family, famDoc.FamilyManager, filePath, fileName, isPrimary);
                if (string.IsNullOrWhiteSpace(data.FamilyName))
                    data.FamilyName = displayName;
                return data;
            }
            finally
            {
                famDoc.Close(false);
            }
        }

        private static ExtractedFamilyData BuildExtractedData(
            Family family, FamilyManager fm, string filePath, string fileName, bool isPrimary)
        {
            var fi = new FileInfo(filePath);
            var category = family.FamilyCategory;
            var isAnnotation = category != null &&
                               category.CategoryType == CategoryType.Annotation;

            return new ExtractedFamilyData
            {
                FilePath = filePath,
                FamilyName = family.Name,
                OriginalFilename = fileName,
                Category = category?.Name,
                SizeBytes = fi.Length,
                Sha256 = ComputeSha256(filePath),
                Parameters = ExtractParameters(fm),
                Types = ExtractTypes(fm),
                IsPrimary = isPrimary,
                IsAnnotation = isAnnotation
            };
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
            if (string.IsNullOrWhiteSpace(name))
                name = "family";
            foreach (char c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            return name;
        }
    }
}
