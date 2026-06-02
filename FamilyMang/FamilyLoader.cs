using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Autodesk.Revit.DB;

namespace FamilyMang
{
    public sealed class FamilyOverwriteOptions : IFamilyLoadOptions
    {
        public bool OnFamilyFound(bool familyInUse, out bool overwriteParameterValues)
        {
            overwriteParameterValues = true;
            return true;
        }

        public bool OnSharedFamilyFound(
            Family sharedFamily, bool familyInUse,
            out FamilySource source, out bool overwriteParameterValues)
        {
            source = FamilySource.Family;
            overwriteParameterValues = true;
            return true;
        }
    }

    public static class FamilyLoader
    {
        public static async Task<string> DownloadAsync(
            ApiClient client, string familyId, string fileName)
        {
            var presignedUrl = await client.GetDownloadUrlAsync(familyId).ConfigureAwait(false);
            return await client.DownloadFileAsync(presignedUrl, fileName).ConfigureAwait(false);
        }

        public static bool LoadIntoDocument(Document doc, string rfaPath, out Family family)
        {
            family = null;
            if (doc == null || doc.IsReadOnly || !File.Exists(rfaPath))
                return false;

            using (var tx = new Transaction(doc, "Загрузка семейства из каталога"))
            {
                tx.Start();
                bool loaded = doc.LoadFamily(rfaPath, new FamilyOverwriteOptions(), out family);
                if (loaded)
                {
                    tx.Commit();
                    return true;
                }

                tx.RollBack();
            }

            family = FindExistingFamily(doc, Path.GetFileNameWithoutExtension(rfaPath));
            return family != null;
        }

        private static Family FindExistingFamily(Document doc, string familyName)
        {
            if (doc == null || string.IsNullOrWhiteSpace(familyName))
                return null;

            return new FilteredElementCollector(doc)
                .OfClass(typeof(Family))
                .Cast<Family>()
                .FirstOrDefault(f =>
                    f.IsValidObject &&
                    string.Equals(f.Name, familyName, StringComparison.OrdinalIgnoreCase));
        }

        public static FamilySymbol GetDefaultSymbol(Document doc, Family family)
        {
            if (doc == null || family == null)
                return null;

            return family.GetFamilySymbolIds()
                .Select(id => doc.GetElement(id) as FamilySymbol)
                .FirstOrDefault(s => s != null && s.IsValidObject);
        }
    }
}
