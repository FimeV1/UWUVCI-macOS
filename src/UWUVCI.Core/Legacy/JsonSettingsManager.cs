using System;
using System.IO;
using Newtonsoft.Json;
using UWUVCI.Core;
using UWUVCI_AIO_WPF.Models;

namespace UWUVCI_AIO_WPF.Helpers
{
    public class JsonSettingsManager
    {
        public static JsonAppSettings Settings { get; private set; } = new JsonAppSettings();

        public static void LoadSettings()
        {
            try
            {
                if (File.Exists(AppPaths.SettingsFile))
                    Settings = JsonConvert.DeserializeObject<JsonAppSettings>(File.ReadAllText(AppPaths.SettingsFile)) ?? new JsonAppSettings();
            }
            catch (Exception ex)
            {
                Logger.Log($"Error loading settings: {ex.Message}. Reverting to default settings.");
                Settings = new JsonAppSettings();
            }

            // Unlike the Windows app there is no "choose your folders" first-run step: start with working defaults.
            if (string.IsNullOrWhiteSpace(Settings.BasePath))
                Settings.BasePath = AppPaths.DefaultBasePath;
            if (string.IsNullOrWhiteSpace(Settings.OutPath))
                Settings.OutPath = AppPaths.DefaultOutPath;
            Settings.PathsSet = true;
            SaveSettings();
        }

        public static void SaveSettings()
        {
            try
            {
                Directory.CreateDirectory(AppPaths.DataRoot);
                File.WriteAllText(AppPaths.SettingsFile, JsonConvert.SerializeObject(Settings, Formatting.Indented));
            }
            catch (Exception ex)
            {
                Logger.Log($"Error saving settings: {ex.Message}");
            }
        }
    }
}
