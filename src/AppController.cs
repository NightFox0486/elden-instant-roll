using System;
using System.Collections.Generic;

namespace EldenInstantRoll
{
    public enum EngineRunState
    {
        Off = 0,
        WaitingForGame = 1,
        Active = 2
    }

    public sealed class AppController
    {
        private readonly object _sync = new object();
        private readonly AppSettings _settings;
        private readonly IStartupManager _startupManager;
        private readonly string _settingsPath;
        private readonly string _executablePath;

        private HashSet<string> _targetProcessCache;
        private string _lastForegroundProcess = string.Empty;

        public event EventHandler StateChanged;

        public AppController(AppSettings settings, IStartupManager startupManager, string settingsPath, string executablePath)
        {
            if (settings == null) throw new ArgumentNullException("settings");
            if (startupManager == null) throw new ArgumentNullException("startupManager");

            _settings = settings;
            _startupManager = startupManager;
            _settingsPath = settingsPath;
            _executablePath = executablePath;

            RebuildTargetCacheLocked();
            EnforceMasterPriorityInvariant(false);
        }

        public string SettingsPath
        {
            get { return _settingsPath; }
        }

        public bool MasterEnabled
        {
            get { lock (_sync) { return _settings.MasterEnabled; } }
        }

        public bool ChildTogglesEnabled
        {
            get { lock (_sync) { return _settings.MasterEnabled; } }
        }

        public bool AutoEnableOnGame
        {
            get { lock (_sync) { return _settings.MasterEnabled && _settings.AutoEnableOnGame; } }
        }

        public bool AutoStartWithWindows
        {
            get { lock (_sync) { return _settings.MasterEnabled && _settings.AutoStartWithWindows; } }
        }

        public KeyBinding RollKey
        {
            get { lock (_sync) { return _settings.RollKey; } }
        }

        public KeyBinding ToggleHotkey
        {
            get { lock (_sync) { return _settings.ToggleHotkey; } }
        }

        public int TapDelayMs
        {
            get { lock (_sync) { return _settings.TapDelayMs; } }
        }

        public bool TargetNightreign
        {
            get { lock (_sync) { return _settings.TargetNightreign; } }
        }

        public bool TargetEldenRing
        {
            get { lock (_sync) { return _settings.TargetEldenRing; } }
        }

        public string CustomProcesses
        {
            get { lock (_sync) { return _settings.CustomProcesses ?? string.Empty; } }
        }

        public bool MinimizeToTrayOnClose
        {
            get { lock (_sync) { return _settings.MinimizeToTrayOnClose; } }
        }

        public string LastForegroundProcess
        {
            get { lock (_sync) { return _lastForegroundProcess; } }
        }

        public void SetMasterEnabled(bool enabled)
        {
            bool changed;
            lock (_sync)
            {
                changed = (_settings.MasterEnabled != enabled);
                _settings.MasterEnabled = enabled;

                if (!enabled)
                {
                    // CORE PRIORITY RULE:
                    // Turning Master OFF immediately forces BOTH child toggles OFF, disables them,
                    // and deletes the Windows startup registry entry.
                    if (_settings.AutoEnableOnGame || _settings.AutoStartWithWindows)
                    {
                        changed = true;
                    }
                    _settings.AutoEnableOnGame = false;
                    _settings.AutoStartWithWindows = false;
                    _startupManager.Unregister();
                }

                SaveLocked();
            }

            if (changed)
            {
                RaiseStateChanged();
            }
        }

        public void ToggleMaster()
        {
            SetMasterEnabled(!MasterEnabled);
        }

        public bool SetAutoEnableOnGame(bool enabled)
        {
            bool changed = false;
            lock (_sync)
            {
                if (!_settings.MasterEnabled)
                {
                    // Cannot enable child toggle when Master is OFF
                    _settings.AutoEnableOnGame = false;
                    return false;
                }

                if (_settings.AutoEnableOnGame != enabled)
                {
                    _settings.AutoEnableOnGame = enabled;
                    changed = true;
                    SaveLocked();
                }
            }

            if (changed)
            {
                RaiseStateChanged();
            }
            return true;
        }

        public bool SetAutoStartWithWindows(bool enabled)
        {
            bool changed = false;
            bool result = false;

            lock (_sync)
            {
                if (!_settings.MasterEnabled)
                {
                    // Cannot enable child toggle when Master is OFF; ensure registry is removed
                    _settings.AutoStartWithWindows = false;
                    _startupManager.Unregister();
                    return false;
                }

                if (enabled)
                {
                    result = _startupManager.Register(_executablePath);
                    if (result)
                    {
                        if (!_settings.AutoStartWithWindows)
                        {
                            _settings.AutoStartWithWindows = true;
                            changed = true;
                        }
                        SaveLocked();
                    }
                }
                else
                {
                    result = _startupManager.Unregister();
                    if (_settings.AutoStartWithWindows)
                    {
                        _settings.AutoStartWithWindows = false;
                        changed = true;
                    }
                    SaveLocked();
                }
            }

            if (changed)
            {
                RaiseStateChanged();
            }
            return result;
        }

        public bool TrySetRollKey(KeyBinding newKey, out string errorMessage)
        {
            errorMessage = null;
            if (newKey == null)
            {
                errorMessage = "유효하지 않은 키입니다.";
                return false;
            }

            lock (_sync)
            {
                if (newKey.Equals(_settings.ToggleHotkey))
                {
                    errorMessage = "구르기 키와 ON/OFF 단축키는 같은 키로 설정할 수 없습니다.";
                    return false;
                }

                _settings.RollKey = newKey;
                SaveLocked();
            }

            RaiseStateChanged();
            return true;
        }

        public bool TrySetToggleHotkey(KeyBinding newHotkey, out string errorMessage)
        {
            errorMessage = null;
            if (newHotkey == null || newHotkey.Kind != BindingKind.Keyboard)
            {
                errorMessage = "ON/OFF 단축키는 키보드 키만 지정할 수 있습니다.";
                return false;
            }

            lock (_sync)
            {
                if (newHotkey.Equals(_settings.RollKey))
                {
                    errorMessage = "ON/OFF 단축키와 구르기 키는 같은 키로 설정할 수 없습니다.";
                    return false;
                }

                _settings.ToggleHotkey = newHotkey;
                SaveLocked();
            }

            RaiseStateChanged();
            return true;
        }

        public void SetTapDelayMs(int delayMs)
        {
            int clamped = AppSettings.ClampTapDelay(delayMs);
            bool changed;
            lock (_sync)
            {
                changed = (_settings.TapDelayMs != clamped);
                if (changed)
                {
                    _settings.TapDelayMs = clamped;
                    SaveLocked();
                }
            }

            if (changed)
            {
                RaiseStateChanged();
            }
        }

        public void SetTargets(bool targetNightreign, bool targetEldenRing, string customProcesses)
        {
            string normalizedCustom = (customProcesses ?? string.Empty).Trim();
            bool changed;
            lock (_sync)
            {
                changed = (_settings.TargetNightreign != targetNightreign)
                    || (_settings.TargetEldenRing != targetEldenRing)
                    || !string.Equals(_settings.CustomProcesses ?? string.Empty, normalizedCustom, StringComparison.Ordinal);

                if (changed)
                {
                    _settings.TargetNightreign = targetNightreign;
                    _settings.TargetEldenRing = targetEldenRing;
                    _settings.CustomProcesses = normalizedCustom;
                    RebuildTargetCacheLocked();
                    SaveLocked();
                }
            }

            if (changed)
            {
                RaiseStateChanged();
            }
        }

        public void SetMinimizeToTrayOnClose(bool minimize)
        {
            bool changed;
            lock (_sync)
            {
                changed = (_settings.MinimizeToTrayOnClose != minimize);
                if (changed)
                {
                    _settings.MinimizeToTrayOnClose = minimize;
                    SaveLocked();
                }
            }

            if (changed)
            {
                RaiseStateChanged();
            }
        }

        public void UpdateForegroundProcess(string processFileName)
        {
            string normalized = (processFileName ?? string.Empty).Trim();
            lock (_sync)
            {
                _lastForegroundProcess = normalized;
            }
        }

        public bool IsTargetProcess(string processFileName)
        {
            if (string.IsNullOrEmpty(processFileName))
            {
                return false;
            }
            lock (_sync)
            {
                return _targetProcessCache.Contains(processFileName.Trim());
            }
        }

        public bool ShouldInterceptRollForProcess(string foregroundProcessFileName)
        {
            lock (_sync)
            {
                if (!_settings.MasterEnabled)
                {
                    return false;
                }
                if (!_settings.AutoEnableOnGame)
                {
                    // Manual active mode: works everywhere (Notepad test, custom games, etc.)
                    return true;
                }
                if (string.IsNullOrEmpty(foregroundProcessFileName))
                {
                    return false;
                }
                return _targetProcessCache.Contains(foregroundProcessFileName.Trim());
            }
        }

        public EngineRunState GetCurrentRunState()
        {
            return GetRunStateForProcess(LastForegroundProcess);
        }

        public EngineRunState GetRunStateForProcess(string foregroundProcessFileName)
        {
            lock (_sync)
            {
                if (!_settings.MasterEnabled)
                {
                    return EngineRunState.Off;
                }
                if (!_settings.AutoEnableOnGame)
                {
                    return EngineRunState.Active;
                }
                if (!string.IsNullOrEmpty(foregroundProcessFileName) && _targetProcessCache.Contains(foregroundProcessFileName.Trim()))
                {
                    return EngineRunState.Active;
                }
                return EngineRunState.WaitingForGame;
            }
        }

        public bool NeedsMouseHook(bool isCapturingRollKey)
        {
            if (isCapturingRollKey)
            {
                return true;
            }
            lock (_sync)
            {
                return _settings.MasterEnabled && _settings.RollKey != null && _settings.RollKey.Kind == BindingKind.Mouse;
            }
        }

        private void EnforceMasterPriorityInvariant(bool saveIfChanged)
        {
            lock (_sync)
            {
                if (!_settings.MasterEnabled)
                {
                    _settings.AutoEnableOnGame = false;
                    _settings.AutoStartWithWindows = false;
                    _startupManager.Unregister();
                }
                else
                {
                    if (_settings.AutoStartWithWindows)
                    {
                        _startupManager.Register(_executablePath);
                    }
                    else
                    {
                        // Keep registry clean if user turned it off in config
                        if (_startupManager.IsRegistered())
                        {
                            _startupManager.Unregister();
                        }
                    }
                }

                if (saveIfChanged)
                {
                    SaveLocked();
                }
            }
        }

        private void RebuildTargetCacheLocked()
        {
            _targetProcessCache = _settings.GetTargetProcessSet();
        }

        private void SaveLocked()
        {
            if (!string.IsNullOrEmpty(_settingsPath))
            {
                _settings.SaveToFile(_settingsPath);
            }
        }

        private void RaiseStateChanged()
        {
            EventHandler handler = StateChanged;
            if (handler != null)
            {
                handler(this, EventArgs.Empty);
            }
        }
    }
}
