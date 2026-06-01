using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
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
                        "Нет открытого документа.");
                    return Result.Cancelled;
                }

                if (!doc.IsFamilyDocument)
                {
                    TaskDialog.Show("FamilyMang",
                        "Откройте семейство в редакторе семейств Revit.\n\n" +
                        "Выгрузка выполняется из редактора: основное семейство и все вложенные.");
                    return Result.Cancelled;
                }

                FamilyUploadBundle bundle;
                try
                {
                    bundle = FamilyExtractor.CollectUploadBundle(doc);
                }
                catch (Exception ex)
                {
                    TaskDialog.Show("FamilyMang — Ошибка", ex.Message);
                    return Result.Failed;
                }

                if (bundle.Items.Count == 0)
                {
                    TaskDialog.Show("FamilyMang",
                        "В редакторе не найдены семейства для выгрузки.");
                    return Result.Cancelled;
                }

                var window = new UploadWindow(bundle);
                IntPtr hwnd = Process.GetCurrentProcess().MainWindowHandle;
                if (hwnd != IntPtr.Zero)
                    new System.Windows.Interop.WindowInteropHelper(window).Owner = hwnd;

                if (window.ShowDialog() != true)
                    return Result.Succeeded;

                var settings = PluginSettings.Load();

                ExtractedUploadBundle extracted;
                try
                {
                    extracted = FamilyExtractor.ExtractBundle(doc, bundle);
                }
                catch (Exception ex)
                {
                    TaskDialog.Show("FamilyMang — Ошибка",
                        $"Не удалось подготовить файлы семейств:\n{ex.Message}");
                    return Result.Failed;
                }

                var uiDoc = commandData.Application.ActiveUIDocument;
                var thumbExport = FamilyThumbnailExporter.TryExportDetailed(doc, uiDoc);
                string hostThumbnailPath = thumbExport.FilePath;
                string thumbnailExportNote = thumbExport.Success
                    ? null
                    : "Превью: не удалось снять снимок в Revit" +
                      (string.IsNullOrWhiteSpace(thumbExport.ErrorDetail)
                          ? ""
                          : " (" + thumbExport.ErrorDetail + ")");

                string resultMessage = Task.Run(() =>
                    UploadExtractedBundleAsync(settings, extracted, hostThumbnailPath, thumbnailExportNote))
                    .GetAwaiter().GetResult();

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

        private static async Task<string> UploadExtractedBundleAsync(
            PluginSettings settings,
            ExtractedUploadBundle extracted,
            string hostThumbnailPath,
            string thumbnailExportNote = null)
        {
            using (var auth = new JwtAuthService(settings.ServerUrl, settings.CompanyId))
            using (var client = new ApiClient(auth))
            {
                client.BaseUrl = settings.ServerUrl;

                var primaryData = extracted.Primary;
                var nestedPreview = extracted.Nested.Select(n => new Dictionary<string, object>
                {
                    { "family_name", n.FamilyName },
                    { "category", n.Category ?? "" },
                    { "role", "nested" }
                }).ToList<object>();

                var primaryResult = await UploadSingleAsync(
                    client, primaryData,
                    settings.CompanyId,
                    parentFamilyId: null,
                    parentFamilyName: null,
                    nestedPreview: nestedPreview,
                    thumbnailPath: hostThumbnailPath).ConfigureAwait(false);

                int nestedOk = 0;
                var nestedErrors = new List<string>(extracted.NestedErrors);
                var nestedVersionLines = new List<string>();

                foreach (var nestedData in extracted.Nested)
                {
                    try
                    {
                        var nestedResult = await UploadSingleAsync(
                            client, nestedData,
                            settings.CompanyId,
                            parentFamilyId: primaryResult.FamilyId,
                            parentFamilyName: primaryData.FamilyName,
                            nestedPreview: null).ConfigureAwait(false);
                        nestedOk++;
                        nestedVersionLines.Add(
                            $"  • {nestedData.FamilyName}: {FormatVersionLine(nestedResult)}");
                    }
                    catch (Exception ex)
                    {
                        nestedErrors.Add($"{nestedData.FamilyName}: {ex.Message}");
                    }
                }

                var lines = new List<string>
                {
                    $"Основное семейство «{primaryData.FamilyName}»: {FormatVersionLine(primaryResult)}",
                    $"ID: {primaryResult.FamilyId}",
                    $"Вложенных: {nestedOk} из {extracted.Nested.Count}"
                };

                if (!string.IsNullOrWhiteSpace(thumbnailExportNote))
                    lines.Add(thumbnailExportNote);
                else if (!string.IsNullOrWhiteSpace(primaryResult.ThumbnailNote))
                    lines.Add(primaryResult.ThumbnailNote);

                if (nestedVersionLines.Count > 0)
                {
                    lines.Add("");
                    lines.AddRange(nestedVersionLines);
                }

                if (nestedErrors.Count > 0)
                {
                    lines.Add("");
                    lines.Add("Ошибки вложенных:");
                    lines.AddRange(nestedErrors);
                }

                return string.Join("\n", lines);
            }
        }

        private static string FormatVersionLine(FamilyUploadResult result)
        {
            if (result.Unchanged)
                return $"без изменений (v{result.Version})";
            if (result.IsNew)
                return $"создано v{result.Version}";
            return $"обновлено до v{result.Version}";
        }

        private static async Task<FamilyUploadResult> UploadSingleAsync(
            ApiClient client,
            ExtractedFamilyData data,
            string companyId,
            string parentFamilyId,
            string parentFamilyName,
            List<object> nestedPreview,
            string thumbnailPath = null)
        {
            InitUploadResponseDto init;
            try
            {
                init = await client.InitUploadAsync(
                    data.OriginalFilename,
                    data.SizeBytes,
                    data.Sha256,
                    data.FamilyName,
                    data.Category,
                    data.IsPrimary,
                    parentFamilyId).ConfigureAwait(false);
            }
            catch (AuthException ex)
            {
                throw new Exception(
                    $"Ошибка аутентификации (проверьте Company ID):\n{ex.Message}", ex);
            }
            catch (HttpRequestException ex)
            {
                throw new Exception($"Ошибка init-upload:\n{ex.Message}", ex);
            }

            var version = init.version > 0 ? init.version : 1;
            if (init.unchanged)
            {
                string thumbNote = null;
                if (data.IsPrimary)
                {
                    thumbNote = await FamilyThumbnailUpload.TryUploadHostThumbnailAsync(
                        client, init.family_id, thumbnailPath, init).ConfigureAwait(false);
                }

                return new FamilyUploadResult
                {
                    FamilyId = init.family_id,
                    Version = version,
                    IsNew = false,
                    Unchanged = true,
                    ThumbnailNote = thumbNote ??
                        (data.IsPrimary
                            ? $"Семейство .rfa без изменений (v{version}); превью не обновлено"
                            : null)
                };
            }

            string etag = null;
            if (!string.IsNullOrWhiteSpace(init.presigned_put_url))
            {
                try
                {
                    etag = await client.UploadToS3Async(
                        init.presigned_put_url, data.FilePath).ConfigureAwait(false);
                }
                catch (HttpRequestException ex)
                {
                    throw new Exception($"Ошибка загрузки файла в S3:\n{ex.Message}", ex);
                }
            }

            var extra = new Dictionary<string, object>
            {
                { "is_primary", data.IsPrimary },
                { "role", data.IsPrimary ? "host" : "nested" },
                { "version", version },
                { "uploaded_by_company_id", companyId ?? "" },
                { "uploaded_by_windows_user", Environment.UserName }
            };

            if (!string.IsNullOrEmpty(parentFamilyId))
            {
                extra["parent_family_id"] = parentFamilyId;
                extra["parent_family_name"] = parentFamilyName ?? "";
            }

            if (nestedPreview != null && nestedPreview.Count > 0)
                extra["nested_children"] = nestedPreview;

            var metadata = new Dictionary<string, object>
            {
                { "family_name", data.FamilyName },
                { "category", data.Category ?? "" },
                { "parameters", data.Parameters },
                { "types", data.Types },
                { "extra", extra }
            };

            try
            {
                await client.PostMetadataAsync(init.family_id, metadata).ConfigureAwait(false);
            }
            catch (HttpRequestException ex)
            {
                throw new Exception($"Ошибка отправки метаданных:\n{ex.Message}", ex);
            }

            try
            {
                await client.CompleteUploadAsync(init.family_id, etag).ConfigureAwait(false);
            }
            catch (HttpRequestException ex)
            {
                throw new Exception($"Ошибка завершения загрузки:\n{ex.Message}", ex);
            }

            string thumbnailNote = null;
            if (data.IsPrimary)
            {
                thumbnailNote = await FamilyThumbnailUpload.TryUploadHostThumbnailAsync(
                    client, init.family_id, thumbnailPath, init).ConfigureAwait(false);
            }

            return new FamilyUploadResult
            {
                FamilyId = init.family_id,
                Version = version,
                IsNew = init.is_new,
                Unchanged = false,
                ThumbnailNote = thumbnailNote
            };
        }
    }
}
