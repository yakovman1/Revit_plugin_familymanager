using System;
using System.Threading.Tasks;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace FamilyMang
{
    /// <summary>
    /// Выгрузка семейства в хранилище после подтверждения в немодальном окне.
    /// </summary>
    public sealed class UploadWorkflowHandler : IExternalEventHandler
    {
        private static UploadWorkflowHandler _handler;
        private static ExternalEvent _externalEvent;

        private FamilyUploadBundle _bundle;
        private PluginSettings _settings;

        public static void Initialize()
        {
            if (_externalEvent != null)
                return;

            _handler = new UploadWorkflowHandler();
            _externalEvent = ExternalEvent.Create(_handler);
        }

        public static void Schedule(FamilyUploadBundle bundle, PluginSettings settings)
        {
            if (bundle == null || settings == null)
                return;

            Initialize();
            _handler._bundle = bundle;
            _handler._settings = settings;
            _externalEvent.Raise();
        }

        public void Execute(UIApplication app)
        {
            var bundle = _bundle;
            var settings = _settings;
            _bundle = null;
            _settings = null;

            try
            {
                Document doc = app.ActiveUIDocument?.Document;
                if (doc == null)
                {
                    TaskDialog.Show("FamilyMang", "Нет открытого документа.");
                    return;
                }

                if (!doc.IsFamilyDocument)
                {
                    TaskDialog.Show("FamilyMang",
                        "Откройте семейство в редакторе семейств Revit и повторите выгрузку.");
                    return;
                }

                var uiDoc = app.ActiveUIDocument;
                var thumbExport = FamilyThumbnailExporter.TryExportDetailed(doc, uiDoc);

                ExtractedUploadBundle extracted;
                try
                {
                    extracted = FamilyExtractor.ExtractBundle(doc, bundle);
                }
                catch (Exception ex)
                {
                    TaskDialog.Show("FamilyMang — Ошибка",
                        $"Не удалось подготовить файлы семейств:\n{ex.Message}");
                    return;
                }

                string hostThumbnailPath = thumbExport.FilePath;
                string thumbnailExportNote = thumbExport.Success
                    ? null
                    : "Превью: не удалось снять снимок в Revit" +
                      (string.IsNullOrWhiteSpace(thumbExport.ErrorDetail)
                          ? ""
                          : " (" + thumbExport.ErrorDetail + ")");

                string resultMessage = Task.Run(() =>
                        UploadCommand.UploadExtractedBundleAsync(
                            settings, extracted, hostThumbnailPath, thumbnailExportNote))
                    .GetAwaiter().GetResult();

                TaskDialog.Show("FamilyMang", resultMessage);
            }
            catch (Exception ex)
            {
                TaskDialog.Show("FamilyMang — Ошибка", ex.Message);
            }
        }

        public string GetName() => "FamilyMang — выгрузка в хранилище";
    }
}
