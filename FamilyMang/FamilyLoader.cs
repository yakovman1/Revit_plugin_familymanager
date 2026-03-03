using System.IO;
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

        public static bool LoadIntoDocument(Document doc, string rfaPath)
        {
            if (doc == null || doc.IsReadOnly || !File.Exists(rfaPath))
                return false;

            using (var tx = new Transaction(doc, "Загрузка семейства из каталога"))
            {
                tx.Start();
                bool loaded = doc.LoadFamily(rfaPath, new FamilyOverwriteOptions(), out _);
                if (loaded)
                    tx.Commit();
                else
                    tx.RollBack();
                return loaded;
            }
        }
    }
}
