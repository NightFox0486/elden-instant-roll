using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace EldenInstantRoll
{
    public enum EngineRunState
    {
        Off = 0,
        WaitingForGame = 1,
        Active = 2
    }

    public sealed class ActiveHookSnapshot
    {
        public Dictionary<uint, KeyBinding> KeyboardBindings { get; private set; }
        public HashSet<MouseBindButton> MouseButtons { get; private set; }

        public ActiveHookSnapshot(Dictionary<uint, KeyBinding> keyboardBindings, HashSet<MouseBindButton> mouseButtons)
        {
            KeyboardBindings = keyboardBindings;
            MouseButtons = mouseButtons;
        }

        public bool HasAnyMouseButton
        {
            get { return MouseButtons.Count > 0; }
        }
    }

    public sealed class AppController
    {
        private readonly object _sync = new object();
        private readonly AppSettings _settings;
        private readonly IStartupManager _startupManager;
        private readonly string _settingsPath;
        private readonly string _executablePath;

        private HashSet<string> _targetProcessCache;
        private volatile ActiveHookSnapshot _activeSnapshot;
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
            RebuildActiveSnapshotLocked();
            EnforceMasterPriorityInvariant(settings.MigratedFromV1);
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

        public ActiveHookSnapshot GetActiveHookSnapshot()
        {
            return _activeSnapshot;
        }

        public List<ActionBindingItem> GetActionsSnapshot()
        {
            lock (_sync)
            {
                List<ActionBindingItem> copy = new List<ActionBindingItem>(_settings.Actions.Count);
                for (int i = 0; i < _settings.Actions.Count; i++)
                {
                    copy.Add(_settings.Actions[i].Clone());
                }
                return copy;
            }
        }

        public int GetActiveActionCount()
        {
            lock (_sync)
            {
                int count = 0;
                for (int i = 0; i < _settings.Actions.Count; i++)
                {
                    if (_settings.Actions[i].Enabled && _settings.Actions[i].Binding != null)
                    {
                        count++;
                    }
                }
                return count;
            }
        }

        public string FormatActiveKeysSummary(int maxKeysToShow)
        {
            lock (_sync)
            {
                List<string> activeNames = new List<string>();
                for (int i = 0; i < _settings.Actions.Count; i++)
                {
                    ActionBindingItem item = _settings.Actions[i];
                    if (item.Enabled && item.Binding != null)
                    {
                        activeNames.Add(item.Binding.FormatShortText());
                    }
                }

                if (activeNames.Count == 0)
                {
                    return "활성 키: 0개 (없음)";
                }

                StringBuilder sb = new StringBuilder();
                sb.AppendFormat(CultureInfo.InvariantCulture, "활성 키: {0}개 (", activeNames.Count);
                int shown = Math.Min(activeNames.Count, maxKeysToShow);
                for (int i = 0; i < shown; i++)
                {
                    if (i > 0) sb.Append(", ");
                    sb.Append(activeNames[i]);
                }
                if (activeNames.Count > shown)
                {
                    sb.AppendFormat(CultureInfo.InvariantCulture, " 외 {0}", activeNames.Count - shown);
                }
                sb.Append(")");
                return sb.ToString();
            }
        }

        public string FormatCompactActiveKeysForTray()
        {
            lock (_sync)
            {
                List<string> activeNames = new List<string>();
                for (int i = 0; i < _settings.Actions.Count; i++)
                {
                    ActionBindingItem item = _settings.Actions[i];
                    if (item.Enabled && item.Binding != null)
                    {
                        activeNames.Add(item.Binding.FormatShortText());
                    }
                }

                if (activeNames.Count == 0)
                {
                    return "활성:0";
                }

                int shown = Math.Min(activeNames.Count, 2);
                StringBuilder sb = new StringBuilder();
                sb.AppendFormat(CultureInfo.InvariantCulture, "활성:{0}(", activeNames.Count);
                for (int i = 0; i < shown; i++)
                {
                    if (i > 0) sb.Append(",");
                    sb.Append(activeNames[i]);
                }
                if (activeNames.Count > shown)
                {
                    sb.Append("+");
                }
                sb.Append(")");
                return sb.ToString();
            }
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

        public bool TrySetActionEnabled(string actionId, bool enabled, out string errorMessage)
        {
            errorMessage = null;
            bool changed = false;

            lock (_sync)
            {
                ActionBindingItem item = _settings.FindAction(actionId);
                if (item == null)
                {
                    errorMessage = "액션을 찾을 수 없습니다.";
                    return false;
                }

                if (enabled)
                {
                    if (item.Binding == null)
                    {
                        errorMessage = "먼저 키를 지정한 뒤 체크해 주세요.";
                        return false;
                    }

                    if (item.Binding.Equals(_settings.ToggleHotkey))
                    {
                        errorMessage = "ON/OFF 단축키([" + _settings.ToggleHotkey.FormatShortText() + "])와 동일한 키는 활성화할 수 없습니다.";
                        return false;
                    }

                    ActionBindingItem duplicate = FindOtherActiveActionWithBindingLocked(actionId, item.Binding);
                    if (duplicate != null)
                    {
                        errorMessage = "'" + duplicate.Name + "' 액션과 같은 키([" + item.Binding.FormatShortText() + "])가 이미 활성화되어 있습니다.";
                        return false;
                    }
                }

                if (item.Enabled != enabled)
                {
                    item.Enabled = enabled;
                    changed = true;
                    RebuildActiveSnapshotLocked();
                    SaveLocked();
                }
            }

            if (changed)
            {
                RaiseStateChanged();
            }
            return true;
        }

        public bool TrySetActionKey(string actionId, KeyBinding newKey, out string errorMessage)
        {
            errorMessage = null;
            if (newKey == null)
            {
                errorMessage = "유효하지 않은 키입니다.";
                return false;
            }

            lock (_sync)
            {
                ActionBindingItem item = _settings.FindAction(actionId);
                if (item == null)
                {
                    errorMessage = "액션을 찾을 수 없습니다.";
                    return false;
                }

                if (newKey.Equals(_settings.ToggleHotkey))
                {
                    errorMessage = "ON/OFF 단축키([" + _settings.ToggleHotkey.FormatShortText() + "])와 같은 키로 지정할 수 없습니다.";
                    return false;
                }

                if (item.Enabled)
                {
                    ActionBindingItem duplicate = FindOtherActiveActionWithBindingLocked(actionId, newKey);
                    if (duplicate != null)
                    {
                        errorMessage = "'" + duplicate.Name + "' 액션이 이미 [" + newKey.FormatShortText() + "] 키로 활성화되어 있습니다.";
                        return false;
                    }
                }

                item.Binding = newKey;
                RebuildActiveSnapshotLocked();
                SaveLocked();
            }

            RaiseStateChanged();
            return true;
        }

        public bool ClearActionKey(string actionId)
        {
            bool changed = false;
            lock (_sync)
            {
                ActionBindingItem item = _settings.FindAction(actionId);
                if (item == null)
                {
                    return false;
                }

                if (item.Binding != null || item.Enabled)
                {
                    item.Binding = null;
                    item.Enabled = false;
                    changed = true;
                    RebuildActiveSnapshotLocked();
                    SaveLocked();
                }
            }

            if (changed)
            {
                RaiseStateChanged();
            }
            return true;
        }

        public ActionBindingItem AddCustomAction(string name)
        {
            ActionBindingItem created;
            lock (_sync)
            {
                int nextNum = 1;
                for (int i = 0; i < _settings.Actions.Count; i++)
                {
                    if (_settings.Actions[i].IsCustom)
                    {
                        nextNum++;
                    }
                }

                string cleanName = string.IsNullOrEmpty(name) ? ("사용자 키 " + nextNum.ToString(CultureInfo.InvariantCulture)) : name.Trim();
                string id = "custom_" + Guid.NewGuid().ToString("N").Substring(0, 8);
                created = new ActionBindingItem(id, ActionBindingItem.CategoryCustom, cleanName, false, null, true);
                _settings.Actions.Add(created);
                RebuildActiveSnapshotLocked();
                SaveLocked();
            }

            RaiseStateChanged();
            return created.Clone();
        }

        public bool RemoveCustomAction(string actionId)
        {
            bool removed = false;
            lock (_sync)
            {
                for (int i = 0; i < _settings.Actions.Count; i++)
                {
                    if (_settings.Actions[i].IsCustom &&
                        string.Equals(_settings.Actions[i].Id, actionId, StringComparison.OrdinalIgnoreCase))
                    {
                        _settings.Actions.RemoveAt(i);
                        removed = true;
                        RebuildActiveSnapshotLocked();
                        SaveLocked();
                        break;
                    }
                }
            }

            if (removed)
            {
                RaiseStateChanged();
            }
            return removed;
        }

        public void ResetActionsToDefaults()
        {
            lock (_sync)
            {
                _settings.Actions = ActionBindingItem.CreateDefaultNightreignActions();
                // Ensure default enabled dodge action does not conflict with a customized ToggleHotkey
                ActionBindingItem dodge = _settings.FindAction(ActionBindingItem.DodgeActionId);
                if (dodge != null && dodge.Binding != null && dodge.Binding.Equals(_settings.ToggleHotkey))
                {
                    dodge.Enabled = false;
                }
                RebuildActiveSnapshotLocked();
                SaveLocked();
            }

            RaiseStateChanged();
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
                for (int i = 0; i < _settings.Actions.Count; i++)
                {
                    ActionBindingItem item = _settings.Actions[i];
                    if (item.Enabled && item.Binding != null && item.Binding.Equals(newHotkey))
                    {
                        errorMessage = "활성화된 '" + item.Name + "' 키와 같은 키는 ON/OFF 단축키로 지정할 수 없습니다.";
                        return false;
                    }
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

        public bool ShouldInterceptForProcess(string foregroundProcessFileName)
        {
            lock (_sync)
            {
                if (!_settings.MasterEnabled)
                {
                    return false;
                }
                if (!_settings.AutoEnableOnGame)
                {
                    return true;
                }
                if (string.IsNullOrEmpty(foregroundProcessFileName))
                {
                    return false;
                }
                return _targetProcessCache.Contains(foregroundProcessFileName.Trim());
            }
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

        public bool NeedsMouseHook(bool isCapturingActionKey)
        {
            if (isCapturingActionKey)
            {
                return true;
            }
            if (!MasterEnabled)
            {
                return false;
            }
            ActiveHookSnapshot snap = _activeSnapshot;
            return snap != null && snap.HasAnyMouseButton;
        }

        private ActionBindingItem FindOtherActiveActionWithBindingLocked(string excludeActionId, KeyBinding binding)
        {
            for (int i = 0; i < _settings.Actions.Count; i++)
            {
                ActionBindingItem other = _settings.Actions[i];
                if (string.Equals(other.Id, excludeActionId, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                if (other.Enabled && other.Binding != null && other.Binding.Equals(binding))
                {
                    return other;
                }
            }
            return null;
        }

        private void RebuildActiveSnapshotLocked()
        {
            Dictionary<uint, KeyBinding> kbd = new Dictionary<uint, KeyBinding>();
            HashSet<MouseBindButton> mouse = new HashSet<MouseBindButton>();

            for (int i = 0; i < _settings.Actions.Count; i++)
            {
                ActionBindingItem item = _settings.Actions[i];
                if (!item.Enabled || item.Binding == null)
                {
                    continue;
                }

                if (item.Binding.Kind == BindingKind.Keyboard)
                {
                    uint composite = item.Binding.GetKeyboardCompositeId();
                    if (!kbd.ContainsKey(composite))
                    {
                        kbd.Add(composite, item.Binding);
                    }
                }
                else if (item.Binding.Kind == BindingKind.Mouse)
                {
                    mouse.Add(item.Binding.MouseButton);
                }
            }

            _activeSnapshot = new ActiveHookSnapshot(kbd, mouse);
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
