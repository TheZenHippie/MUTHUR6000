using System;
using System.IO;
using System.Text.Json;

namespace MUTHUR6000.Settings
{
    public class WidgetSettings
    {
        public int BaudRate { get; set; } = 1200;
        public string PhosphorColorHex { get; set; } = "#00FF66";
        public string BackgroundColorHex { get; set; } = "#08140B";
        public double BackgroundOpacity { get; set; } = 0.85;
        public double FontOpacity { get; set; } = 1.0;
        public string FontFamily { get; set; } = "Cascadia Code, Consolas, Courier New";
        public double FontSize { get; set; } = 13.0;

        public bool BloomEnabled { get; set; } = true;
        public double BloomIntensity { get; set; } = 8.0;

        public bool CrtScanlinesEnabled { get; set; } = false;
        public double ScanlineThickness { get; set; } = 3.0;

        public bool CrtGlitchEnabled { get; set; } = true;
        public double GlitchChance { get; set; } = 15.0;

        public bool CrtSnowEnabled { get; set; } = false;
        public double SnowAmount { get; set; } = 25.0;

        public double HoldDelaySeconds { get; set; } = 3.0;

        public bool AlwaysOnTop { get; set; } = false;
        public bool WindowShadow { get; set; } = true;

        public double? WindowWidth { get; set; } = 780;
        public double? WindowHeight { get; set; } = 520;
        public double? WindowLeft { get; set; }
        public double? WindowTop { get; set; }

        private static readonly object FileLock = new object();

        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true
        };

        private static string GetSettingsFilePath()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string folder = Path.Combine(appData, "MUTHUR6000");
            if (!Directory.Exists(folder))
            {
                Directory.CreateDirectory(folder);
            }
            return Path.Combine(folder, "settings.json");
        }

        public static WidgetSettings Load()
        {
            lock (FileLock)
            {
                try
                {
                    string path = GetSettingsFilePath();
                    if (File.Exists(path))
                    {
                        string json = File.ReadAllText(path);
                        if (!string.IsNullOrWhiteSpace(json))
                        {
                            var settings = JsonSerializer.Deserialize<WidgetSettings>(json, JsonOptions);
                            if (settings != null)
                            {
                                return settings;
                            }
                        }
                    }
                }
                catch { }

                return new WidgetSettings();
            }
        }

        public void Save()
        {
            lock (FileLock)
            {
                try
                {
                    string path = GetSettingsFilePath();
                    string tempPath = path + ".tmp";
                    string json = JsonSerializer.Serialize(this, JsonOptions);
                    File.WriteAllText(tempPath, json);
                    File.Move(tempPath, path, overwrite: true);
                }
                catch { }
            }
        }
    }
}

