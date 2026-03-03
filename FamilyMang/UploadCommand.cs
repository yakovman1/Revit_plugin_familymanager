using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Threading.Tasks;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace FamilyMang
{
    [Transaction(TransactionMode.Manual)]
    public class UploadCommand : IExternalCommand
    {
        public Result Execute(
            ExternalCommandData commandData,
            ref string message,
            ElementSet elements)
        {
            try
            {
                Document doc = commandData.Application.ActiveUIDocument?.Document;
                if (doc == null)
                {
                    TaskDialog.Show("FamilyMang",
                        "Нет открытого документа. Откройте проект Revit.");
                    return Result.Cancelled;
                }

                var families = FamilyExtractor.CollectFamilies(doc);
                if (families.Count == 0)
                {
                    TaskDialog.Show("FamilyMang",
                        "В документе не найдены загружаемые семейства.");
                    return Result.Cancelled;
                }

                var window = new UploadWindow(families);
                IntPtr hwnd = Process.GetCurrentProcess().MainWindowHandle;
                if (hwnd != IntPtr.Zero)
                    new System.Windows.Interop.WindowInteropHelper(window).Owner = hwnd;

                if (window.ShowDialog() != true || window.SelectedElementId < 0)
                    return Result.Succeeded;

                var settings = PluginSettings.Load();
                int selectedId = window.SelectedElementId;

                ExtractedFamilyData data;
                try
                {
                    data = FamilyExtractor.ExtractAndSave(doc, selectedId);
                }
                catch (Exception ex)
                {
                    TaskDialog.Show("FamilyMang — Ошибка",
                        $"Не удалось извлечь данные семейства:\n{ex.Message}");
                    return Result.Failed;
                }

                string resultMessage = Task.Run(() =>
                    PerformUploadAsync(settings, data)).GetAwaiter().GetResult();

                TaskDialog.Show("FamilyMang", resultMessage);
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                TaskDialog.Show("FamilyMang — Ошибка", ex.Message);
                return Result.Failed;
            }
        }

        private static async Task<string> PerformUploadAsync(
            PluginSettings settings, ExtractedFamilyData data)
        {
            using (var client = new ApiClient())
            {
                client.BaseUrl = settings.ServerUrl;
                client.Token = settings.JwtToken;

                InitUploadResponseDto init;
                try
                {
                    init = await client.InitUploadAsync(
                        settings.ProjectId,
                        data.OriginalFilename,
                        data.SizeBytes,
                        data.Sha256).ConfigureAwait(false);
                }
                catch (HttpRequestException ex)
                {
                    throw new Exception(
                        $"Ошибка init-upload (проверьте токен и Project ID):\n{ex.Message}", ex);
                }

                string etag;
                try
                {
                    etag = await client.UploadToS3Async(
                        init.presigned_put_url, data.FilePath).ConfigureAwait(false);
                }
                catch (HttpRequestException ex)
                {
                    throw new Exception(
                        $"Ошибка загрузки файла в S3:\n{ex.Message}", ex);
                }

                var metadata = new Dictionary<string, object>
                {
                    { "family_name", data.FamilyName },
                    { "category", data.Category ?? "" },
                    { "parameters", data.Parameters },
                    { "types", data.Types },
                    { "extra", new Dictionary<string, object>() }
                };

                try
                {
                    await client.PostMetadataAsync(
                        init.family_id, metadata).ConfigureAwait(false);
                }
                catch (HttpRequestException ex)
                {
                    throw new Exception(
                        $"Ошибка отправки метаданных:\n{ex.Message}", ex);
                }

                try
                {
                    await client.CompleteUploadAsync(
                        init.family_id, etag).ConfigureAwait(false);
                }
                catch (HttpRequestException ex)
                {
                    throw new Exception(
                        $"Ошибка завершения загрузки:\n{ex.Message}", ex);
                }

                return $"Семейство «{data.FamilyName}» успешно загружено!\n\n" +
                       $"ID: {init.family_id}\n" +
                       $"Файл: {data.OriginalFilename}\n" +
                       $"Размер: {data.SizeBytes / 1024.0:F1} KB\n" +
                       $"Параметров: {data.Parameters.Count}\n" +
                       $"Типоразмеров: {data.Types.Count}";
            }
        }
    }
}
