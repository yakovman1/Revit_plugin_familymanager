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
            PluginWindows.ShowCatalog();
            return Result.Succeeded;
        }
    }
}
