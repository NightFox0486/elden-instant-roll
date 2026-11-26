using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace EldenInstantRoll
{
    public sealed class AppSettings
    {
        public const int MinTapDelayMs = 15;
        public const int MaxTapDelayMs = 60;
        public const int DefaultTapDelayMs = 25;

        public const string NightreignProcessName = "nightreign.exe";
        public const string EldenRingProcessName = "eldenring.exe";

        public bool MasterEnabled { get; set; }
        public bool AutoEnableOnGame { get; set; }
        public bool AutoStartWithWindows { get; set; }

        public KeyBinding RollKey { get; set; }
        public KeyBinding ToggleHotkey { get; set; }
        public int TapDelayMs { get; set; }

        public bool TargetNightreign { get; set; }
        public bool TargetEldenRing { get; set; }
        public string CustomProcesses { get; set; }

        public bool MinimizeToTrayOnClose { get; set; }

        public AppSettings()
        {
            MasterEnabled = true;
            AutoEnableOnGame = true;
            AutoStartWithWindows = false;

            RollKey = KeyBinding.DefaultRollKey();
            ToggleHotkey = KeyBinding.DefaultToggleHotkey();
            TapDelayMs = DefaultTapDelayMs;

            TargetNightreign = true;
            TargetEldenRing = true;
            CustomProcesses = string.Empty;

            MinimizeToTrayOnClose = true;
        }

        public static int ClampTapDelay(int ms)
        {
            if (ms < MinTapDelayMs)
            {
                return MinTapDelayMs;
            }
            if (ms > MaxTapDelayMs)
            {
                return MaxTapDelayMs;
            }
            return ms;
        }

        public HashSet<string> GetTargetProcessSet()
        {
            HashSet<string> set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (TargetNightreign)
            {
                set.Add(NightreignProcessName);
            }
            if (TargetEldenRing)
            {
                set.Add(EldenRingProcessName);
            }
            if (!string.IsNullOrEmpty(CustomProcesses))
            {
                string[] items = CustomProcesses.Split(new char[] { ',', ';', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                for (int i = 0; i < items.Length; i++)
                {
                    string proc = items[i].Trim();
                    if (proc.Length == 0)
                    {
                        continue;
                    }
                    if (!proc.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    {
                        proc = proc + ".exe";
                    }
                    set.Add(proc);
                }
            }
            return set;
        }

        public static string ResolveSettingsPath(string exeDirectory, string appDataDirectory)
        {
            string fileName = "EldenInstantRoll.ini";
            string portablePath = Path.Combine(exeDirectory, fileName);

            if (File.Exists(portablePath))
            {
                if (IsDirectoryWritable(exeDirectory))
                {
                    return portablePath;
                }
            }
            else if (IsDirectoryWritable(exeDirectory))
            {
                return portablePath;
            }

            string fallbackDir = Path.Combine(appDataDirectory, "EldenInstantRoll");
            return Path.Combine(fallbackDir, fileName);
        }

        public static bool IsDirectoryWritable(string directoryPath)
        {
            if (string.IsNullOrEmpty(directoryPath))
            {
                return false;
            }

            try
            {
                if (!Directory.Exists(directoryPath))
                {
                    Directory.CreateDirectory(directoryPath);
                }

                string probe = Path.Combine(directoryPath, ".eir_write_test_" + Guid.NewGuid().ToString("N") + ".tmp");
                using (FileStream fs = File.Create(probe, 1, FileOptions.DeleteOnClose))
                {
                    // Successfully created and auto-deleted
                }
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static AppSettings LoadFromFile(string path)
        {
            AppSettings settings = new AppSettings();
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                return settings;
            }

            try
            {
                string[] lines = File.ReadAllLines(path, Encoding.UTF8);
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i].Trim();
                    if (line.Length == 0 || line.StartsWith(";") || line.StartsWith("#") || line.StartsWith("["))
                    {
                        continue;
                    }

                    int eq = line.IndexOf('=');
                    if (eq <= 0)
                    {
                        continue;
                    }

                    string key = line.Substring(0, eq).Trim();
                    string val = line.Substring(eq + 1).Trim();

                    if (key.Equals("MasterEnabled", StringComparison.OrdinalIgnoreCase))
                    {
                        settings.MasterEnabled = ParseBool(val, settings.MasterEnabled);
                    }
                    else if (key.Equals("AutoEnableOnGame", StringComparison.OrdinalIgnoreCase))
                    {
                        settings.AutoEnableOnGame = ParseBool(val, settings.AutoEnableOnGame);
                    }
                    else if (key.Equals("AutoStartWithWindows", StringComparison.OrdinalIgnoreCase))
                    {
                        settings.AutoStartWithWindows = ParseBool(val, settings.AutoStartWithWindows);
                    }
                    else if (key.Equals("RollKey", StringComparison.OrdinalIgnoreCase))
                    {
                        KeyBinding parsed;
                        if (KeyBinding.TryParse(val, out parsed))
                        {
                            settings.RollKey = parsed;
                        }
                    }
                    else if (key.Equals("ToggleHotkey", StringComparison.OrdinalIgnoreCase))
                    {
                        KeyBinding parsed;
                        if (KeyBinding.TryParse(val, out parsed) && parsed.Kind == BindingKind.Keyboard)
                        {
                            settings.ToggleHotkey = parsed;
                        }
                    }
                    else if (key.Equals("TapDelayMs", StringComparison.OrdinalIgnoreCase))
                    {
                        int parsedDelay;
                        if (int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsedDelay))
                        {
                            settings.TapDelayMs = ClampTapDelay(parsedDelay);
                        }
                    }
                    else if (key.Equals("TargetNightreign", StringComparison.OrdinalIgnoreCase))
                    {
                        settings.TargetNightreign = ParseBool(val, settings.TargetNightreign);
                    }
                    else if (key.Equals("TargetEldenRing", StringComparison.OrdinalIgnoreCase))
                    {
                        settings.TargetEldenRing = ParseBool(val, settings.TargetEldenRing);
                    }
                    else if (key.Equals("CustomProcesses", StringComparison.OrdinalIgnoreCase))
                    {
                        settings.CustomProcesses = val;
                    }
                    else if (key.Equals("MinimizeToTrayOnClose", StringComparison.OrdinalIgnoreCase))
                    {
                        settings.MinimizeToTrayOnClose = ParseBool(val, settings.MinimizeToTrayOnClose);
                    }
                }
            }
            catch
            {
                // Return defaults on corrupted file
            }

            // Prevent RollKey and ToggleHotkey conflict on load
            if (settings.RollKey != null && settings.RollKey.Equals(settings.ToggleHotkey))
            {
                settings.ToggleHotkey = KeyBinding.DefaultToggleHotkey();
                if (settings.RollKey.Equals(settings.ToggleHotkey))
                {
                    settings.RollKey = KeyBinding.DefaultRollKey();
                }
            }

            // Enforce Master priority rule even if INI was manually edited
            if (!settings.MasterEnabled)
            {
                settings.AutoEnableOnGame = false;
                settings.AutoStartWithWindows = false;
            }

            return settings;
        }

        public bool SaveToFile(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return false;
            }

            try
            {
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                StringBuilder sb = new StringBuilder();
                sb.AppendLine("; EldenInstantRoll Portable Configuration");
                sb.AppendLine("[General]");
                sb.AppendLine("MasterEnabled=" + (MasterEnabled ? "true" : "false"));
                sb.AppendLine("AutoEnableOnGame=" + (AutoEnableOnGame ? "true" : "false"));
                sb.AppendLine("AutoStartWithWindows=" + (AutoStartWithWindows ? "true" : "false"));
                sb.AppendLine("MinimizeToTrayOnClose=" + (MinimizeToTrayOnClose ? "true" : "false"));
                sb.AppendLine();
                sb.AppendLine("[Input]");
                sb.AppendLine("RollKey=" + (RollKey != null ? RollKey.Serialize() : KeyBinding.DefaultRollKey().Serialize()));
                sb.AppendLine("ToggleHotkey=" + (ToggleHotkey != null ? ToggleHotkey.Serialize() : KeyBinding.DefaultToggleHotkey().Serialize()));
                sb.AppendLine("TapDelayMs=" + ClampTapDelay(TapDelayMs).ToString(CultureInfo.InvariantCulture));
                sb.AppendLine();
                sb.AppendLine("[Targets]");
                sb.AppendLine("TargetNightreign=" + (TargetNightreign ? "true" : "false"));
                sb.AppendLine("TargetEldenRing=" + (TargetEldenRing ? "true" : "false"));
                sb.AppendLine("CustomProcesses=" + (CustomProcesses ?? string.Empty).Trim());

                File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool ParseBool(string value, bool fallback)
        {
            if (string.IsNullOrEmpty(value))
            {
                return fallback;
            }
            if (value.Equals("1", StringComparison.OrdinalIgnoreCase) ||
                value.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                value.Equals("yes", StringComparison.OrdinalIgnoreCase) ||
                value.Equals("on", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            if (value.Equals("0", StringComparison.OrdinalIgnoreCase) ||
                value.Equals("false", StringComparison.OrdinalIgnoreCase) ||
                value.Equals("no", StringComparison.OrdinalIgnoreCase) ||
                value.Equals("off", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
            return fallback;
        }
    }
}
