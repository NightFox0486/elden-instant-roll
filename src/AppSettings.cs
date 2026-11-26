using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace EldenInstantRoll
{
    public sealed class AppSettings
    {
        public const int CurrentConfigVersion = 2;
        public const int MinTapDelayMs = 15;
        public const int MaxTapDelayMs = 60;
        public const int DefaultTapDelayMs = 25;

        public const string NightreignProcessName = "nightreign.exe";
        public const string EldenRingProcessName = "eldenring.exe";

        public int ConfigVersion { get; set; }
        public bool MigratedFromV1 { get; private set; }

        public bool MasterEnabled { get; set; }
        public bool AutoEnableOnGame { get; set; }
        public bool AutoStartWithWindows { get; set; }

        public KeyBinding ToggleHotkey { get; set; }
        public int TapDelayMs { get; set; }

        public List<ActionBindingItem> Actions { get; set; }

        public bool TargetNightreign { get; set; }
        public bool TargetEldenRing { get; set; }
        public string CustomProcesses { get; set; }

        public bool MinimizeToTrayOnClose { get; set; }

        public AppSettings()
        {
            ConfigVersion = CurrentConfigVersion;
            MasterEnabled = true;
            AutoEnableOnGame = true;
            AutoStartWithWindows = false;

            ToggleHotkey = KeyBinding.DefaultToggleHotkey();
            TapDelayMs = DefaultTapDelayMs;

            Actions = ActionBindingItem.CreateDefaultNightreignActions();

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

        public ActionBindingItem FindAction(string id)
        {
            if (Actions == null || string.IsNullOrEmpty(id))
            {
                return null;
            }
            for (int i = 0; i < Actions.Count; i++)
            {
                if (string.Equals(Actions[i].Id, id, StringComparison.OrdinalIgnoreCase))
                {
                    return Actions[i];
                }
            }
            return null;
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

            bool hasV2Actions = false;
            KeyBinding legacyRollKey = null;

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

                    if (key.Equals("ConfigVersion", StringComparison.OrdinalIgnoreCase))
                    {
                        int ver;
                        if (int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out ver))
                        {
                            settings.ConfigVersion = ver;
                        }
                    }
                    else if (key.Equals("MasterEnabled", StringComparison.OrdinalIgnoreCase))
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
                            legacyRollKey = parsed;
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
                    else if (key.StartsWith("Action.", StringComparison.OrdinalIgnoreCase))
                    {
                        hasV2Actions = true;
                        string actionId = key.Substring(7).Trim();
                        ActionBindingItem item = settings.FindAction(actionId);
                        if (item != null)
                        {
                            ApplyActionValue(item, val);
                        }
                    }
                    else if (key.StartsWith("Custom.", StringComparison.OrdinalIgnoreCase))
                    {
                        hasV2Actions = true;
                        string customId = key.Substring(7).Trim();
                        ActionBindingItem custom = ParseCustomAction(customId, val);
                        if (custom != null && settings.FindAction(custom.Id) == null)
                        {
                            settings.Actions.Add(custom);
                        }
                    }
                }
            }
            catch
            {
                // Return defaults on corrupted file
            }

            // Automatic v1.0.0 -> v1.1.0 migration
            if (!hasV2Actions && legacyRollKey != null)
            {
                ActionBindingItem dodge = settings.FindAction(ActionBindingItem.DodgeActionId);
                if (dodge != null)
                {
                    dodge.Binding = legacyRollKey;
                    dodge.Enabled = true;
                }
                settings.ConfigVersion = CurrentConfigVersion;
                settings.MigratedFromV1 = true;
            }

            SanitizeActiveBindings(settings);

            // Enforce Master priority rule even if INI was manually edited
            if (!settings.MasterEnabled)
            {
                settings.AutoEnableOnGame = false;
                settings.AutoStartWithWindows = false;
            }

            return settings;
        }

        private static void ApplyActionValue(ActionBindingItem item, string rawValue)
        {
            // Format: <1|0>|<serializedKey or NONE>
            // Note: serialized keyboard keys contain a pipe like KEY:0010:0|Q, so split ONLY on first pipe.
            int firstPipe = rawValue.IndexOf('|');
            if (firstPipe <= 0)
            {
                return;
            }

            string enabledPart = rawValue.Substring(0, firstPipe).Trim();
            string keyPart = rawValue.Substring(firstPipe + 1).Trim();

            bool enabled = ParseBool(enabledPart, false);
            if (keyPart.Equals("NONE", StringComparison.OrdinalIgnoreCase) || keyPart.Length == 0)
            {
                item.Binding = null;
                item.Enabled = false;
            }
            else
            {
                KeyBinding parsed;
                if (KeyBinding.TryParse(keyPart, out parsed))
                {
                    item.Binding = parsed;
                    item.Enabled = enabled;
                }
            }
        }

        private static ActionBindingItem ParseCustomAction(string customId, string rawValue)
        {
            // Format: <1|0>|<nameEscaped>;<serializedKey or NONE>
            if (string.IsNullOrEmpty(customId) || string.IsNullOrEmpty(rawValue))
            {
                return null;
            }

            int firstPipe = rawValue.IndexOf('|');
            if (firstPipe <= 0)
            {
                return null;
            }

            bool enabled = ParseBool(rawValue.Substring(0, firstPipe).Trim(), false);
            string rest = rawValue.Substring(firstPipe + 1);
            int semi = rest.IndexOf(';');
            if (semi < 0)
            {
                return null;
            }

            string name = UnescapeField(rest.Substring(0, semi).Trim());
            if (string.IsNullOrEmpty(name))
            {
                name = "사용자 키";
            }

            string keyPart = rest.Substring(semi + 1).Trim();
            KeyBinding binding = null;
            if (!keyPart.Equals("NONE", StringComparison.OrdinalIgnoreCase) && keyPart.Length > 0)
            {
                KeyBinding parsed;
                if (KeyBinding.TryParse(keyPart, out parsed))
                {
                    binding = parsed;
                }
            }

            return new ActionBindingItem(customId, ActionBindingItem.CategoryCustom, name, binding != null && enabled, binding, true);
        }

        private static void SanitizeActiveBindings(AppSettings settings)
        {
            if (settings.ToggleHotkey == null || settings.ToggleHotkey.Kind != BindingKind.Keyboard)
            {
                settings.ToggleHotkey = KeyBinding.DefaultToggleHotkey();
            }

            HashSet<KeyBinding> seenActive = new HashSet<KeyBinding>();
            for (int i = 0; i < settings.Actions.Count; i++)
            {
                ActionBindingItem item = settings.Actions[i];
                if (!item.Enabled)
                {
                    continue;
                }
                if (item.Binding == null)
                {
                    item.Enabled = false;
                    continue;
                }
                if (item.Binding.Equals(settings.ToggleHotkey))
                {
                    item.Enabled = false;
                    continue;
                }
                if (!seenActive.Add(item.Binding))
                {
                    item.Enabled = false;
                }
            }
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
                sb.AppendLine("; EldenInstantRoll (v1.1.0) Portable Configuration");
                sb.AppendLine("[General]");
                sb.AppendLine("ConfigVersion=" + CurrentConfigVersion.ToString(CultureInfo.InvariantCulture));
                sb.AppendLine("MasterEnabled=" + (MasterEnabled ? "true" : "false"));
                sb.AppendLine("AutoEnableOnGame=" + (AutoEnableOnGame ? "true" : "false"));
                sb.AppendLine("AutoStartWithWindows=" + (AutoStartWithWindows ? "true" : "false"));
                sb.AppendLine("MinimizeToTrayOnClose=" + (MinimizeToTrayOnClose ? "true" : "false"));
                sb.AppendLine();
                sb.AppendLine("[Input]");
                sb.AppendLine("ToggleHotkey=" + (ToggleHotkey != null ? ToggleHotkey.Serialize() : KeyBinding.DefaultToggleHotkey().Serialize()));
                sb.AppendLine("TapDelayMs=" + ClampTapDelay(TapDelayMs).ToString(CultureInfo.InvariantCulture));
                sb.AppendLine();
                sb.AppendLine("[Actions]");
                if (Actions != null)
                {
                    for (int i = 0; i < Actions.Count; i++)
                    {
                        ActionBindingItem item = Actions[i];
                        string keyStr = item.Binding != null ? item.Binding.Serialize() : "NONE";
                        string enabledStr = (item.Enabled && item.Binding != null) ? "1" : "0";
                        if (item.IsCustom)
                        {
                            sb.AppendLine("Custom." + item.Id + "=" + enabledStr + "|" + EscapeField(item.Name) + ";" + keyStr);
                        }
                        else
                        {
                            sb.AppendLine("Action." + item.Id + "=" + enabledStr + "|" + keyStr);
                        }
                    }
                }
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

        private static string EscapeField(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }
            return text.Replace("%", "%25").Replace(";", "%3B").Replace("|", "%7C").Replace("\r", " ").Replace("\n", " ");
        }

        private static string UnescapeField(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }
            return text.Replace("%7C", "|").Replace("%3B", ";").Replace("%25", "%");
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
