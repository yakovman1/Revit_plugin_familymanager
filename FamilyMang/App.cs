using System;
using System.Reflection;
using Autodesk.Revit.UI;

namespace FamilyMang
{
    public class App : IExternalApplication
    {
        public Result OnStartup(UIControlledApplication application)
        {
            try
            {
                const string tabName = "FamilyMang";
                application.CreateRibbonTab(tabName);

                RibbonPanel panel = application.CreateRibbonPanel(tabName, "Инструменты");
                string asmPath = Assembly.GetExecutingAssembly().Location;

                var catalogBtn = panel.AddItem(
                    new PushButtonData("CatalogCmd", "Каталог", asmPath,
                        "FamilyMang.CatalogCommand")) as PushButton;
                catalogBtn.ToolTip = "Открыть каталог семейств из хранилища";

                var uploadBtn = panel.AddItem(
                    new PushButtonData("UploadCmd", "Загрузка\nсемейства", asmPath,
                        "FamilyMang.UploadCommand")) as PushButton;
                uploadBtn.ToolTip = "Загрузить семейство из проекта в хранилище";

                var aboutBtn = panel.AddItem(
                    new PushButtonData("AboutCmd", "О плагине", asmPath,
                        "FamilyMang.Command")) as PushButton;
                aboutBtn.ToolTip = "Информация о плагине FamilyMang";

                FamilyLoadHandler.Initialize();
                FamilyPlacementHandler.Initialize();
                UploadWorkflowHandler.Initialize();

                return Result.Succeeded;
            }
            catch (Exception)
            {
                return Result.Failed;
            }
        }

        public Result OnShutdown(UIControlledApplication application)
        {
            return Result.Succeeded;
        }
    }
}
