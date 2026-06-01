using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;

namespace FamilyMang
{
    internal static class FamilyThumbnailUpload
    {
        public static async Task<string> TryUploadHostThumbnailAsync(
            ApiClient client,
            string familyId,
            string thumbnailPath,
            InitUploadResponseDto initResponse)
        {
            if (string.IsNullOrWhiteSpace(thumbnailPath) || !File.Exists(thumbnailPath))
                return "Превью: не удалось снять снимок в Revit";

            try
            {
                var putUrl = initResponse?.presigned_thumbnail_put_url;

                if (string.IsNullOrWhiteSpace(putUrl))
                {
                    var thumbInit = await client.InitThumbnailUploadAsync(familyId)
                        .ConfigureAwait(false);
                    putUrl = thumbInit?.presigned_put_url;
                }

                if (string.IsNullOrWhiteSpace(putUrl))
                    return "Превью: сервер не выдал URL (нужен backend rev.5.1: thumbnail/init-upload)";

                await client.UploadThumbnailToS3Async(putUrl, thumbnailPath).ConfigureAwait(false);

                try
                {
                    await client.CompleteThumbnailUploadAsync(familyId).ConfigureAwait(false);
                }
                catch (HttpRequestException)
                {
                    // complete основного upload мог уже выставить has_thumbnail
                }

                return "Превью: загружено на сервер";
            }
            catch (HttpRequestException ex)
            {
                return "Превью: ошибка загрузки — " + ex.Message;
            }
            catch (Exception ex)
            {
                return "Превью: " + ex.Message;
            }
        }
    }
}
