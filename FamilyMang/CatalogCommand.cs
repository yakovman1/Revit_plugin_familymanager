using System;
using System.Diagnostics;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace FamilyMang
{
    [Transaction(TransactionMode.Manual)]
    public class CatalogCommand : IExternalCommand
    {
        public Result Execute(
            ExternalCommandData commandData,
            ref string message,
            ElementSet elements)
        {
            try
            {
                var window = new CatalogWindow();

                IntPtr revitHandle = Process.GetCurrentProcess().MainWindowHandle;
                if (revitHandle != IntPtr.Zero)
                    new System.Windows.Interop.WindowInteropHelper(window).Owner = revitHandle;

                bool? dialogResult = window.ShowDialog();

                if (dialogResult != true || string.IsNullOrEmpty(window.DownloadedFilePath))
                    return Result.Succeeded;

                Document doc = commandData.Application.ActiveUIDocument?.Document;
                if (doc == null)
                {
                    TaskDialog.Show("FamilyMang",
                        "Нет открытого документа. Откройте проект Revit и попробуйте снова.");
                    return Result.Cancelled;
                }

                bool loaded = FamilyLoader.LoadIntoDocument(doc, window.DownloadedFilePath);

                TaskDialog.Show("FamilyMang",
                    loaded
                        ? "Семейство успешно загружено в проект."
                        : "Не удалось загрузить семейство. Возможно, оно уже присутствует в проекте.");

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return Result.Failed;
            }
        }
    }
}
