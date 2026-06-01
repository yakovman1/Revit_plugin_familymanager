using System;

using System.Collections.Generic;

using System.IO;

using System.Linq;

using Autodesk.Revit.DB;

using Autodesk.Revit.UI;



namespace FamilyMang

{

    public sealed class ThumbnailExportResult

    {

        public string FilePath { get; set; }

        public string ErrorDetail { get; set; }

        public bool Success => !string.IsNullOrWhiteSpace(FilePath) && File.Exists(FilePath);

    }



    /// <summary>Экспорт PNG-превью: Fine + Shaded + Thin Lines (как в UI Revit).</summary>

    public static class FamilyThumbnailExporter

    {

        private static readonly string PreviewDir =

            Path.Combine(Path.GetTempPath(), "FamilyMang", "preview");



        public static string TryExport(Document doc, UIDocument uiDoc = null) =>

            TryExportDetailed(doc, uiDoc).FilePath;



        public static ThumbnailExportResult TryExportDetailed(Document doc, UIDocument uiDoc = null)

        {

            if (doc == null || !doc.IsFamilyDocument)

                return Fail("документ не является семейством");



            var views = CollectCandidateViews(doc, uiDoc);

            if (views.Count == 0)

                return Fail("в семействе нет подходящих видов для экспорта");



            Directory.CreateDirectory(PreviewDir);

            var errors = new List<string>();



            foreach (var view in views)

            {

                try

                {

                    var path = TryExportSingleView(doc, uiDoc, view);

                    if (!string.IsNullOrWhiteSpace(path))

                        return new ThumbnailExportResult { FilePath = path };

                }

                catch (Exception ex)

                {

                    errors.Add($"{view.Name}: {ex.Message}");

                }

            }



            var detail = errors.Count > 0

                ? string.Join("; ", errors.Take(3))

                : "ExportImage не создал файл";

            return Fail(detail);

        }



        private static ThumbnailExportResult Fail(string detail) =>

            new ThumbnailExportResult { ErrorDetail = detail };



        private static List<View> CollectCandidateViews(Document doc, UIDocument uiDoc)

        {

            var list = new List<View>();

            var seen = new HashSet<int>();



            void Add(View v)

            {

                if (v == null || v.IsTemplate || seen.Contains(v.Id.IntegerValue))

                    return;

                if (v.ViewType == ViewType.DrawingSheet)

                    return;

                seen.Add(v.Id.IntegerValue);

                list.Add(v);

            }



            var view3ds = new FilteredElementCollector(doc)

                .OfClass(typeof(View3D))

                .Cast<View3D>()

                .Where(v => !v.IsTemplate)

                .OrderByDescending(v => string.Equals(v.Name, "{3D}", StringComparison.OrdinalIgnoreCase))

                .ThenByDescending(v => v.Name?.IndexOf("3D", StringComparison.OrdinalIgnoreCase) >= 0)

                .ToList();

            foreach (var v in view3ds)

                Add(v);



            if (uiDoc?.ActiveView is View active)

                Add(active);



            var plans = new FilteredElementCollector(doc)

                .OfClass(typeof(View))

                .Cast<View>()

                .Where(v => !v.IsTemplate &&

                            (v.ViewType == ViewType.FloorPlan ||

                             v.ViewType == ViewType.CeilingPlan ||

                             v.ViewType == ViewType.Elevation ||

                             v.ViewType == ViewType.Section))

                .Take(4);

            foreach (var v in plans)

                Add(v);



            var created = Ensure3DView(doc);

            if (created != null)

                Add(created);



            return list;

        }



        private static View3D Ensure3DView(Document doc)

        {

            var existing = new FilteredElementCollector(doc)

                .OfClass(typeof(View3D))

                .Cast<View3D>()

                .FirstOrDefault(v => !v.IsTemplate);

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

                ApplyPreviewDisplaySettings(doc, created);

                tx.Commit();

                return created;

            }

        }



        private static string TryExportSingleView(Document doc, UIDocument uiDoc, View view)

        {

            var previousThinLines = ThinLinesOptions.AreThinLinesEnabled;

            ThinLinesOptions.AreThinLinesEnabled = true;



            try

            {

                using (var group = new TransactionGroup(doc, "FamilyMang preview export"))

                {

                    group.Start();



                    using (var tx = new Transaction(doc, "FamilyMang preview display"))

                    {

                        tx.Start();

                        ApplyPreviewDisplaySettings(doc, view);

                        tx.Commit();

                    }



                    ActivateView(uiDoc, doc, view);



                    var sessionDir = Path.Combine(PreviewDir, Guid.NewGuid().ToString("N"));

                    Directory.CreateDirectory(sessionDir);

                    var stamp = DateTime.UtcNow;

                    var targetPath = Path.Combine(sessionDir, SanitizeFileName(view.Name) + ".png");



                    string result = null;



                    if (TryExportWithOptions(doc, BuildOptions(targetPath, ExportRange.CurrentView),

                            sessionDir, targetPath, stamp, out var path1))

                        result = path1;



                    if (result == null)

                    {

                        var options2 = BuildOptions(targetPath, ExportRange.SetOfViews);

                        options2.SetViewsAndSheets(new List<ElementId> { view.Id });

                        if (TryExportWithOptions(doc, options2, sessionDir, targetPath, stamp, out var path2))

                            result = path2;

                    }



                    group.RollBack();

                    return result;

                }

            }

            finally

            {

                ThinLinesOptions.AreThinLinesEnabled = previousThinLines;

            }

        }



        /// <summary>Fine, Shaded, без аннотаций на время снимка.</summary>

        private static void ApplyPreviewDisplaySettings(Document doc, View view)

        {

            if (view == null)

                return;



            view.DetailLevel = ViewDetailLevel.Fine;

            SetShadedDisplayStyle(view);

            HideAnnotationsTemporary(doc, view);

        }



        private static void SetShadedDisplayStyle(View view)

        {

            try

            {

                view.DisplayStyle = DisplayStyle.Shading;

                return;

            }

            catch

            {

                // ignored

            }



            try

            {

                view.DisplayStyle = DisplayStyle.ShadingWithEdges;

            }

            catch

            {

                // остаётся текущий стиль вида

            }

        }



        private static void HideAnnotationsTemporary(Document doc, View view)

        {

            if (doc?.Settings?.Categories == null)

                return;



            foreach (Category cat in doc.Settings.Categories)

            {

                if (cat == null || cat.CategoryType != CategoryType.Annotation)

                    continue;

                if (!cat.get_AllowsVisibilityControl(view))

                    continue;



                try

                {

                    view.HideCategoryTemporary(cat.Id);

                }

                catch

                {

                    // категория может не поддерживаться на этом виде

                }

            }

        }



        private static void ActivateView(UIDocument uiDoc, Document doc, View view)

        {

            if (uiDoc == null || !ReferenceEquals(uiDoc.Document, doc))

                return;



            try

            {

                uiDoc.ActiveView = view;

            }

            catch

            {

                try { uiDoc.RequestViewChange(view); }

                catch { /* SetOfViews fallback */ }

            }

        }



        private static ImageExportOptions BuildOptions(string filePath, ExportRange range) =>

            new ImageExportOptions

            {

                FilePath = filePath,

                FitDirection = FitDirectionType.Horizontal,

                HLRandWFViewsFileType = ImageFileType.PNG,

                ShadowViewsFileType = ImageFileType.PNG,

                ImageResolution = ImageResolution.DPI_300,

                ZoomType = ZoomFitType.FitToPage,

                PixelSize = 640,

                ExportRange = range

            };



        private static bool TryExportWithOptions(

            Document doc,

            ImageExportOptions options,

            string sessionDir,

            string targetPath,

            DateTime stamp,

            out string resultPath)

        {

            doc.ExportImage(options);

            resultPath = FindExportedPng(sessionDir, targetPath, stamp);

            return !string.IsNullOrWhiteSpace(resultPath);

        }



        private static string SanitizeFileName(string name)

        {

            if (string.IsNullOrWhiteSpace(name))

                return "view";

            foreach (var c in Path.GetInvalidFileNameChars())

                name = name.Replace(c, '_');

            return name;

        }



        private static string FindExportedPng(string sessionDir, string expectedPath, DateTime notBeforeUtc)

        {

            var cutoff = notBeforeUtc.AddSeconds(-5);



            if (File.Exists(expectedPath) && new FileInfo(expectedPath).Length > 0)

                return expectedPath;



            var candidates = new List<string>();

            if (Directory.Exists(sessionDir))

                candidates.AddRange(Directory.GetFiles(sessionDir, "*.png", SearchOption.AllDirectories));



            return candidates

                .Where(p => File.GetLastWriteTimeUtc(p) >= cutoff && new FileInfo(p).Length > 0)

                .OrderByDescending(p => File.GetLastWriteTimeUtc(p))

                .FirstOrDefault();

        }

    }

}


