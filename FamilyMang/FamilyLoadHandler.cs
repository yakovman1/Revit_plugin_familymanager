using System;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace FamilyMang
{
    /// <summary>
    /// Загрузка .rfa в активный проект Revit через ExternalEvent (без модального окна).
    /// </summary>
    public sealed class FamilyLoadHandler : IExternalEventHandler
    {
        private static FamilyLoadHandler _handler;
        private static ExternalEvent _externalEvent;

        private string _rfaPath;
        private Action<bool, string> _callback;

        public static void Initialize()
        {
            if (_externalEvent != null)
                return;

            _handler = new FamilyLoadHandler();
            _externalEvent = ExternalEvent.Create(_handler);
        }

        public static void Schedule(string rfaPath, Action<bool, string> callback = null)
        {
            if (string.IsNullOrWhiteSpace(rfaPath))
                return;

            Initialize();
            _handler._rfaPath = rfaPath;
            _handler._callback = callback;
            _externalEvent.Raise();
        }

        public void Execute(UIApplication app)
        {
            var path = _rfaPath;
            var callback = _callback;
            _rfaPath = null;
            _callback = null;

            try
            {
                var doc = app.ActiveUIDocument?.Document;
                if (doc == null)
                {
                    callback?.Invoke(false, "Нет открытого документа Revit.");
                    return;
                }

                if (doc.IsFamilyDocument)
                {
                    callback?.Invoke(false, "Откройте проект Revit, а не редактор семейства.");
                    return;
                }

                if (!FamilyLoader.LoadIntoDocument(doc, path, out Family family) || family == null)
                {
                    callback?.Invoke(false,
                        "Не удалось загрузить семейство.");
                    return;
                }

                var symbol = FamilyLoader.GetDefaultSymbol(doc, family);
                if (symbol != null)
                    FamilyPlacementHandler.ActivateAndPlace(app, doc, symbol);

                var name = family?.Name ?? "Семейство";
                callback?.Invoke(true, $"«{name}» загружено и активировано для расстановки.");
            }
            catch (Exception ex)
            {
                callback?.Invoke(false, ex.Message);
            }
        }

        public string GetName() => "FamilyMang — загрузка семейства в проект";
    }
}
