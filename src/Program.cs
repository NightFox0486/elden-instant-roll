using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;

[assembly: AssemblyTitle("콩벌레 (EldenInstantRoll)")]
[assembly: AssemblyDescription("콩벌레 (EldenInstantRoll) - 엘든링 · 밤의 통치자 키 누름 즉시 구르기")]
[assembly: AssemblyProduct("EldenInstantRoll")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]

namespace EldenInstantRoll
{
    internal static class Program
    {
        private const string SingleInstanceMutexName = @"Local\EldenInstantRoll_SingleInstance_Mutex_v1";
        private const string ActivateMessageName = "EldenInstantRoll_ActivateExisting_v1";

        [STAThread]
        private static void Main(string[] args)
        {
            NativeMethods.EnableDpiAwareness();

            uint activateMsgId = NativeMethods.RegisterWindowMessage(ActivateMessageName);

            bool createdNew;
            using (Mutex instanceMutex = new Mutex(true, SingleInstanceMutexName, out createdNew))
            {
                if (!createdNew)
                {
                    // Another instance is already running -> bring its window to foreground
                    if (activateMsgId != 0)
                    {
                        NativeMethods.PostMessage(NativeMethods.HWND_BROADCAST, activateMsgId, IntPtr.Zero, IntPtr.Zero);
                    }
                    return;
                }

                bool startMinimized = HasArg(args, "--minimized") || HasArg(args, "-minimized") || HasArg(args, "/minimized");

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);

                string exePath = Application.ExecutablePath;
                string exeDir = Path.GetDirectoryName(exePath);
                string appDataDir = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                string settingsPath = AppSettings.ResolveSettingsPath(exeDir, appDataDir);

                AppSettings settings = AppSettings.LoadFromFile(settingsPath);
                if (!File.Exists(settingsPath))
                {
                    // Persist initial defaults cleanly so user can see/edit portable INI if desired
                    settings.SaveToFile(settingsPath);
                }

                StartupRegistryManager startupManager = new StartupRegistryManager();
                AppController controller = new AppController(settings, startupManager, settingsPath, exePath);

                using (InputHookEngine hookEngine = new InputHookEngine(controller))
                {
                    hookEngine.Start();
                    using (MainForm mainForm = new MainForm(controller, hookEngine, activateMsgId, startMinimized))
                    {
                        Application.Run(mainForm);
                    }
                }
            }
        }

        private static bool HasArg(string[] args, string flag)
        {
            if (args == null)
            {
                return false;
            }
            for (int i = 0; i < args.Length; i++)
            {
                if (string.Equals(args[i], flag, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }
    }
}
