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



            var isAnnotation = IsAnnotationFamily(doc);

            var views = isAnnotation

                ? CollectPlanViewsForAnnotation(doc, uiDoc)

                : CollectCandidateViews(doc, uiDoc);

            Directory.CreateDirectory(PreviewDir);



            if (views.Count == 0 && isAnnotation)

            {

                var tempResult = TryExportAnnotationWithTemporaryView(doc, uiDoc);

                if (tempResult != null)

                    return new ThumbnailExportResult { FilePath = tempResult };



                return Fail(

                    "в марке нет листа (Sheet) и не удалось создать временный вид для снимка " +

                    "(добавьте лист с символом марки или откройте семейство в редакторе)");

            }



            if (views.Count == 0)

                return Fail("в семействе нет подходящих видов для экспорта");

            var errors = new List<string>();



            foreach (var view in views)

            {

                try

                {

                    var path = TryExportSingleView(doc, uiDoc, view, isAnnotation);

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

                : views.Count == 1 && IsSheetView(views[0])

                    ? $"ExportImage не создал файл с листа «{FormatSheetLabel(views[0] as ViewSheet)}»"

                    : "ExportImage не создал файл";

            return Fail(detail);

        }



        private static ThumbnailExportResult Fail(string detail) =>

            new ThumbnailExportResult { ErrorDetail = detail };



        private static bool IsAnnotationFamily(Document doc)

        {

            var category = doc?.OwnerFamily?.FamilyCategory;

            return category != null && category.CategoryType == CategoryType.Annotation;

        }



        /// <summary>

        /// Марка: если есть лист (Sheet) — только он (без планов и размеров на них).

        /// Иначе — Legend/Drafting/планы; при отсутствии видов — временный план.

        /// </summary>

        private static List<View> CollectPlanViewsForAnnotation(Document doc, UIDocument uiDoc)

        {

            var sheet = FindAnnotationSheet(doc);

            if (sheet != null)

                return new List<View> { sheet };



            var list = new List<View>();

            var seen = new HashSet<int>();



            void Add(View v, bool insertFirst)

            {

                if (v == null || !IsAnnotationExportableView(v) || seen.Contains(v.Id.IntegerValue))

                    return;

                seen.Add(v.Id.IntegerValue);

                if (insertFirst)

                    list.Insert(0, v);

                else

                    list.Add(v);

            }



            foreach (var v in new FilteredElementCollector(doc)

                         .OfClass(typeof(View))

                         .Cast<View>()

                         .Where(IsAnnotationExportableView)

                         .OrderBy(GetAnnotationViewSortKey)

                         .ThenBy(v => v.Name, StringComparer.CurrentCultureIgnoreCase))

                Add(v, insertFirst: false);



            return list;

        }



        /// <summary>Стандартный лист марки в браузере: Sheets → «-» (номер листа).</summary>

        private static View FindAnnotationSheet(Document doc)

        {

            var sheets = new FilteredElementCollector(doc)

                .OfClass(typeof(ViewSheet))

                .Cast<ViewSheet>()

                .Where(v => !v.IsTemplate)

                .ToList();



            if (sheets.Count == 0)

            {

                sheets = new FilteredElementCollector(doc)

                    .OfClass(typeof(View))

                    .Cast<View>()

                    .Where(v => !v.IsTemplate && v.ViewType == ViewType.DrawingSheet)

                    .OfType<ViewSheet>()

                    .ToList();

            }



            if (sheets.Count == 0)

                return null;



            // Обычно в марке один лист без номера/имени (в браузере отображается как «-»).

            if (sheets.Count == 1)

                return sheets[0];



            var standard = sheets.FirstOrDefault(IsStandardTagSheet);

            if (standard != null)

                return standard;



            return sheets.First();

        }



        private static bool IsStandardTagSheet(ViewSheet sheet)

        {

            if (sheet == null)

                return false;



            var number = sheet.SheetNumber?.Trim() ?? "";

            var name = sheet.Name?.Trim() ?? "";



            if (number.Length == 0 && name.Length == 0)

                return true;



            if (string.Equals(number, "-", StringComparison.Ordinal))

                return true;

            if (string.Equals(name, "-", StringComparison.Ordinal))

                return true;



            return false;

        }



        private static IList<View> GetViewsPlacedOnSheet(Document doc, ViewSheet sheet)

        {

            var views = new List<View>();

            if (doc == null || sheet == null)

                return views;



            foreach (var vp in new FilteredElementCollector(doc, sheet.Id)

                         .OfClass(typeof(Viewport))

                         .Cast<Viewport>())

            {

                if (doc.GetElement(vp.ViewId) is View v && !v.IsTemplate)

                    views.Add(v);

            }



            return views;

        }



        private static bool IsSheetView(View view) =>

            view != null && view.ViewType == ViewType.DrawingSheet;



        private static bool IsAnnotationExportableView(View view)

        {

            if (view == null || view.IsTemplate)

                return false;



            switch (view.ViewType)

            {

                case ViewType.DrawingSheet:

                case ViewType.Schedule:

                    return false;

                default:

                    return true;

            }

        }



        private static int GetAnnotationViewSortKey(View view)

        {

            switch (view.ViewType)

            {

                case ViewType.Legend:

                    return 0;

                case ViewType.DraftingView:

                    return 1;

                case ViewType.FloorPlan:

                    return 2;

                case ViewType.CeilingPlan:

                    return 3;

                case ViewType.EngineeringPlan:

                    return 4;

                case ViewType.AreaPlan:

                    return 5;

                case ViewType.Detail:

                    return 6;

                case ViewType.Section:

                    return 7;

                case ViewType.Elevation:

                    return 8;

                case ViewType.ThreeD:

                    return 50;

                default:

                    return 20;

            }

        }



        /// <summary>

        /// Марка без видов в браузере: создаём план/чертёж/3D, ExportImage, откат — в .rfa не остаётся лишних видов.

        /// </summary>

        private static string TryExportAnnotationWithTemporaryView(Document doc, UIDocument uiDoc)

        {

            var previousThinLines = ThinLinesOptions.AreThinLinesEnabled;

            ThinLinesOptions.AreThinLinesEnabled = true;



            try

            {

                using (var group = new TransactionGroup(doc, "FamilyMang tag preview"))

                {

                    group.Start();



                    View view;

                    using (var tx = new Transaction(doc, "FamilyMang create preview view"))

                    {

                        tx.Start();

                        view = TryCreateAnnotationViewInTransaction(doc);

                        if (view == null)

                        {

                            tx.RollBack();

                            group.RollBack();

                            return null;

                        }



                        try

                        {

                            view.Name = "FamilyMang_Preview";

                        }

                        catch

                        {

                            // имя может быть занято

                        }



                        ApplyPreviewDisplaySettings(doc, view, isAnnotationFamily: true);

                        TryZoomFamilyContent(doc, view);

                        tx.Commit();

                    }



                    ActivateView(uiDoc, doc, view);

                    TryZoomViewToContent(uiDoc, doc, view);



                    var path = ExportViewToPng(doc, uiDoc, view, isAnnotationFamily: true);

                    group.RollBack();

                    return path;

                }

            }

            finally

            {

                ThinLinesOptions.AreThinLinesEnabled = previousThinLines;

            }

        }



        private static View TryCreateAnnotationViewInTransaction(Document doc)

        {

            var families = new[]

            {

                ViewFamily.FloorPlan,

                ViewFamily.Drafting,

                ViewFamily.Detail,

                ViewFamily.ThreeDimensional

            };



            var vfts = new FilteredElementCollector(doc)

                .OfClass(typeof(ViewFamilyType))

                .Cast<ViewFamilyType>()

                .ToList();



            foreach (var family in families)

            {

                foreach (var vft in vfts.Where(v => v.ViewFamily == family))

                {

                    var created = TryCreateViewForFamily(doc, vft);

                    if (created != null)

                        return created;

                }

            }



            return null;

        }



        private static View TryCreateViewForFamily(Document doc, ViewFamilyType vft)

        {

            if (vft == null)

                return null;



            try

            {

                switch (vft.ViewFamily)

                {

                    case ViewFamily.FloorPlan:

                    case ViewFamily.CeilingPlan:

                    case ViewFamily.StructuralPlan:

                    {

                        var levelId = TryGetOrCreateReferenceLevelId(doc);

                        if (levelId == ElementId.InvalidElementId)

                            return null;

                        return ViewPlan.Create(doc, vft.Id, levelId);

                    }

                    case ViewFamily.Drafting:

                        return ViewDrafting.Create(doc, vft.Id);

                    case ViewFamily.ThreeDimensional:

                        return View3D.CreateIsometric(doc, vft.Id);

                    default:

                        return null;

                }

            }

            catch

            {

                return null;

            }

        }



        private static void TryZoomFamilyContent(Document doc, View view)

        {

            if (doc == null || view == null)

                return;



            try

            {

                var box = ComputeFamilyContentBox(doc, view);

                if (box == null)

                    return;



                var width = box.Max.X - box.Min.X;

                var height = box.Max.Y - box.Min.Y;

                if (width < 1e-6 && height < 1e-6)

                    return;



                var margin = Math.Max(Math.Max(width, height) * 0.2, 0.1);

                view.CropBoxActive = true;

                view.CropBoxVisible = false;

                view.CropBox = new BoundingBoxXYZ

                {

                    Min = new XYZ(box.Min.X - margin, box.Min.Y - margin, box.Min.Z - 1),

                    Max = new XYZ(box.Max.X + margin, box.Max.Y + margin, box.Max.Z + 1)

                };

            }

            catch

            {

                // оставляем масштаб по умолчанию

            }

        }



        private static BoundingBoxXYZ ComputeFamilyContentBox(Document doc, View view)

        {

            BoundingBoxXYZ merged = null;



            foreach (var element in new FilteredElementCollector(doc)

                         .WhereElementIsNotElementType()

                         .Where(e => e.Category != null && e.Category.CategoryType != CategoryType.Internal))

            {

                var bb = element.get_BoundingBox(view) ?? element.get_BoundingBox(null);

                if (bb == null)

                    continue;



                if (merged == null)

                {

                    merged = new BoundingBoxXYZ

                    {

                        Min = bb.Min,

                        Max = bb.Max

                    };

                    continue;

                }



                merged.Min = new XYZ(

                    Math.Min(merged.Min.X, bb.Min.X),

                    Math.Min(merged.Min.Y, bb.Min.Y),

                    Math.Min(merged.Min.Z, bb.Min.Z));

                merged.Max = new XYZ(

                    Math.Max(merged.Max.X, bb.Max.X),

                    Math.Max(merged.Max.Y, bb.Max.Y),

                    Math.Max(merged.Max.Z, bb.Max.Z));

            }



            return merged;

        }



        private static View EnsureAnnotationPreviewView(Document doc)

        {

            View TryCreate(ViewFamily family)

            {

                var vft = new FilteredElementCollector(doc)

                    .OfClass(typeof(ViewFamilyType))

                    .Cast<ViewFamilyType>()

                    .FirstOrDefault(x => x.ViewFamily == family);

                if (vft == null)

                    return null;



                using (var tx = new Transaction(doc, "FamilyMang annotation preview view"))

                {

                    tx.Start();

                    View created = null;

                    try

                    {

                        if (family == ViewFamily.FloorPlan)

                        {

                            var levelId = TryGetOrCreateReferenceLevelId(doc);

                            if (levelId != ElementId.InvalidElementId)

                                created = ViewPlan.Create(doc, vft.Id, levelId);

                        }

                        else if (family == ViewFamily.Drafting)

                            created = ViewDrafting.Create(doc, vft.Id);

                        else if (family == ViewFamily.Legend)

                            created = TryDuplicateLegendView(doc, useOwnTransaction: false);

                    }

                    catch

                    {

                        tx.RollBack();

                        return null;

                    }



                    if (created == null)

                    {

                        tx.RollBack();

                        return null;

                    }



                    ApplyPreviewDisplaySettings(doc, created, isAnnotationFamily: true);

                    tx.Commit();

                    return created;

                }

            }



            foreach (var family in new[]

                     {

                         ViewFamily.Drafting,

                         ViewFamily.FloorPlan,

                         ViewFamily.Legend

                     })

            {

                var v = TryCreate(family);

                if (v != null)

                    return v;

            }



            return Ensure3DView(doc);

        }



        private static ElementId TryGetReferenceLevelId(Document doc)

        {

            var level = new FilteredElementCollector(doc)

                .OfClass(typeof(Level))

                .Cast<Level>()

                .OrderBy(l => l.Elevation)

                .FirstOrDefault();

            return level?.Id ?? ElementId.InvalidElementId;

        }



        private static ElementId TryGetOrCreateReferenceLevelId(Document doc)

        {

            var existing = TryGetReferenceLevelId(doc);

            if (existing != ElementId.InvalidElementId)

                return existing;



            try

            {

                var level = Level.Create(doc, 0.0);

                return level?.Id ?? ElementId.InvalidElementId;

            }

            catch

            {

                return ElementId.InvalidElementId;

            }

        }



        /// <summary>Revit API не создаёт Legend с нуля — только Duplicate существующего вида.</summary>

        private static View TryDuplicateLegendView(Document doc, bool useOwnTransaction = true)

        {

            var template = new FilteredElementCollector(doc)

                .OfClass(typeof(View))

                .Cast<View>()

                .FirstOrDefault(v => !v.IsTemplate && v.ViewType == ViewType.Legend);

            if (template == null)

                return null;



            if (!useOwnTransaction)

            {

                try

                {

                    var newId = template.Duplicate(ViewDuplicateOption.Duplicate);

                    return doc.GetElement(newId) as View;

                }

                catch

                {

                    return null;

                }

            }



            using (var tx = new Transaction(doc, "FamilyMang legend preview"))

            {

                tx.Start();

                try

                {

                    var newId = template.Duplicate(ViewDuplicateOption.Duplicate);

                    var created = doc.GetElement(newId) as View;

                    if (created == null)

                    {

                        tx.RollBack();

                        return null;

                    }



                    ApplyPreviewDisplaySettings(doc, created, isAnnotationFamily: true);

                    tx.Commit();

                    return created;

                }

                catch

                {

                    tx.RollBack();

                    return null;

                }

            }

        }



        private static bool IsPlanLikeView(View view)

        {

            if (view == null)

                return false;



            switch (view.ViewType)

            {

                case ViewType.FloorPlan:

                case ViewType.CeilingPlan:

                case ViewType.EngineeringPlan:

                case ViewType.AreaPlan:

                case ViewType.Section:

                case ViewType.Elevation:

                case ViewType.Detail:

                    return true;

                default:

                    return false;

            }

        }



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

                ApplyPreviewDisplaySettings(doc, created, isAnnotationFamily: false);

                tx.Commit();

                return created;

            }

        }



        private static string ExportViewToPng(Document doc, UIDocument uiDoc, View view, bool isAnnotationFamily)

        {

            var sessionDir = Path.Combine(PreviewDir, Guid.NewGuid().ToString("N"));

            Directory.CreateDirectory(sessionDir);

            var stamp = DateTime.UtcNow;

            var fileBase = Path.Combine(

                sessionDir,

                IsSheetView(view) ? "tag_sheet" : SanitizeFileName(view.Name));



            if (IsSheetView(view))

            {

                var sheetPath = TryExportSheetToPng(doc, uiDoc, (ViewSheet)view, sessionDir, fileBase, stamp);

                if (!string.IsNullOrWhiteSpace(sheetPath))

                    return sheetPath;

            }

            else

            {

                var setOfViews = BuildOptions(fileBase, ExportRange.SetOfViews);

                setOfViews.SetViewsAndSheets(new List<ElementId> { view.Id });

                if (TryExportWithOptions(doc, view, setOfViews, sessionDir, fileBase, stamp, out var path1))

                    return path1;



                if (TryExportWithOptions(doc, view, BuildOptions(fileBase, ExportRange.CurrentView), sessionDir, fileBase,

                        stamp, out var path2))

                    return path2;

            }



            if (isAnnotationFamily && TryExportWithOptions(doc, view,

                    BuildOptions(fileBase, ExportRange.VisibleRegionOfCurrentView), sessionDir, fileBase, stamp,

                    out var path3))

                return path3;



            return null;

        }



        private static string TryExportSheetToPng(

            Document doc,

            UIDocument uiDoc,

            ViewSheet sheet,

            string sessionDir,

            string fileBase,

            DateTime stamp)

        {

            if (sheet == null)

                return null;



            ActivateView(uiDoc, doc, sheet);



            if (TryExportWithOptions(doc, sheet, BuildOptions(fileBase, ExportRange.CurrentView), sessionDir, fileBase,

                    stamp, out var fromSheet))

                return fromSheet;



            var sheetSet = BuildOptions(fileBase, ExportRange.SetOfViews);

            sheetSet.SetViewsAndSheets(new List<ElementId> { sheet.Id });

            if (TryExportWithOptions(doc, sheet, sheetSet, sessionDir, fileBase, stamp, out var fromSet))

                return fromSet;



            foreach (var placed in GetViewsPlacedOnSheet(doc, sheet))

            {

                ActivateView(uiDoc, doc, placed);

                TryZoomViewToContent(uiDoc, doc, placed);



                if (TryExportWithOptions(doc, placed, BuildOptions(fileBase, ExportRange.CurrentView), sessionDir,

                        fileBase, stamp, out var fromVp))

                    return fromVp;



                var vpSet = BuildOptions(fileBase, ExportRange.SetOfViews);

                vpSet.SetViewsAndSheets(new List<ElementId> { placed.Id });

                if (TryExportWithOptions(doc, placed, vpSet, sessionDir, fileBase, stamp, out var fromVpSet))

                    return fromVpSet;

            }



            return null;

        }



        private static string TryExportSingleView(Document doc, UIDocument uiDoc, View view, bool isAnnotationFamily)

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

                        ApplyPreviewDisplaySettings(doc, view, isAnnotationFamily);

                        tx.Commit();

                    }



                    ActivateView(uiDoc, doc, view);

                    if (!IsSheetView(view))

                        TryZoomViewToContent(uiDoc, doc, view);



                    var result = ExportViewToPng(doc, uiDoc, view, isAnnotationFamily);

                    group.RollBack();

                    return result;

                }

            }

            finally

            {

                ThinLinesOptions.AreThinLinesEnabled = previousThinLines;

            }

        }



        /// <summary>Модель: Fine + Shaded. Марки: Fine; с листа — без Dimensions.</summary>

        private static void ApplyPreviewDisplaySettings(Document doc, View view, bool isAnnotationFamily)

        {

            if (view == null)

                return;



            view.DetailLevel = ViewDetailLevel.Fine;



            if (isAnnotationFamily)

            {

                if (view is ViewSheet sheet)

                    ApplyAnnotationSheetDisplaySettings(doc, sheet);

                else if (IsSheetView(view))

                    HideDimensionsTemporary(doc, view);

                return;

            }



            SetShadedDisplayStyle(view);

            HideAnnotationsTemporary(doc, view);

        }



        private static void ApplyAnnotationSheetDisplaySettings(Document doc, ViewSheet sheet)

        {

            if (doc == null || sheet == null)

                return;



            HideDimensionsTemporary(doc, sheet);



            foreach (var placed in GetViewsPlacedOnSheet(doc, sheet))

                HideDimensionsTemporary(doc, placed);

        }



        private static void HideDimensionsTemporary(Document doc, View view)

        {

            if (doc == null || view == null)

                return;



            var categories = new[]

            {

                BuiltInCategory.OST_Dimensions,

                BuiltInCategory.OST_SpotElevations,

                BuiltInCategory.OST_SpotCoordinates

            };



            foreach (var builtIn in categories)

            {

                try

                {

                    var cat = Category.GetCategory(doc, builtIn);

                    if (cat == null || !cat.get_AllowsVisibilityControl(view))

                        continue;

                    view.HideCategoryTemporary(cat.Id);

                }

                catch

                {

                    // ignored

                }

            }

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

            if (uiDoc == null || !ReferenceEquals(uiDoc.Document, doc) || view == null)

                return;



            try

            {

                uiDoc.ActiveView = view;

                uiDoc.RefreshActiveView();

                return;

            }

            catch

            {

                // ignored

            }



            try

            {

                uiDoc.RequestViewChange(view);

                uiDoc.RefreshActiveView();

            }

            catch

            {

                /* SetOfViews fallback */

            }

        }



        private static void TryZoomViewToContent(UIDocument uiDoc, Document doc, View view)

        {

            if (uiDoc == null || view == null || !ReferenceEquals(uiDoc.Document, doc))

                return;



            try

            {

                ActivateView(uiDoc, doc, view);

                uiDoc.RefreshActiveView();



                foreach (var uiView in uiDoc.GetOpenUIViews() ?? Enumerable.Empty<UIView>())

                {

                    if (uiView.ViewId != view.Id)

                        continue;

                    uiView.ZoomToFit();

                    return;

                }

            }

            catch

            {

                // вид может быть не открыт в UI — экспорт SetOfViews всё равно попробуем

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

            View view,

            ImageExportOptions options,

            string sessionDir,

            string fileBase,

            DateTime stamp,

            out string resultPath)

        {

            resultPath = null;

            try

            {

                doc.ExportImage(options);

            }

            catch

            {

                return false;

            }



            resultPath = ResolveExportedPngPath(doc, view, options, sessionDir, fileBase, stamp);

            return !string.IsNullOrWhiteSpace(resultPath);

        }



        private static string ResolveExportedPngPath(

            Document doc,

            View view,

            ImageExportOptions options,

            string sessionDir,

            string fileBase,

            DateTime stamp)

        {

            if (view != null && options != null)

            {

                try

                {

                    var generatedName = ImageExportOptions.GetFileName(doc, view.Id);

                    if (!string.IsNullOrWhiteSpace(generatedName))

                    {

                        var fromApi = Path.Combine(sessionDir, generatedName + ".png");

                        if (File.Exists(fromApi) && new FileInfo(fromApi).Length > 0)

                            return fromApi;



                        fromApi = Path.Combine(sessionDir, generatedName + ".PNG");

                        if (File.Exists(fromApi) && new FileInfo(fromApi).Length > 0)

                            return fromApi;

                    }

                }

                catch

                {

                    // GetFileName недоступен для некоторых режимов — ищем по папке

                }

            }



            var expected = fileBase + ".png";

            return FindExportedPng(sessionDir, expected, stamp);

        }



        private static string FormatSheetLabel(ViewSheet sheet)

        {

            if (sheet == null)

                return "—";

            var number = sheet.SheetNumber?.Trim();

            var name = sheet.Name?.Trim();

            if (!string.IsNullOrEmpty(number))

                return number;

            if (!string.IsNullOrEmpty(name))

                return name;

            return "(-)";

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



            var fromSession = candidates

                .Where(p => File.GetLastWriteTimeUtc(p) >= cutoff && new FileInfo(p).Length > 0)

                .OrderByDescending(p => File.GetLastWriteTimeUtc(p))

                .FirstOrDefault();

            if (!string.IsNullOrWhiteSpace(fromSession))

                return fromSession;



            if (!Directory.Exists(PreviewDir))

                return null;



            return Directory.GetFiles(PreviewDir, "*.png", SearchOption.AllDirectories)

                .Where(p => File.GetLastWriteTimeUtc(p) >= cutoff && new FileInfo(p).Length > 0)

                .OrderByDescending(p => File.GetLastWriteTimeUtc(p))

                .FirstOrDefault();

        }

    }

}


