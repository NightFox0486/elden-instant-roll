using System;
using System.IO;
using System.Runtime.InteropServices;

namespace EldenInstantRoll.Tests
{
    internal sealed class MockStartupManager : IStartupManager
    {
        public bool Registered { get; private set; }
        public string LastCommand { get; private set; }

        public bool IsRegistered()
        {
            return Registered;
        }

        public bool Register(string exePath)
        {
            Registered = true;
            LastCommand = StartupRegistryManager.BuildStartupCommand(exePath);
            return true;
        }

        public bool Unregister()
        {
            Registered = false;
            LastCommand = null;
            return true;
        }
    }

    internal static class Program
    {
        private const string NeutralExePath = @"C:\Tools\EldenInstantRoll\EldenInstantRoll.exe";
        private static int _passed;
        private static int _failed;

        private static int Main()
        {
            RunTest("Win32 INPUT struct size matches native ABI", TestInputStructSize);
            RunTest("Default 7 core Nightreign actions and initial settings match v1.1.0 spec", TestDefaults);
            RunTest("Multi-key checked vs unchecked filtering in ActiveHookSnapshot", TestMultiKeyFiltering);
            RunTest("Mouse hook installed ONLY when a mouse action is checked or capturing", TestConditionalMouseHook);
            RunTest("Key conflict prevention (ToggleHotkey, duplicate active keys, unassigned)", TestConflictPrevention);
            RunTest("Custom action add/bind/save/reload/remove and ResetActionsToDefaults", TestCustomActionsAndReset);
            RunTest("v1.0.0 INI automatic migration to v1.1.0 format", TestV1IniMigration);
            RunTest("Master OFF forces child toggles OFF, disabled, and unregisters startup", TestMasterOffPriorityRule);
            RunTest("Master ON re-enables child toggles independently", TestMasterOnEnablesChildToggles);
            RunTest("Tampered INI with Master=false enforces priority invariant on startup", TestTamperedIniPriorityInvariant);
            RunTest("Target game filtering and AutoEnableOnGame manual mode", TestTargetGameFiltering);
            RunTest("Portable v1.1.0 INI save/load round-trip and TapDelay clamping", TestIniRoundTrip);
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
            Assert(s.ConfigVersion == 2, "ConfigVersion should be 2");
            Assert(s.MasterEnabled, "MasterEnabled should default true");
            Assert(s.AutoEnableOnGame, "AutoEnableOnGame should default true");
            Assert(!s.AutoStartWithWindows, "AutoStartWithWindows should default false");
            Assert(s.ToggleHotkey.Kind == BindingKind.Keyboard && s.ToggleHotkey.ScanCode == 0x42, "ToggleHotkey should default to F8");
            Assert(s.TapDelayMs == 25, "TapDelayMs should default to 25");
            Assert(s.TargetNightreign && s.TargetEldenRing, "Both Elden Ring games should default checked");

            Assert(s.Actions.Count == 7, "Expected 7 core Nightreign actions, got " + s.Actions.Count);

            int enabledCount = 0;
            for (int i = 0; i < s.Actions.Count; i++)
            {
                if (s.Actions[i].Enabled) enabledCount++;
            }
            Assert(enabledCount == 1, "Only dodge should be enabled by default");

            Assert(s.FindAction("dodge").Binding.ScanCode == 0x2A && s.FindAction("dodge").Enabled, "dodge should default LShift ON");
            Assert(s.FindAction("surge_sprint").Binding.ScanCode == 0x38 && !s.FindAction("surge_sprint").Enabled, "surge_sprint should default LAlt OFF");
            Assert(s.FindAction("character_skill").Binding.ScanCode == 0x12 && !s.FindAction("character_skill").Enabled, "character_skill should default E OFF");
            Assert(s.FindAction("ultimate_art").Binding.ScanCode == 0x13 && !s.FindAction("ultimate_art").Enabled, "ultimate_art should default R OFF");
            Assert(s.FindAction("flask").Binding.ScanCode == 0x02 && !s.FindAction("flask").Enabled, "flask should default 1 OFF");
            Assert(s.FindAction("use_item").Binding.ScanCode == 0x22 && !s.FindAction("use_item").Enabled, "use_item should default G OFF");
            Assert(s.FindAction("switch_item").Binding.ScanCode == 0x03 && !s.FindAction("switch_item").Enabled, "switch_item should default 2 OFF");
        }

        private static void TestMultiKeyFiltering()
        {
            AppController ctrl = new AppController(new AppSettings(), new MockStartupManager(), null, NeutralExePath);

            // Initially only LShift (0x2A) is active; E (0x12), R (0x13), 1 (0x02) are unchecked
            ActiveHookSnapshot snap = ctrl.GetActiveHookSnapshot();
            Assert(snap.KeyboardBindings.Count == 1, "Expected 1 active keyboard binding initially");
            Assert(snap.KeyboardBindings.ContainsKey(KeyBinding.MakeKeyboardCompositeId(0x2A, false)), "LShift should be active");
            Assert(!snap.KeyboardBindings.ContainsKey(KeyBinding.MakeKeyboardCompositeId(0x12, false)), "Unchecked E must NOT be hooked");
            Assert(!snap.KeyboardBindings.ContainsKey(KeyBinding.MakeKeyboardCompositeId(0x13, false)), "Unchecked R must NOT be hooked");
            Assert(snap.MouseButtons.Count == 0, "No mouse buttons should be active initially");

            // Enable multiple keys: flask (1), ultimate_art (R), use_item (G)
            string err;
            Assert(ctrl.TrySetActionEnabled("flask", true, out err), "Enable flask failed: " + err);
            Assert(ctrl.TrySetActionEnabled("ultimate_art", true, out err), "Enable ultimate_art failed: " + err);
            Assert(ctrl.TrySetActionEnabled("use_item", true, out err), "Enable use_item failed: " + err);

            snap = ctrl.GetActiveHookSnapshot();
            Assert(snap.KeyboardBindings.Count == 4, "Expected 4 active keyboard bindings, got " + snap.KeyboardBindings.Count);
            Assert(snap.KeyboardBindings.ContainsKey(KeyBinding.MakeKeyboardCompositeId(0x2A, false)), "LShift active");
            Assert(snap.KeyboardBindings.ContainsKey(KeyBinding.MakeKeyboardCompositeId(0x02, false)), "1 active");
            Assert(snap.KeyboardBindings.ContainsKey(KeyBinding.MakeKeyboardCompositeId(0x13, false)), "R active");
            Assert(snap.KeyboardBindings.ContainsKey(KeyBinding.MakeKeyboardCompositeId(0x22, false)), "G active");
            Assert(!snap.KeyboardBindings.ContainsKey(KeyBinding.MakeKeyboardCompositeId(0x12, false)), "Unchecked E must remain unhooked");

            // Uncheck use_item -> immediately removed from hook snapshot
            Assert(ctrl.TrySetActionEnabled("use_item", false, out err), "Disable use_item failed");
            snap = ctrl.GetActiveHookSnapshot();
            Assert(snap.KeyboardBindings.Count == 3, "Expected 3 active bindings after unchecking use_item");
            Assert(!snap.KeyboardBindings.ContainsKey(KeyBinding.MakeKeyboardCompositeId(0x22, false)), "G must be removed immediately");
            Assert(ctrl.GetActiveActionCount() == 3, "Active action count should be 3");
        }

        private static void TestConditionalMouseHook()
        {
            AppController ctrl = new AppController(new AppSettings(), new MockStartupManager(), null, NeutralExePath);

            // Only keyboard dodge is checked -> NO mouse hook installed
            Assert(!ctrl.NeedsMouseHook(false), "Mouse hook must NOT be installed when no mouse action is checked");
            Assert(ctrl.NeedsMouseHook(true), "Mouse hook MUST be installed during action key capture");

            // Rebind character_skill to Mouse4 (still unchecked -> still no mouse hook!)
            string err;
            Assert(ctrl.TrySetActionKey("character_skill", KeyBinding.FromMouse(MouseBindButton.Mouse4), out err), "Bind Mouse4 failed");
            Assert(!ctrl.NeedsMouseHook(false), "Unchecked Mouse4 action must NOT install mouse hook");

            // Check character_skill -> mouse hook required
            Assert(ctrl.TrySetActionEnabled("character_skill", true, out err), "Enable Mouse4 failed: " + err);
            Assert(ctrl.NeedsMouseHook(false), "Mouse hook must be installed when Mouse4 action is checked");
            Assert(ctrl.GetActiveHookSnapshot().MouseButtons.Contains(MouseBindButton.Mouse4), "Mouse4 should be in active snapshot");

            // Add custom MouseLeft action and enable
            ActionBindingItem custom = ctrl.AddCustomAction("좌클릭 탭");
            Assert(ctrl.TrySetActionKey(custom.Id, KeyBinding.FromMouse(MouseBindButton.MouseLeft), out err), "Bind MouseLeft failed");
            Assert(ctrl.TrySetActionEnabled(custom.Id, true, out err), "Enable MouseLeft failed");
            Assert(ctrl.GetActiveHookSnapshot().MouseButtons.Contains(MouseBindButton.MouseLeft), "MouseLeft should be in active snapshot");

            // Master OFF -> mouse hook not needed
            ctrl.SetMasterEnabled(false);
            Assert(!ctrl.NeedsMouseHook(false), "Mouse hook must NOT be installed when Master is OFF");

            // Master ON, uncheck both mouse actions -> mouse hook uninstalled again
            ctrl.SetMasterEnabled(true);
            Assert(ctrl.NeedsMouseHook(false), "Mouse hook needed again when Master ON with checked mouse actions");
            Assert(ctrl.TrySetActionEnabled("character_skill", false, out err), "Uncheck character_skill");
            Assert(ctrl.TrySetActionEnabled(custom.Id, false, out err), "Uncheck custom");
            Assert(!ctrl.NeedsMouseHook(false), "Mouse hook must be uninstalled once all mouse actions are unchecked");
        }

        private static void TestConflictPrevention()
        {
            AppController ctrl = new AppController(new AppSettings(), new MockStartupManager(), null, NeutralExePath);
            string err;

            // 1. Clearing a key makes it unassigned -> cannot be enabled
            Assert(ctrl.ClearActionKey("use_item"), "ClearActionKey should succeed");
            Assert(!ctrl.TrySetActionEnabled("use_item", true, out err), "Unassigned action must not be checkable");

            // 2. Cannot assign ToggleHotkey (F8) to an action
            Assert(!ctrl.TrySetActionKey("flask", KeyBinding.DefaultToggleHotkey(), out err), "Assigning ToggleHotkey to action must fail");

            // 3. Cannot change ToggleHotkey to an actively checked action key (LShift)
            Assert(!ctrl.TrySetToggleHotkey(KeyBinding.FromKeyboard(0x2A, false, "LShift"), out err), "ToggleHotkey conflicting with active dodge must fail");

            // 4. Bind active dodge to E while unchecked character_skill is also E -> allowed
            Assert(ctrl.TrySetActionKey(ActionBindingItem.DodgeActionId, KeyBinding.FromKeyboard(0x12, false, "E"), out err), "Rebinding active dodge to E should succeed when character_skill is unchecked");

            // 5. Attempting to ALSO check character_skill (also E) must fail as duplicate active key
            Assert(!ctrl.TrySetActionEnabled("character_skill", true, out err), "Duplicate active key E must be rejected");
            Assert(!string.IsNullOrEmpty(err), "Duplicate key error message expected");

            // 6. Clearing an active key disables it
            Assert(ctrl.ClearActionKey(ActionBindingItem.DodgeActionId), "ClearActionKey on active dodge should succeed");
            Assert(ctrl.GetActiveActionCount() == 0, "Cleared action must automatically uncheck");
        }

        private static void TestCustomActionsAndReset()
        {
            string tmpFile = Path.Combine(Path.GetTempPath(), "eir_custom_" + Guid.NewGuid().ToString("N") + ".ini");
            try
            {
                AppController ctrl = new AppController(new AppSettings(), new MockStartupManager(), tmpFile, NeutralExePath);
                ActionBindingItem custom = ctrl.AddCustomAction("사이드 구르기");
                Assert(custom.IsCustom && custom.Category == ActionBindingItem.CategoryCustom, "Custom action metadata mismatch");

                string err;
                Assert(ctrl.TrySetActionKey(custom.Id, KeyBinding.FromMouse(MouseBindButton.Mouse5), out err), "Bind Mouse5 to custom failed: " + err);
                Assert(ctrl.TrySetActionEnabled(custom.Id, true, out err), "Enable custom Mouse5 failed: " + err);
                Assert(ctrl.NeedsMouseHook(false), "Custom active Mouse5 should require mouse hook");

                // Reload from INI and confirm custom action persisted
                AppSettings reloaded = AppSettings.LoadFromFile(tmpFile);
                ActionBindingItem loadedCustom = reloaded.FindAction(custom.Id);
                Assert(loadedCustom != null && loadedCustom.IsCustom && loadedCustom.Enabled && loadedCustom.Name == "사이드 구르기", "Reloaded custom action mismatch");
                Assert(loadedCustom.Binding != null && loadedCustom.Binding.MouseButton == MouseBindButton.Mouse5, "Reloaded custom binding mismatch");

                // Remove custom action
                Assert(ctrl.RemoveCustomAction(custom.Id), "RemoveCustomAction failed");
                Assert(!ctrl.NeedsMouseHook(false), "Mouse hook should turn off after removing custom Mouse5");

                // Add another and reset to defaults
                ctrl.AddCustomAction("임시 키");
                ctrl.ResetActionsToDefaults();
                Assert(ctrl.GetActionsSnapshot().Count == 7, "Reset should restore exactly 7 default core actions");
                Assert(ctrl.GetActiveActionCount() == 1, "Reset should leave only dodge active");
            }
            finally
            {
                if (File.Exists(tmpFile)) File.Delete(tmpFile);
            }
        }

        private static void TestV1IniMigration()
        {
            string tmpFile = Path.Combine(Path.GetTempPath(), "eir_v1_" + Guid.NewGuid().ToString("N") + ".ini");
            try
            {
                File.WriteAllText(tmpFile,
                    "; EldenInstantRoll Portable Configuration\r\n" +
                    "[General]\r\n" +
                    "MasterEnabled=true\r\n" +
                    "AutoEnableOnGame=true\r\n" +
                    "AutoStartWithWindows=false\r\n" +
                    "MinimizeToTrayOnClose=true\r\n" +
                    "[Input]\r\n" +
                    "RollKey=KEY:0010:0|Q\r\n" +
                    "ToggleHotkey=KEY:0042:0|F8\r\n" +
                    "TapDelayMs=30\r\n" +
                    "[Targets]\r\n" +
                    "TargetNightreign=true\r\n" +
                    "TargetEldenRing=false\r\n");

                AppSettings migrated = AppSettings.LoadFromFile(tmpFile);
                Assert(migrated.MigratedFromV1, "Should detect v1.0.0 migration");
                Assert(migrated.ConfigVersion == 2, "Should upgrade ConfigVersion to 2");
                Assert(migrated.TapDelayMs == 30, "TapDelayMs should preserve 30ms");
                Assert(migrated.TargetNightreign && !migrated.TargetEldenRing, "Target checkboxes should preserve");

                ActionBindingItem dodge = migrated.FindAction(ActionBindingItem.DodgeActionId);
                Assert(dodge != null && dodge.Enabled && dodge.Binding != null && dodge.Binding.ScanCode == 0x10, "v1 RollKey Q must migrate to enabled dodge action");

                AppController ctrl = new AppController(migrated, new MockStartupManager(), tmpFile, NeutralExePath);
                Assert(ctrl.GetActiveActionCount() == 1, "Only migrated dodge should be active");
                string savedText = File.ReadAllText(tmpFile);
                Assert(savedText.Contains("ConfigVersion=2") && savedText.Contains("Action.dodge=1|KEY:0010:0|Q"), "Migrated v1.1.0 INI should be saved automatically");
            }
            finally
            {
                if (File.Exists(tmpFile)) File.Delete(tmpFile);
            }
        }

        private static void TestMasterOffPriorityRule()
        {
            string tmpFile = Path.Combine(Path.GetTempPath(), "eir_master_" + Guid.NewGuid().ToString("N") + ".ini");
            try
            {
                MockStartupManager startup = new MockStartupManager();
                AppController ctrl = new AppController(new AppSettings(), startup, tmpFile, NeutralExePath);

                Assert(ctrl.MasterEnabled && ctrl.ChildTogglesEnabled, "Master & child toggles should start enabled");
                Assert(ctrl.SetAutoEnableOnGame(true), "Allow AutoEnableOnGame=true while Master ON");
                Assert(ctrl.SetAutoStartWithWindows(true), "Allow AutoStartWithWindows=true while Master ON");
                Assert(startup.Registered, "Startup should be registered");
                Assert(startup.LastCommand == "\"" + NeutralExePath + "\" --minimized", "Unexpected startup command");

                ctrl.SetMasterEnabled(false);

                Assert(!ctrl.MasterEnabled, "Master should be OFF");
                Assert(!ctrl.ChildTogglesEnabled, "Child toggles must be Disabled when Master is OFF");
                Assert(!ctrl.AutoEnableOnGame, "AutoEnableOnGame must automatically turn OFF");
                Assert(!ctrl.AutoStartWithWindows, "AutoStartWithWindows must automatically turn OFF");
                Assert(!startup.Registered, "Startup registry MUST be deleted immediately when Master turns OFF");

                Assert(!ctrl.SetAutoEnableOnGame(true), "SetAutoEnableOnGame(true) must be refused while Master is OFF");
                Assert(!ctrl.SetAutoStartWithWindows(true), "SetAutoStartWithWindows(true) must be refused while Master is OFF");
            }
            finally
            {
                if (File.Exists(tmpFile)) File.Delete(tmpFile);
            }
        }

        private static void TestMasterOnEnablesChildToggles()
        {
            string tmpFile = Path.Combine(Path.GetTempPath(), "eir_master_on_" + Guid.NewGuid().ToString("N") + ".ini");
            try
            {
                MockStartupManager startup = new MockStartupManager();
                AppController ctrl = new AppController(new AppSettings(), startup, tmpFile, NeutralExePath);

                ctrl.SetMasterEnabled(false);
                ctrl.SetMasterEnabled(true);
                Assert(ctrl.MasterEnabled && ctrl.ChildTogglesEnabled, "Child toggles re-enabled when Master ON");
                Assert(!ctrl.AutoEnableOnGame && !ctrl.AutoStartWithWindows, "Child toggles stay OFF until toggled by user");
                Assert(ctrl.ShouldInterceptForProcess("notepad.exe"), "Manual active mode works when AutoEnableOnGame is OFF");

                Assert(ctrl.SetAutoEnableOnGame(true), "Re-enable AutoEnableOnGame");
                Assert(!ctrl.ShouldInterceptForProcess("notepad.exe"), "notepad.exe waits when AutoEnableOnGame is ON");
                Assert(ctrl.ShouldInterceptForProcess("nightreign.exe"), "nightreign.exe active when AutoEnableOnGame is ON");

                Assert(ctrl.SetAutoStartWithWindows(true), "Re-enable AutoStartWithWindows");
                Assert(startup.Registered, "Startup registered");
                Assert(ctrl.SetAutoStartWithWindows(false), "Disable AutoStartWithWindows independently");
                Assert(!startup.Registered, "Startup unregistered");
            }
            finally
            {
                if (File.Exists(tmpFile)) File.Delete(tmpFile);
            }
        }

        private static void TestTamperedIniPriorityInvariant()
        {
            string tmpFile = Path.Combine(Path.GetTempPath(), "eir_tamper_" + Guid.NewGuid().ToString("N") + ".ini");
            try
            {
                File.WriteAllText(tmpFile,
                    "MasterEnabled=false\r\n" +
                    "AutoEnableOnGame=true\r\n" +
                    "AutoStartWithWindows=true\r\n");

                AppSettings loaded = AppSettings.LoadFromFile(tmpFile);
                Assert(!loaded.MasterEnabled && !loaded.AutoEnableOnGame && !loaded.AutoStartWithWindows, "Tampered INI must enforce Master OFF invariant");

                MockStartupManager startup = new MockStartupManager();
                startup.Register(NeutralExePath);
                AppController ctrl = new AppController(loaded, startup, tmpFile, NeutralExePath);
                Assert(!startup.Registered, "Stale registry entry must be cleaned on startup when Master=false");
            }
            finally
            {
                if (File.Exists(tmpFile)) File.Delete(tmpFile);
            }
        }

        private static void TestTargetGameFiltering()
        {
            AppController ctrl = new AppController(new AppSettings(), new MockStartupManager(), null, NeutralExePath);

            Assert(ctrl.ShouldInterceptForProcess("nightreign.exe"), "nightreign.exe should match");
            Assert(ctrl.ShouldInterceptForProcess("ELDENRING.EXE"), "Case-insensitive eldenring.exe should match");
            Assert(!ctrl.ShouldInterceptForProcess("chrome.exe"), "chrome.exe should wait");
            Assert(ctrl.GetRunStateForProcess("chrome.exe") == EngineRunState.WaitingForGame, "chrome.exe state WaitingForGame");
            Assert(ctrl.GetRunStateForProcess("nightreign.exe") == EngineRunState.Active, "nightreign.exe state Active");

            ctrl.SetTargets(false, true, "sekiro, DarkSoulsIII.exe");
            Assert(!ctrl.ShouldInterceptForProcess("nightreign.exe"), "Unchecked nightreign.exe excluded");
            Assert(ctrl.ShouldInterceptForProcess("sekiro.exe"), "Custom process without .exe matched");
            Assert(ctrl.ShouldInterceptForProcess("darksoulsiii.exe"), "Custom process matched case-insensitively");

            ctrl.SetMasterEnabled(false);
            Assert(!ctrl.ShouldInterceptForProcess("eldenring.exe"), "Master OFF blocks interception");
            Assert(ctrl.GetRunStateForProcess("eldenring.exe") == EngineRunState.Off, "Master OFF state Off");
        }

        private static void TestIniRoundTrip()
        {
            string tmpFile = Path.Combine(Path.GetTempPath(), "eir_rt_" + Guid.NewGuid().ToString("N") + ".ini");
            try
            {
                AppSettings s = new AppSettings();
                s.AutoEnableOnGame = false;
                s.ToggleHotkey = KeyBinding.FromKeyboard(0x44, false, "F10");
                s.TapDelayMs = 42;
                s.TargetNightreign = true;
                s.TargetEldenRing = false;
                s.CustomProcesses = "sekiro.exe, armoredcore6.exe";
                s.MinimizeToTrayOnClose = false;

                s.FindAction(ActionBindingItem.DodgeActionId).Binding = KeyBinding.FromKeyboard(0x10, false, "Q");
                s.FindAction("surge_sprint").Binding = KeyBinding.FromKeyboard(0x2A, false, "LShift");
                s.FindAction("surge_sprint").Enabled = true;
                s.FindAction("flask").Enabled = true;

                Assert(s.SaveToFile(tmpFile), "SaveToFile failed");
                AppSettings loaded = AppSettings.LoadFromFile(tmpFile);

                Assert(loaded.TapDelayMs == 42, "TapDelayMs mismatch");
                Assert(loaded.ToggleHotkey.ScanCode == 0x44, "ToggleHotkey mismatch");
                Assert(loaded.FindAction(ActionBindingItem.DodgeActionId).Binding.ScanCode == 0x10, "Dodge key mismatch");
                Assert(loaded.FindAction("surge_sprint").Enabled && loaded.FindAction("surge_sprint").Binding.ScanCode == 0x2A, "surge_sprint mismatch");
                Assert(loaded.FindAction("flask").Enabled, "flask enabled state mismatch");
                Assert(!loaded.FindAction("switch_item").Enabled, "switch_item should remain unchecked");
                Assert(AppSettings.ClampTapDelay(5) == 15 && AppSettings.ClampTapDelay(120) == 60, "TapDelay clamp mismatch");
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

                AppController ctrl = new AppController(new AppSettings(), reg, tmpFile, NeutralExePath);
                Assert(ctrl.SetAutoStartWithWindows(true), "Registering real HKCU Run key should succeed");
                Assert(reg.IsRegistered(), "HKCU Run key should now exist");

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
