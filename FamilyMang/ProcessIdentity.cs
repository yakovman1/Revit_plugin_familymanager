using System;
using System.Diagnostics;
using System.Security.Principal;
using System.Text;

namespace FamilyMang
{
    /// <summary>
    /// Диагностика учётной записи процесса Revit — для настройки прав на сетевом хранилище.
    /// </summary>
    internal static class ProcessIdentity
    {
        public static string GetDiagnosticSummary()
        {
            var sb = new StringBuilder();
            sb.AppendLine("Учётная запись, от имени которой Revit выполняет запись на диск:");

            try
            {
                using (var identity = WindowsIdentity.GetCurrent())
                {
                    sb.AppendLine($"  WindowsIdentity: {identity.Name}");
                }
            }
            catch (Exception ex)
            {
                sb.AppendLine($"  WindowsIdentity: недоступно ({ex.Message})");
            }

            sb.AppendLine($"  Environment.UserDomainName: {Environment.UserDomainName}");
            sb.AppendLine($"  Environment.UserName: {Environment.UserName}");
            sb.AppendLine($"  Environment.MachineName: {Environment.MachineName}");

            try
            {
                var proc = Process.GetCurrentProcess();
                sb.AppendLine($"  Процесс: {proc.ProcessName}.exe (PID {proc.Id}, сеанс {proc.SessionId})");
            }
            catch
            {
                // ignore
            }

            try
            {
                using (var identity = WindowsIdentity.GetCurrent())
                {
                    var principal = new WindowsPrincipal(identity);
                    bool elevated = principal.IsInRole(WindowsBuiltInRole.Administrator);
                    sb.AppendLine($"  Запуск от администратора: {(elevated ? "да" : "нет")}");
                }
            }
            catch
            {
                // ignore
            }

            sb.AppendLine();
            sb.AppendLine("Права на папку хранилища нужно выдать именно этой учётной записи (DOMAIN\\User).");
            return sb.ToString().TrimEnd();
        }
    }
}
