using System;
using Microsoft.Win32;

namespace EldenInstantRoll
{
    public interface IStartupManager
    {
        bool IsRegistered();
        bool Register(string exePath);
        bool Unregister();
    }

    public sealed class StartupRegistryManager : IStartupManager
    {
        public const string RunRegistrySubKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        public const string DefaultValueName = "EldenInstantRoll";

        private readonly string _valueName;

        public StartupRegistryManager()
            : this(DefaultValueName)
        {
        }

        public StartupRegistryManager(string valueName)
        {
            _valueName = string.IsNullOrEmpty(valueName) ? DefaultValueName : valueName;
        }

        public string ValueName
        {
            get { return _valueName; }
        }

        public static string BuildStartupCommand(string exePath)
        {
            string cleanPath = (exePath ?? string.Empty).Trim().Trim('"');
            return "\"" + cleanPath + "\" --minimized";
        }

        public bool IsRegistered()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunRegistrySubKey, false))
                {
                    if (key == null)
                    {
                        return false;
                    }
                    object value = key.GetValue(_valueName, null);
                    return value != null && !string.IsNullOrEmpty(value.ToString());
                }
            }
            catch
            {
                return false;
            }
        }

        public bool Register(string exePath)
        {
            if (string.IsNullOrEmpty(exePath))
            {
                return false;
            }

            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunRegistrySubKey, true)
                    ?? Registry.CurrentUser.CreateSubKey(RunRegistrySubKey))
                {
                    if (key == null)
                    {
                        return false;
                    }
                    key.SetValue(_valueName, BuildStartupCommand(exePath), RegistryValueKind.String);
                    return true;
                }
            }
            catch
            {
                return false;
            }
        }

        public bool Unregister()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunRegistrySubKey, true))
                {
                    if (key == null)
                    {
                        return true;
                    }
                    if (key.GetValue(_valueName, null) != null)
                    {
                        key.DeleteValue(_valueName, false);
                    }
                    return true;
                }
            }
            catch
            {
                return false;
            }
        }
    }
}
