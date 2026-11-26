using System;
using System.IO;
using System.Runtime.InteropServices;

namespace EldenInstantRoll.Tests
{
    internal sealed class MockStartupManager : IStartupManager
    {
        public bool Registered { get; private set; }
        public string LastCommand { get; private set; }
        public int RegisterCalls { get; private set; }
        public int UnregisterCalls { get; private set; }

        public bool IsRegistered()
        {
            return Registered;
        }

        public bool Register(string exePath)
        {
            RegisterCalls++;
            Registered = true;
            LastCommand = StartupRegistryManager.BuildStartupCommand(exePath);
            return true;
        }

        public bool Unregister()
        {
            UnregisterCalls++;
            Registered = false;
            LastCommand = null;
            return true;
        }
    }

    internal static class Program
    {
        private static int _passed;
        private static int _failed;

        private static int Main()
        {
            RunTest("Win32 INPUT struct size matches native ABI", TestInputStructSize);
            RunTest("Default settings match specification", TestDefaults);
            RunTest("Master OFF forces child toggles OFF, disabled, and unregisters startup", TestMasterOffPriorityRule);
            RunTest("Master ON re-enables child toggles independently", TestMasterOnEnablesChildToggles);
            RunTest("Tampered INI with Master=false enforces priority invariant on startup", TestTamperedIniPriorityInvariant);
            RunTest("KeyBinding keyboard/mouse serialization & conflict prevention", TestKeyBindingsAndConflicts);
            RunTest("Target game filtering and AutoEnableOnGame manual mode", TestTargetGameFiltering);
            RunTest("Mouse hook only demanded when mouse roll key or capturing", TestMouseHookDemand);
            RunTest("Portable INI save/load round-trip and TapDelay clamping", TestIniRoundTrip);
            RunTest("Real Windows HKCU Run registry register and Master OFF immediate delete", TestRealWindowsRegistry);

            Console.WriteLine();
            Console.WriteLine("Tests completed: {0} passed, {1} failed.", _passed, _failed);
            return _failed == 0 ? 0 : 1;
        }

        private static void RunTest(string name, Action test)
        {
            try
            {
                test();
                _passed++;
                Console.WriteLine("[PASS] " + name);
            }
            catch (Exception ex)
            {
                _failed++;
                Console.WriteLine("[FAIL] " + name + " -> " + ex.Message);
            }
        }

        private static void Assert(bool condition, string message)
        {
            if (!condition)
            {
                throw new Exception(message);
            }
        }

        private static void TestInputStructSize()
        {
            int actual = Marshal.SizeOf(typeof(NativeMethods.INPUT));
            int expected = IntPtr.Size == 8 ? 40 : 28;
            Assert(actual == expected, "Expected INPUT size " + expected + ", got " + actual);
        }

        private static void TestDefaults()
        {
            AppSettings s = new AppSettings();
            Assert(s.MasterEnabled, "MasterEnabled should default true");
            Assert(s.AutoEnableOnGame, "AutoEnableOnGame should default true");
            Assert(!s.AutoStartWithWindows, "AutoStartWithWindows should default false");
            Assert(s.RollKey.Kind == BindingKind.Keyboard && s.RollKey.ScanCode == 0x10 && !s.RollKey.IsExtended, "RollKey should default to Q (sc010)");
            Assert(s.ToggleHotkey.Kind == BindingKind.Keyboard && s.ToggleHotkey.ScanCode == 0x42 && !s.ToggleHotkey.IsExtended, "ToggleHotkey should default to F8 (sc042)");
            Assert(s.TapDelayMs == 25, "TapDelayMs should default to 25");
            Assert(s.TargetNightreign && s.TargetEldenRing, "Both Elden Ring games should default checked");
        }

        private static void TestMasterOffPriorityRule()
        {
            string tmpFile = Path.Combine(Path.GetTempPath(), "eir_test_" + Guid.NewGuid().ToString("N") + ".ini");
            try
            {
                MockStartupManager startup = new MockStartupManager();
                AppSettings settings = new AppSettings();
                AppController ctrl = new AppController(settings, startup, tmpFile, @"C:\Tools\EldenInstantRoll.exe");

                Assert(ctrl.MasterEnabled && ctrl.ChildTogglesEnabled, "Master & child toggles should start enabled");
                Assert(ctrl.SetAutoEnableOnGame(true), "Should allow AutoEnableOnGame=true while Master ON");
                Assert(ctrl.SetAutoStartWithWindows(true), "Should allow AutoStartWithWindows=true while Master ON");
                Assert(ctrl.AutoEnableOnGame && ctrl.AutoStartWithWindows, "Both child toggles should be ON");
                Assert(startup.Registered, "Startup should be registered");
                Assert(startup.LastCommand == "\"C:\\Tools\\EldenInstantRoll.exe\" --minimized", "Unexpected startup command: " + startup.LastCommand);

                // Turn Master OFF -> MUST force both child toggles OFF, disable them, and delete startup registry!
                ctrl.SetMasterEnabled(false);

                Assert(!ctrl.MasterEnabled, "Master should be OFF");
                Assert(!ctrl.ChildTogglesEnabled, "Child toggles must be Disabled when Master is OFF");
                Assert(!ctrl.AutoEnableOnGame, "AutoEnableOnGame must automatically turn OFF");
                Assert(!ctrl.AutoStartWithWindows, "AutoStartWithWindows must automatically turn OFF");
                Assert(!startup.Registered, "Startup registry MUST be deleted immediately when Master turns OFF");

                // Attempt to toggle child switches while Master is OFF -> must be refused
                Assert(!ctrl.SetAutoEnableOnGame(true), "SetAutoEnableOnGame(true) must be refused while Master is OFF");
                Assert(!ctrl.SetAutoStartWithWindows(true), "SetAutoStartWithWindows(true) must be refused while Master is OFF");
                Assert(!ctrl.AutoEnableOnGame && !ctrl.AutoStartWithWindows && !startup.Registered, "Child toggles & registry must remain OFF");
            }
            finally
            {
                if (File.Exists(tmpFile)) File.Delete(tmpFile);
            }
        }

        private static void TestMasterOnEnablesChildToggles()
        {
            string tmpFile = Path.Combine(Path.GetTempPath(), "eir_test_" + Guid.NewGuid().ToString("N") + ".ini");
            try
            {
                MockStartupManager startup = new MockStartupManager();
                AppController ctrl = new AppController(new AppSettings(), startup, tmpFile, @"C:\Tools\EldenInstantRoll.exe");

                ctrl.SetMasterEnabled(false);
                Assert(!ctrl.ChildTogglesEnabled, "Child toggles disabled when Master OFF");

                ctrl.SetMasterEnabled(true);
                Assert(ctrl.MasterEnabled && ctrl.ChildTogglesEnabled, "Child toggles re-enabled when Master ON");
                Assert(!ctrl.AutoEnableOnGame && !ctrl.AutoStartWithWindows, "Child toggles remain OFF until user enables them");
                Assert(ctrl.ShouldInterceptRollForProcess("notepad.exe"), "With Master ON and AutoEnableOnGame OFF, manual mode should be active");

                Assert(ctrl.SetAutoEnableOnGame(true), "User can turn AutoEnableOnGame back ON");
                Assert(ctrl.AutoEnableOnGame, "AutoEnableOnGame should now be ON");
                Assert(!ctrl.ShouldInterceptRollForProcess("notepad.exe"), "With AutoEnableOnGame ON, notepad should wait");
                Assert(ctrl.ShouldInterceptRollForProcess("nightreign.exe"), "With AutoEnableOnGame ON, nightreign.exe should be active");

                Assert(ctrl.SetAutoStartWithWindows(true), "User can turn AutoStartWithWindows back ON");
                Assert(startup.Registered, "Registry should be registered");
                Assert(ctrl.SetAutoStartWithWindows(false), "User can turn AutoStartWithWindows OFF independently");
                Assert(!startup.Registered, "Registry should be removed when AutoStartWithWindows turned OFF");
            }
            finally
            {
                if (File.Exists(tmpFile)) File.Delete(tmpFile);
            }
        }

        private static void TestTamperedIniPriorityInvariant()
        {
            string tmpFile = Path.Combine(Path.GetTempPath(), "eir_test_" + Guid.NewGuid().ToString("N") + ".ini");
            try
            {
                File.WriteAllText(tmpFile,
                    "MasterEnabled=false\r\n" +
                    "AutoEnableOnGame=true\r\n" +
                    "AutoStartWithWindows=true\r\n");

                AppSettings loaded = AppSettings.LoadFromFile(tmpFile);
                Assert(!loaded.MasterEnabled, "MasterEnabled should load as false");
                Assert(!loaded.AutoEnableOnGame, "Loaded AutoEnableOnGame must be forced false when Master=false");
                Assert(!loaded.AutoStartWithWindows, "Loaded AutoStartWithWindows must be forced false when Master=false");

                MockStartupManager startup = new MockStartupManager();
                startup.Register(@"C:\Tools\EldenInstantRoll.exe");
                Assert(startup.Registered, "Pre-condition: stale registry entry exists");

                AppController ctrl = new AppController(loaded, startup, tmpFile, @"C:\Tools\EldenInstantRoll.exe");
                Assert(!ctrl.MasterEnabled && !ctrl.AutoEnableOnGame && !ctrl.AutoStartWithWindows, "Controller must enforce invariant");
                Assert(!startup.Registered, "Controller startup must clean stale registry entry when Master=false");
            }
            finally
            {
                if (File.Exists(tmpFile)) File.Delete(tmpFile);
            }
        }

        private static void TestKeyBindingsAndConflicts()
        {
            KeyBinding space = KeyBinding.FromKeyboard(0x39, false, "Space");
            KeyBinding parsed;
            Assert(KeyBinding.TryParse(space.Serialize(), out parsed) && parsed.Equals(space), "Keyboard round-trip failed");

            KeyBinding rightArrow = KeyBinding.FromKeyboard(0x4D, true, "Right");
            Assert(KeyBinding.TryParse(rightArrow.Serialize(), out parsed) && parsed.Equals(rightArrow) && parsed.IsExtended, "Extended key round-trip failed");

            KeyBinding mouse4 = KeyBinding.FromMouse(MouseBindButton.Mouse4);
            Assert(KeyBinding.TryParse(mouse4.Serialize(), out parsed) && parsed.Equals(mouse4) && parsed.Kind == BindingKind.Mouse, "Mouse4 round-trip failed");

            MockStartupManager startup = new MockStartupManager();
            AppController ctrl = new AppController(new AppSettings(), startup, null, @"C:\Tools\EldenInstantRoll.exe");

            string err;
            // Conflict: setting RollKey to F8 (current ToggleHotkey) must fail
            Assert(!ctrl.TrySetRollKey(KeyBinding.DefaultToggleHotkey(), out err), "RollKey matching ToggleHotkey must be rejected");
            Assert(!string.IsNullOrEmpty(err), "Conflict error message should be populated");

            // Conflict: setting ToggleHotkey to Q (current RollKey) must fail
            Assert(!ctrl.TrySetToggleHotkey(KeyBinding.DefaultRollKey(), out err), "ToggleHotkey matching RollKey must be rejected");

            // Mouse button cannot be ToggleHotkey
            Assert(!ctrl.TrySetToggleHotkey(mouse4, out err), "Mouse button must not be allowed as ToggleHotkey");

            // Valid changes
            Assert(ctrl.TrySetRollKey(mouse4, out err) && ctrl.RollKey.Equals(mouse4), "RollKey should update to Mouse4");
            Assert(ctrl.TrySetToggleHotkey(KeyBinding.FromKeyboard(0x43, false, "F9"), out err) && ctrl.ToggleHotkey.ScanCode == 0x43, "ToggleHotkey should update to F9");
        }

        private static void TestTargetGameFiltering()
        {
            MockStartupManager startup = new MockStartupManager();
            AppController ctrl = new AppController(new AppSettings(), startup, null, @"C:\Tools\EldenInstantRoll.exe");

            Assert(ctrl.ShouldInterceptRollForProcess("nightreign.exe"), "nightreign.exe should match");
            Assert(ctrl.ShouldInterceptRollForProcess("NIGHTREIGN.EXE"), "Case-insensitive match expected");
            Assert(ctrl.ShouldInterceptRollForProcess("eldenring.exe"), "eldenring.exe should match");
            Assert(!ctrl.ShouldInterceptRollForProcess("chrome.exe"), "chrome.exe should not match");
            Assert(ctrl.GetRunStateForProcess("chrome.exe") == EngineRunState.WaitingForGame, "Should be WaitingForGame for chrome.exe");
            Assert(ctrl.GetRunStateForProcess("nightreign.exe") == EngineRunState.Active, "Should be Active for nightreign.exe");

            // Uncheck Nightreign, add custom process without .exe suffix
            ctrl.SetTargets(false, true, "sekiro, DarkSoulsIII.exe");
            Assert(!ctrl.ShouldInterceptRollForProcess("nightreign.exe"), "Unchecked nightreign.exe should not match");
            Assert(ctrl.ShouldInterceptRollForProcess("eldenring.exe"), "Checked eldenring.exe should still match");
            Assert(ctrl.ShouldInterceptRollForProcess("sekiro.exe"), "Custom process without .exe should auto-append .exe");
            Assert(ctrl.ShouldInterceptRollForProcess("darksoulsiii.exe"), "Custom process should match case-insensitively");

            // Master OFF always returns Off / false
            ctrl.SetMasterEnabled(false);
            Assert(!ctrl.ShouldInterceptRollForProcess("eldenring.exe"), "Master OFF must not intercept");
            Assert(ctrl.GetRunStateForProcess("eldenring.exe") == EngineRunState.Off, "Master OFF must report Off state");
        }

        private static void TestMouseHookDemand()
        {
            MockStartupManager startup = new MockStartupManager();
            AppController ctrl = new AppController(new AppSettings(), startup, null, @"C:\Tools\EldenInstantRoll.exe");

            // Default Q keyboard key -> no mouse hook when not capturing
            Assert(!ctrl.NeedsMouseHook(false), "Keyboard roll key should not install mouse hook");
            Assert(ctrl.NeedsMouseHook(true), "Capturing roll key must enable mouse hook");

            // Bind Mouse5 -> mouse hook required when Master ON, not required when Master OFF
            string err;
            Assert(ctrl.TrySetRollKey(KeyBinding.FromMouse(MouseBindButton.Mouse5), out err), "Bind Mouse5 failed");
            Assert(ctrl.NeedsMouseHook(false), "Mouse5 roll key needs mouse hook when Master ON");

            ctrl.SetMasterEnabled(false);
            Assert(!ctrl.NeedsMouseHook(false), "Mouse5 roll key does not need mouse hook when Master OFF");
        }

        private static void TestIniRoundTrip()
        {
            string tmpFile = Path.Combine(Path.GetTempPath(), "eir_roundtrip_" + Guid.NewGuid().ToString("N") + ".ini");
            try
            {
                AppSettings s = new AppSettings();
                s.MasterEnabled = true;
                s.AutoEnableOnGame = false;
                s.AutoStartWithWindows = false;
                s.RollKey = KeyBinding.FromMouse(MouseBindButton.Mouse4);
                s.ToggleHotkey = KeyBinding.FromKeyboard(0x44, false, "F10");
                s.TapDelayMs = 42;
                s.TargetNightreign = true;
                s.TargetEldenRing = false;
                s.CustomProcesses = "sekiro.exe, armoredcore6.exe";
                s.MinimizeToTrayOnClose = false;

                Assert(s.SaveToFile(tmpFile), "SaveToFile failed");
                AppSettings loaded = AppSettings.LoadFromFile(tmpFile);

                Assert(loaded.MasterEnabled == true, "MasterEnabled mismatch");
                Assert(loaded.AutoEnableOnGame == false, "AutoEnableOnGame mismatch");
                Assert(loaded.AutoStartWithWindows == false, "AutoStartWithWindows mismatch");
                Assert(loaded.RollKey.Equals(s.RollKey), "RollKey mismatch");
                Assert(loaded.ToggleHotkey.Equals(s.ToggleHotkey), "ToggleHotkey mismatch");
                Assert(loaded.TapDelayMs == 42, "TapDelayMs mismatch");
                Assert(loaded.TargetNightreign && !loaded.TargetEldenRing, "Target checkboxes mismatch");
                Assert(loaded.CustomProcesses == "sekiro.exe, armoredcore6.exe", "CustomProcesses mismatch");
                Assert(!loaded.MinimizeToTrayOnClose, "MinimizeToTrayOnClose mismatch");

                Assert(AppSettings.ClampTapDelay(5) == 15, "Lower clamp failed");
                Assert(AppSettings.ClampTapDelay(120) == 60, "Upper clamp failed");
            }
            finally
            {
                if (File.Exists(tmpFile)) File.Delete(tmpFile);
            }
        }

        private static void TestRealWindowsRegistry()
        {
            string testValueName = "EldenInstantRoll_UnitTest_" + Guid.NewGuid().ToString("N").Substring(0, 8);
            StartupRegistryManager reg = new StartupRegistryManager(testValueName);
            string tmpFile = Path.Combine(Path.GetTempPath(), "eir_reg_" + Guid.NewGuid().ToString("N") + ".ini");
            try
            {
                Assert(!reg.IsRegistered(), "Test registry value should not exist initially");

                AppController ctrl = new AppController(new AppSettings(), reg, tmpFile, @"C:\Tools\EldenInstantRoll\EldenInstantRoll.exe");
                Assert(ctrl.SetAutoStartWithWindows(true), "Registering real HKCU Run key should succeed");
                Assert(reg.IsRegistered(), "HKCU Run key should now exist");

                // Master OFF must IMMEDIATELY delete the real HKCU Run key
                ctrl.SetMasterEnabled(false);
                Assert(!reg.IsRegistered(), "Real HKCU Run key MUST be immediately deleted on Master OFF");
            }
            finally
            {
                reg.Unregister();
                if (File.Exists(tmpFile)) File.Delete(tmpFile);
            }
        }
    }
}
