using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace FamilyMang
{
    /// <summary>
    /// Отложенный запуск режима расстановки через ExternalEvent.
    /// </summary>
    public sealed class FamilyPlacementHandler : IExternalEventHandler
    {
        private static FamilyPlacementHandler _handler;
        private static ExternalEvent _externalEvent;

        private ElementId _symbolId = ElementId.InvalidElementId;

        public static void Initialize()
        {
            if (_externalEvent != null)
                return;

            _handler = new FamilyPlacementHandler();
            _externalEvent = ExternalEvent.Create(_handler);
        }

        public static void Schedule(FamilySymbol symbol)
        {
            if (symbol == null)
                return;

            Initialize();
            _handler._symbolId = symbol.Id;
            _externalEvent.Raise();
        }

        public static void ActivateAndPlace(UIApplication app, Document doc, FamilySymbol symbol)
        {
            if (app == null || doc == null || symbol == null || !symbol.IsValidObject)
                return;

            if (doc.IsFamilyDocument || doc.IsReadOnly)
                return;

            if (!symbol.IsActive)
            {
                using (var tx = new Transaction(doc, "FamilyMang: активация типа"))
                {
                    tx.Start();
                    symbol.Activate();
                    tx.Commit();
                }
            }

            var uidoc = new UIDocument(doc);
            uidoc.PostRequestForElementTypePlacement(symbol);
        }

        public void Execute(UIApplication app)
        {
            var symbolId = _symbolId;
            _symbolId = ElementId.InvalidElementId;

            try
            {
                if (app == null || symbolId == null || symbolId == ElementId.InvalidElementId)
                    return;

                var doc = FindDocumentContainingElement(app, symbolId);
                if (doc == null || doc.IsFamilyDocument || doc.IsReadOnly)
                    return;

                var symbol = doc.GetElement(symbolId) as FamilySymbol;
                if (symbol == null || !symbol.IsValidObject)
                    return;

                ActivateAndPlace(app, doc, symbol);
            }
            catch
            {
                // Расстановка не критична — семейство уже загружено.
            }
        }

        private static Document FindDocumentContainingElement(UIApplication app, ElementId elementId)
        {
            var active = app.ActiveUIDocument?.Document;
            if (active != null && active.GetElement(elementId) != null)
                return active;

            return app.Application.Documents
                .Cast<Document>()
                .FirstOrDefault(d => d.GetElement(elementId) != null);
        }

        public string GetName() => "FamilyMang — размещение семейства";
    }
}
