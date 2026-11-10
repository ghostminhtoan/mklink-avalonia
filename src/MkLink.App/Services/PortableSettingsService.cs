using System;
using System.IO;

namespace MkLink.Services
{
    public static class PortableSettingsService
    {
        private static string GetSettingsDirectory()
        {
            string baseDir = AppContext.BaseDirectory;
            string portableSettings = Path.Combine(baseDir, ".portable", "settings");

            try
            {
                if (!Directory.Exists(portableSettings))
                {
                    Directory.CreateDirectory(portableSettings);
                }
                return portableSettings;
            }
            catch
            {
                // Fallback nếu không có quyền ghi tại portable folder
                string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                string fallbackDir = Path.Combine(appData, "MkLink-GMTPC");
                if (!Directory.Exists(fallbackDir))
                {
                    Directory.CreateDirectory(fallbackDir);
                }
                return fallbackDir;
            }
        }

        public static string GetLastSavedMdFolder()
        {
            try
            {
                string settingsDir = GetSettingsDirectory();
                string configPath = Path.Combine(settingsDir, "last_md_folder.txt");
                if (File.Exists(configPath))
                {
                    string path = File.ReadAllText(configPath).Trim();
                    if (Directory.Exists(path))
                    {
                        return path;
                    }
                }
            }
            catch
            {
            }

            return string.Empty;
        }

        public static void SaveLastSavedMdFolder(string fileOrFolderPath)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(fileOrFolderPath))
                {
                    return;
                }

                string folder = Directory.Exists(fileOrFolderPath)
                    ? fileOrFolderPath
                    : Path.GetDirectoryName(fileOrFolderPath) ?? string.Empty;

                if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
                {
                    return;
                }

                string settingsDir = GetSettingsDirectory();
                string configPath = Path.Combine(settingsDir, "last_md_folder.txt");
                File.WriteAllText(configPath, folder);
            }
            catch
            {
            }
        }
    }
}
