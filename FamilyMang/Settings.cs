using System;
using System.IO;
using System.Web.Script.Serialization;

namespace FamilyMang
{
    public class PluginSettings
    {
        public string ServerUrl { get; set; } = "http://localhost:8000";

        private static readonly string SettingsDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FamilyMang");

        private static readonly string SettingsFile = Path.Combine(SettingsDir, "settings.json");

        public static PluginSettings Load()
        {
            try
            {
                if (File.Exists(SettingsFile))
                {
                    var json = File.ReadAllText(SettingsFile);
                    var result = new JavaScriptSerializer().Deserialize<PluginSettings>(json);
                    if (result != null) return result;
                }
            }
            catch { }
            return new PluginSettings();
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(SettingsDir);
                var json = new JavaScriptSerializer().Serialize(this);
                File.WriteAllText(SettingsFile, json);
            }
            catch { }
        }
    }
}
