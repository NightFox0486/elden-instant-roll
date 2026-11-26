using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace EldenInstantRoll
{
    public enum CaptureMode
    {
        None = 0,
        ActionKey = 1,
        ToggleHotkey = 2
    }

    public sealed class KeyCapturedEventArgs : EventArgs
    {
        public CaptureMode Mode { get; private set; }
        public string TargetActionId { get; private set; }
        public KeyBinding CapturedBinding { get; private set; }
        public bool Cancelled { get; private set; }

        public KeyCapturedEventArgs(CaptureMode mode, string targetActionId, KeyBinding binding, bool cancelled)
        {
            Mode = mode;
            TargetActionId = targetActionId;
            CapturedBinding = binding;
            Cancelled = cancelled;
        }
    }

    public sealed class InputHookEngine : IDisposable
    {
        private readonly AppController _controller;

        // Keep delegate references alive so GC never collects active Win32 hook callbacks
        private readonly NativeMethods.LowLevelHookProc _keyboardProc;
        private readonly NativeMethods.LowLevelHookProc _mouseProc;

        private IntPtr _keyboardHook = IntPtr.Zero;
        private IntPtr _mouseHook = IntPtr.Zero;

        private volatile CaptureMode _captureMode = CaptureMode.None;
        private volatile string _captureTargetActionId;

        // Independent per-key held state tracking for multi-key simultaneous operation
        private readonly object _heldLock = new object();
        private readonly HashSet<uint> _heldKeyboardKeys = new HashSet<uint>();
        private readonly HashSet<MouseBindButton> _heldMouseButtons = new HashSet<MouseBindButton>();
        private MouseBindButton _suppressNextMouseUp = MouseBindButton.None;
        private bool _toggleHotkeyHeld;

        // Cached foreground HWND -> process name for ultra-fast hook checks
        private IntPtr _cachedForegroundHwnd = IntPtr.Zero;
        private string _cachedForegroundProcess = string.Empty;

        private bool _disposed;

        public event EventHandler<KeyCapturedEventArgs> KeyCaptured;
        public event EventHandler ToggleHotkeyPressed;

        public InputHookEngine(AppController controller)
        {
            if (controller == null) throw new ArgumentNullException("controller");
            _controller = controller;
            _keyboardProc = KeyboardHookCallback;
            _mouseProc = MouseHookCallback;
        }

        public CaptureMode CurrentCaptureMode
        {
            get { return _captureMode; }
        }

        public string CaptureTargetActionId
        {
            get { return _captureTargetActionId; }
        }

        public bool IsMouseHookInstalled
        {
            get { return _mouseHook != IntPtr.Zero; }
        }

        public void Start()
        {
            InstallKeyboardHook();
            RefreshMouseHookState();
        }

        public void BeginActionKeyCapture(string actionId)
        {
            _captureTargetActionId = actionId;
            _captureMode = CaptureMode.ActionKey;
            ResetHeldStates();
            RefreshMouseHookState();
        }

        public void BeginToggleHotkeyCapture()
        {
            _captureTargetActionId = null;
            _captureMode = CaptureMode.ToggleHotkey;
            ResetHeldStates();
            RefreshMouseHookState();
        }

        public void CancelCapture()
        {
            if (_captureMode != CaptureMode.None)
            {
                CaptureMode previous = _captureMode;
                string prevAction = _captureTargetActionId;
                _captureMode = CaptureMode.None;
                _captureTargetActionId = null;
                RefreshMouseHookState();
                RaiseKeyCaptured(previous, prevAction, null, true);
            }
        }

        public void RefreshMouseHookState()
        {
            bool needMouse = _controller.NeedsMouseHook(_captureMode == CaptureMode.ActionKey);
            if (needMouse)
            {
                InstallMouseHook();
            }
            else if (_suppressNextMouseUp == MouseBindButton.None)
            {
                UninstallMouseHook();
            }
        }

        public string RefreshForegroundProcess()
        {
            IntPtr hwnd = NativeMethods.GetForegroundWindow();
            if (hwnd != _cachedForegroundHwnd)
            {
                _cachedForegroundHwnd = hwnd;
                _cachedForegroundProcess = NativeMethods.GetProcessFileNameFromHwnd(hwnd);
            }
            _controller.UpdateForegroundProcess(_cachedForegroundProcess);
            return _cachedForegroundProcess;
        }

        public void ResetHeldStates()
        {
            lock (_heldLock)
            {
                _heldKeyboardKeys.Clear();
                _heldMouseButtons.Clear();
                _toggleHotkeyHeld = false;
            }
        }

        private void InstallKeyboardHook()
        {
            if (_keyboardHook != IntPtr.Zero)
            {
                return;
            }
            using (Process curProcess = Process.GetCurrentProcess())
            using (ProcessModule curModule = curProcess.MainModule)
            {
                IntPtr hMod = NativeMethods.GetModuleHandle(curModule != null ? curModule.ModuleName : null);
                _keyboardHook = NativeMethods.SetWindowsHookEx(NativeMethods.WH_KEYBOARD_LL, _keyboardProc, hMod, 0);
            }
        }

        private void InstallMouseHook()
        {
            if (_mouseHook != IntPtr.Zero)
            {
                return;
            }
            using (Process curProcess = Process.GetCurrentProcess())
            using (ProcessModule curModule = curProcess.MainModule)
            {
                IntPtr hMod = NativeMethods.GetModuleHandle(curModule != null ? curModule.ModuleName : null);
                _mouseHook = NativeMethods.SetWindowsHookEx(NativeMethods.WH_MOUSE_LL, _mouseProc, hMod, 0);
            }
        }

        private void UninstallMouseHook()
        {
            if (_mouseHook != IntPtr.Zero)
            {
                NativeMethods.UnhookWindowsHookEx(_mouseHook);
                _mouseHook = IntPtr.Zero;
            }
        }

        private IntPtr KeyboardHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                int msg = wParam.ToInt32();
                bool isDown = (msg == NativeMethods.WM_KEYDOWN || msg == NativeMethods.WM_SYSKEYDOWN);
                bool isUp = (msg == NativeMethods.WM_KEYUP || msg == NativeMethods.WM_SYSKEYUP);

                if (isDown || isUp)
                {
                    NativeMethods.KBDLLHOOKSTRUCT kbd = (NativeMethods.KBDLLHOOKSTRUCT)Marshal.PtrToStructure(
                        lParam,
                        typeof(NativeMethods.KBDLLHOOKSTRUCT));

                    // Ignore our own synthetic SendInput events to completely prevent hook loops
                    if (kbd.dwExtraInfo == NativeMethods.MAGIC_EXTRA_INFO)
                    {
                        return NativeMethods.CallNextHookEx(_keyboardHook, nCode, wParam, lParam);
                    }

                    ushort scanCode = (ushort)(kbd.scanCode & 0xFFFF);
                    bool isExtended = (kbd.flags & NativeMethods.LLKHF_EXTENDED) != 0;

                    // 1) Key capture mode (Action Key or Toggle Hotkey)
                    CaptureMode capMode = _captureMode;
                    if (capMode != CaptureMode.None)
                    {
                        if (isDown)
                        {
                            string targetAction = _captureTargetActionId;
                            _captureMode = CaptureMode.None;
                            _captureTargetActionId = null;

                            // ESC (sc001) cancels key capture
                            if (scanCode == 0x01 && !isExtended)
                            {
                                RaiseKeyCaptured(capMode, targetAction, null, true);
                            }
                            else if (scanCode != 0)
                            {
                                KeyBinding captured = KeyBinding.FromKeyboard(scanCode, isExtended, null);
                                RaiseKeyCaptured(capMode, targetAction, captured, false);
                            }
                            else
                            {
                                // Unknown zero scanCode: keep waiting
                                _captureMode = capMode;
                                _captureTargetActionId = targetAction;
                            }
                        }
                        return (IntPtr)1;
                    }

                    // 2) Global Master ON/OFF Toggle Hotkey (always active even when Master is OFF)
                    KeyBinding toggleKey = _controller.ToggleHotkey;
                    if (toggleKey != null && toggleKey.MatchesKeyboard(scanCode, isExtended))
                    {
                        if (isDown)
                        {
                            bool trigger = false;
                            lock (_heldLock)
                            {
                                if (!_toggleHotkeyHeld)
                                {
                                    _toggleHotkeyHeld = true;
                                    trigger = true;
                                }
                            }
                            if (trigger)
                            {
                                RaiseToggleHotkeyPressed();
                            }
                        }
                        else if (isUp)
                        {
                            lock (_heldLock)
                            {
                                _toggleHotkeyHeld = false;
                            }
                        }
                        return (IntPtr)1;
                    }

                    // 3) Multi-key Instant Tap handling (ONLY checked/active keyboard keys!)
                    uint compositeId = KeyBinding.MakeKeyboardCompositeId(scanCode, isExtended);
                    if (isDown)
                    {
                        lock (_heldLock)
                        {
                            if (_heldKeyboardKeys.Contains(compositeId))
                            {
                                // Suppress Windows key auto-repeat while physically holding this key
                                return (IntPtr)1;
                            }
                        }

                        if (_controller.MasterEnabled)
                        {
                            ActiveHookSnapshot snap = _controller.GetActiveHookSnapshot();
                            if (snap != null && snap.KeyboardBindings.ContainsKey(compositeId) && ShouldInterceptNow())
                            {
                                lock (_heldLock)
                                {
                                    _heldKeyboardKeys.Add(compositeId);
                                }
                                DispatchKeyboardTapAsync(scanCode, isExtended, _controller.TapDelayMs);
                                return (IntPtr)1;
                            }
                        }
                    }
                    else if (isUp)
                    {
                        bool wasHeld;
                        lock (_heldLock)
                        {
                            wasHeld = _heldKeyboardKeys.Remove(compositeId);
                        }
                        if (wasHeld)
                        {
                            // Consume physical key-up because our synthetic tap already sent key-up
                            return (IntPtr)1;
                        }
                    }
                }
            }

            return NativeMethods.CallNextHookEx(_keyboardHook, nCode, wParam, lParam);
        }

        private IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                int msg = wParam.ToInt32();

                // Fast-path: immediately ignore mouse movement before any struct marshaling
                if (msg == NativeMethods.WM_MOUSEMOVE)
                {
                    return NativeMethods.CallNextHookEx(_mouseHook, nCode, wParam, lParam);
                }

                bool isDown = false;
                bool isUp = false;
                MouseBindButton button = MouseBindButton.None;

                if (msg == NativeMethods.WM_LBUTTONDOWN)
                {
                    isDown = true;
                    button = MouseBindButton.MouseLeft;
                }
                else if (msg == NativeMethods.WM_LBUTTONUP)
                {
                    isUp = true;
                    button = MouseBindButton.MouseLeft;
                }
                else if (msg == NativeMethods.WM_RBUTTONDOWN)
                {
                    isDown = true;
                    button = MouseBindButton.MouseRight;
                }
                else if (msg == NativeMethods.WM_RBUTTONUP)
                {
                    isUp = true;
                    button = MouseBindButton.MouseRight;
                }
                else if (msg == NativeMethods.WM_MBUTTONDOWN)
                {
                    isDown = true;
                    button = MouseBindButton.Middle;
                }
                else if (msg == NativeMethods.WM_MBUTTONUP)
                {
                    isUp = true;
                    button = MouseBindButton.Middle;
                }
                else if (msg == NativeMethods.WM_XBUTTONDOWN || msg == NativeMethods.WM_XBUTTONUP)
                {
                    NativeMethods.MSLLHOOKSTRUCT msX = (NativeMethods.MSLLHOOKSTRUCT)Marshal.PtrToStructure(
                        lParam,
                        typeof(NativeMethods.MSLLHOOKSTRUCT));

                    if (msX.dwExtraInfo == NativeMethods.MAGIC_EXTRA_INFO)
                    {
                        return NativeMethods.CallNextHookEx(_mouseHook, nCode, wParam, lParam);
                    }

                    uint xButton = (msX.mouseData >> 16) & 0xFFFF;
                    if (xButton == NativeMethods.XBUTTON1)
                    {
                        button = MouseBindButton.Mouse4;
                    }
                    else if (xButton == NativeMethods.XBUTTON2)
                    {
                        button = MouseBindButton.Mouse5;
                    }

                    isDown = (msg == NativeMethods.WM_XBUTTONDOWN);
                    isUp = (msg == NativeMethods.WM_XBUTTONUP);
                }

                if (button != MouseBindButton.None)
                {
                    // For non-X buttons, verify MAGIC_EXTRA_INFO before processing
                    if (button != MouseBindButton.Mouse4 && button != MouseBindButton.Mouse5)
                    {
                        NativeMethods.MSLLHOOKSTRUCT ms = (NativeMethods.MSLLHOOKSTRUCT)Marshal.PtrToStructure(
                            lParam,
                            typeof(NativeMethods.MSLLHOOKSTRUCT));
                        if (ms.dwExtraInfo == NativeMethods.MAGIC_EXTRA_INFO)
                        {
                            return NativeMethods.CallNextHookEx(_mouseHook, nCode, wParam, lParam);
                        }
                    }

                    // Consume trailing button-up right after capturing a mouse button
                    if (isUp && _suppressNextMouseUp == button)
                    {
                        _suppressNextMouseUp = MouseBindButton.None;
                        RefreshMouseHookState();
                        return (IntPtr)1;
                    }

                    // 1) Action key capture mode accepts MouseLeft / MouseRight / Middle / Mouse4 / Mouse5
                    if (_captureMode == CaptureMode.ActionKey)
                    {
                        if (isDown)
                        {
                            string targetAction = _captureTargetActionId;
                            _captureMode = CaptureMode.None;
                            _captureTargetActionId = null;
                            _suppressNextMouseUp = button;
                            RaiseKeyCaptured(CaptureMode.ActionKey, targetAction, KeyBinding.FromMouse(button), false);
                        }
                        return (IntPtr)1;
                    }

                    // 2) Multi-key Instant Tap handling (ONLY checked/active mouse buttons!)
                    if (isDown)
                    {
                        lock (_heldLock)
                        {
                            if (_heldMouseButtons.Contains(button))
                            {
                                return (IntPtr)1;
                            }
                        }

                        if (_controller.MasterEnabled)
                        {
                            ActiveHookSnapshot snap = _controller.GetActiveHookSnapshot();
                            if (snap != null && snap.MouseButtons.Contains(button) && ShouldInterceptNow())
                            {
                                lock (_heldLock)
                                {
                                    _heldMouseButtons.Add(button);
                                }
                                DispatchMouseTapAsync(button, _controller.TapDelayMs);
                                return (IntPtr)1;
                            }
                        }
                    }
                    else if (isUp)
                    {
                        bool wasHeld;
                        lock (_heldLock)
                        {
                            wasHeld = _heldMouseButtons.Remove(button);
                        }
                        if (wasHeld)
                        {
                            return (IntPtr)1;
                        }
                    }
                }
            }

            return NativeMethods.CallNextHookEx(_mouseHook, nCode, wParam, lParam);
        }

        private bool ShouldInterceptNow()
        {
            if (!_controller.AutoEnableOnGame)
            {
                return true;
            }

            string fgProc = RefreshForegroundProcess();
            return _controller.ShouldInterceptForProcess(fgProc);
        }

        private static void DispatchKeyboardTapAsync(ushort scanCode, bool isExtended, int delayMs)
        {
            ThreadPool.QueueUserWorkItem(delegate
            {
                SendKeyboardTap(scanCode, isExtended, delayMs);
            });
        }

        private static void DispatchMouseTapAsync(MouseBindButton button, int delayMs)
        {
            ThreadPool.QueueUserWorkItem(delegate
            {
                SendMouseTap(button, delayMs);
            });
        }

        private static void SendKeyboardTap(ushort scanCode, bool isExtended, int delayMs)
        {
            uint baseFlags = NativeMethods.KEYEVENTF_SCANCODE | (isExtended ? NativeMethods.KEYEVENTF_EXTENDEDKEY : 0u);

            NativeMethods.INPUT[] downInput = new NativeMethods.INPUT[1];
            downInput[0].type = NativeMethods.INPUT_KEYBOARD;
            downInput[0].U.ki.wVk = 0;
            downInput[0].U.ki.wScan = scanCode;
            downInput[0].U.ki.dwFlags = baseFlags;
            downInput[0].U.ki.time = 0;
            downInput[0].U.ki.dwExtraInfo = NativeMethods.MAGIC_EXTRA_INFO;

            NativeMethods.INPUT[] upInput = new NativeMethods.INPUT[1];
            upInput[0].type = NativeMethods.INPUT_KEYBOARD;
            upInput[0].U.ki.wVk = 0;
            upInput[0].U.ki.wScan = scanCode;
            upInput[0].U.ki.dwFlags = baseFlags | NativeMethods.KEYEVENTF_KEYUP;
            upInput[0].U.ki.time = 0;
            upInput[0].U.ki.dwExtraInfo = NativeMethods.MAGIC_EXTRA_INFO;

            int cbSize = Marshal.SizeOf(typeof(NativeMethods.INPUT));

            NativeMethods.timeBeginPeriod(1);
            try
            {
                NativeMethods.SendInput(1, downInput, cbSize);
                Thread.Sleep(delayMs);
                NativeMethods.SendInput(1, upInput, cbSize);
            }
            finally
            {
                NativeMethods.timeEndPeriod(1);
            }
        }

        private static void SendMouseTap(MouseBindButton button, int delayMs)
        {
            uint downFlag;
            uint upFlag;
            uint mouseData = 0;

            if (button == MouseBindButton.MouseLeft)
            {
                downFlag = NativeMethods.MOUSEEVENTF_LEFTDOWN;
                upFlag = NativeMethods.MOUSEEVENTF_LEFTUP;
            }
            else if (button == MouseBindButton.MouseRight)
            {
                downFlag = NativeMethods.MOUSEEVENTF_RIGHTDOWN;
                upFlag = NativeMethods.MOUSEEVENTF_RIGHTUP;
            }
            else if (button == MouseBindButton.Middle)
            {
                downFlag = NativeMethods.MOUSEEVENTF_MIDDLEDOWN;
                upFlag = NativeMethods.MOUSEEVENTF_MIDDLEUP;
            }
            else if (button == MouseBindButton.Mouse4)
            {
                downFlag = NativeMethods.MOUSEEVENTF_XDOWN;
                upFlag = NativeMethods.MOUSEEVENTF_XUP;
                mouseData = NativeMethods.XBUTTON1;
            }
            else if (button == MouseBindButton.Mouse5)
            {
                downFlag = NativeMethods.MOUSEEVENTF_XDOWN;
                upFlag = NativeMethods.MOUSEEVENTF_XUP;
                mouseData = NativeMethods.XBUTTON2;
            }
            else
            {
                return;
            }

            NativeMethods.INPUT[] downInput = new NativeMethods.INPUT[1];
            downInput[0].type = NativeMethods.INPUT_MOUSE;
            downInput[0].U.mi.mouseData = mouseData;
            downInput[0].U.mi.dwFlags = downFlag;
            downInput[0].U.mi.dwExtraInfo = NativeMethods.MAGIC_EXTRA_INFO;

            NativeMethods.INPUT[] upInput = new NativeMethods.INPUT[1];
            upInput[0].type = NativeMethods.INPUT_MOUSE;
            upInput[0].U.mi.mouseData = mouseData;
            upInput[0].U.mi.dwFlags = upFlag;
            upInput[0].U.mi.dwExtraInfo = NativeMethods.MAGIC_EXTRA_INFO;

            int cbSize = Marshal.SizeOf(typeof(NativeMethods.INPUT));

            NativeMethods.timeBeginPeriod(1);
            try
            {
                NativeMethods.SendInput(1, downInput, cbSize);
                Thread.Sleep(delayMs);
                NativeMethods.SendInput(1, upInput, cbSize);
            }
            finally
            {
                NativeMethods.timeEndPeriod(1);
            }
        }

        private void RaiseKeyCaptured(CaptureMode mode, string targetActionId, KeyBinding binding, bool cancelled)
        {
            EventHandler<KeyCapturedEventArgs> handler = KeyCaptured;
            if (handler != null)
            {
                handler(this, new KeyCapturedEventArgs(mode, targetActionId, binding, cancelled));
            }
        }

        private void RaiseToggleHotkeyPressed()
        {
            EventHandler handler = ToggleHotkeyPressed;
            if (handler != null)
            {
                handler(this, EventArgs.Empty);
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;

            if (_keyboardHook != IntPtr.Zero)
            {
                NativeMethods.UnhookWindowsHookEx(_keyboardHook);
                _keyboardHook = IntPtr.Zero;
            }
            UninstallMouseHook();
        }
    }
}
