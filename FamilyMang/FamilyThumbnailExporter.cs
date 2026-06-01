using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;

namespace FamilyMang
{
    /// <summary>Экспорт PNG-превью из открытого документа семейства (главный поток Revit).</summary>
    public static class FamilyThumbnailExporter
    {
        private static readonly string PreviewDir =
            Path.Combine(Path.GetTempPath(), "FamilyMang", "preview");

        public static string TryExport(Document doc)
        {
            if (doc == null || !doc.IsFamilyDocument)
                return null;

            try
            {
                var view3d = GetOrCreate3DView(doc);
                if (view3d == null)
                    return null;

                Directory.CreateDirectory(PreviewDir);
                var sessionDir = Path.Combine(PreviewDir, Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(sessionDir);

                var stamp = DateTime.UtcNow;
                var targetPath = Path.Combine(sessionDir, "preview.png");

                var options = new ImageExportOptions
                {
                    FilePath = targetPath,
                    FitDirection = FitDirectionType.Horizontal,
                    HLRandWFViewsFileType = ImageFileType.PNG,
                    ImageResolution = ImageResolution.DPI_150,
                    ZoomType = ZoomFitType.FitToPage,
                    ExportRange = ExportRange.SetOfViews
                };
                options.SetViewsAndSheets(new List<ElementId> { view3d.Id });

                doc.ExportImage(options);

                return FindExportedPng(sessionDir, targetPath, stamp);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>Revit часто пишет PNG не по переданному пути, а как «Имя вида.png» в папке.</summary>
        private static string FindExportedPng(string sessionDir, string expectedPath, DateTime notBeforeUtc)
        {
            var cutoff = notBeforeUtc.AddSeconds(-2);

            if (File.Exists(expectedPath) && new FileInfo(expectedPath).Length > 0)
                return expectedPath;

            var candidates = new List<string>();
            if (Directory.Exists(sessionDir))
                candidates.AddRange(Directory.GetFiles(sessionDir, "*.png", SearchOption.AllDirectories));

            if (Directory.Exists(PreviewDir))
            {
                candidates.AddRange(
                    Directory.GetFiles(PreviewDir, "*.png", SearchOption.TopDirectoryOnly)
                        .Where(p => !p.StartsWith(sessionDir, StringComparison.OrdinalIgnoreCase)));
            }

            return candidates
                .Where(p => File.GetLastWriteTimeUtc(p) >= cutoff && new FileInfo(p).Length > 0)
                .OrderByDescending(p => File.GetLastWriteTimeUtc(p))
                .FirstOrDefault();
        }

        private static View3D GetOrCreate3DView(Document doc)
        {
            var existing = new FilteredElementCollector(doc)
                .OfClass(typeof(View3D))
                .Cast<View3D>()
                .FirstOrDefault(v => !v.IsTemplate && v.CanBePrinted);

            if (existing != null)
                return existing;

            var viewFamilyType = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewFamilyType))
                .Cast<ViewFamilyType>()
                .FirstOrDefault(vft => vft.ViewFamily == ViewFamily.ThreeDimensional);

            if (viewFamilyType == null)
                return null;

            using (var tx = new Transaction(doc, "FamilyMang thumbnail view"))
            {
                tx.Start();
                var created = View3D.CreateIsometric(doc, viewFamilyType.Id);
                tx.Commit();
                return created;
            }
        }
    }
}
