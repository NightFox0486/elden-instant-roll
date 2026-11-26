using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace EldenInstantRoll
{
    public enum CaptureMode
    {
        None = 0,
        RollKey = 1,
        ToggleHotkey = 2
    }

    public sealed class KeyCapturedEventArgs : EventArgs
    {
        public CaptureMode Mode { get; private set; }
        public KeyBinding CapturedBinding { get; private set; }
        public bool Cancelled { get; private set; }

        public KeyCapturedEventArgs(CaptureMode mode, KeyBinding binding, bool cancelled)
        {
            Mode = mode;
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
        private bool _rollKeyHeld;
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

        public void Start()
        {
            InstallKeyboardHook();
            RefreshMouseHookState();
        }

        public void BeginCapture(CaptureMode mode)
        {
            _captureMode = mode;
            _rollKeyHeld = false;
            _toggleHotkeyHeld = false;
            RefreshMouseHookState();
        }

        public void CancelCapture()
        {
            if (_captureMode != CaptureMode.None)
            {
                CaptureMode previous = _captureMode;
                _captureMode = CaptureMode.None;
                RefreshMouseHookState();
                RaiseKeyCaptured(previous, null, true);
            }
        }

        public void RefreshMouseHookState()
        {
            bool needMouse = _controller.NeedsMouseHook(_captureMode == CaptureMode.RollKey);
            if (needMouse)
            {
                InstallMouseHook();
            }
            else
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
            _rollKeyHeld = false;
            _toggleHotkeyHeld = false;
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

                    // 1) Key capture mode (Roll Key or Toggle Hotkey)
                    CaptureMode capMode = _captureMode;
                    if (capMode != CaptureMode.None)
                    {
                        if (isDown)
                        {
                            _captureMode = CaptureMode.None;
                            // ESC (sc001) cancels key capture
                            if (scanCode == 0x01 && !isExtended)
                            {
                                RaiseKeyCaptured(capMode, null, true);
                            }
                            else if (scanCode != 0)
                            {
                                KeyBinding captured = KeyBinding.FromKeyboard(scanCode, isExtended, null);
                                RaiseKeyCaptured(capMode, captured, false);
                            }
                            else
                            {
                                // Unknown zero scanCode: restore capture mode
                                _captureMode = capMode;
                            }
                        }
                        // Suppress key while capturing
                        return (IntPtr)1;
                    }

                    // 2) Global Master ON/OFF Toggle Hotkey (always active even when Master is OFF)
                    KeyBinding toggleKey = _controller.ToggleHotkey;
                    if (toggleKey != null && toggleKey.MatchesKeyboard(scanCode, isExtended))
                    {
                        if (isDown)
                        {
                            if (!_toggleHotkeyHeld)
                            {
                                _toggleHotkeyHeld = true;
                                RaiseToggleHotkeyPressed();
                            }
                        }
                        else if (isUp)
                        {
                            _toggleHotkeyHeld = false;
                        }
                        return (IntPtr)1;
                    }

                    // 3) Instant Roll key handling (keyboard)
                    KeyBinding rollKey = _controller.RollKey;
                    if (rollKey != null && rollKey.MatchesKeyboard(scanCode, isExtended))
                    {
                        if (isDown)
                        {
                            if (_rollKeyHeld)
                            {
                                // Suppress Windows key auto-repeat while physically holding the roll key
                                return (IntPtr)1;
                            }

                            if (_controller.MasterEnabled && ShouldInterceptNow())
                            {
                                _rollKeyHeld = true;
                                DispatchKeyboardTapAsync(rollKey.ScanCode, rollKey.IsExtended, _controller.TapDelayMs);
                                return (IntPtr)1;
                            }
                        }
                        else if (isUp)
                        {
                            if (_rollKeyHeld)
                            {
                                _rollKeyHeld = false;
                                // Consume physical key-up because our synthetic tap already sent key-up
                                return (IntPtr)1;
                            }
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

                if (msg == NativeMethods.WM_MBUTTONDOWN)
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
                    NativeMethods.MSLLHOOKSTRUCT ms = (NativeMethods.MSLLHOOKSTRUCT)Marshal.PtrToStructure(
                        lParam,
                        typeof(NativeMethods.MSLLHOOKSTRUCT));

                    if (ms.dwExtraInfo == NativeMethods.MAGIC_EXTRA_INFO)
                    {
                        return NativeMethods.CallNextHookEx(_mouseHook, nCode, wParam, lParam);
                    }

                    uint xButton = (ms.mouseData >> 16) & 0xFFFF;
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
                    // For Middle button, still verify MAGIC_EXTRA_INFO
                    if (button == MouseBindButton.Middle)
                    {
                        NativeMethods.MSLLHOOKSTRUCT ms = (NativeMethods.MSLLHOOKSTRUCT)Marshal.PtrToStructure(
                            lParam,
                            typeof(NativeMethods.MSLLHOOKSTRUCT));
                        if (ms.dwExtraInfo == NativeMethods.MAGIC_EXTRA_INFO)
                        {
                            return NativeMethods.CallNextHookEx(_mouseHook, nCode, wParam, lParam);
                        }
                    }

                    // 1) Roll key capture mode accepts Mouse4 / Mouse5 / Middle
                    if (_captureMode == CaptureMode.RollKey)
                    {
                        if (isDown)
                        {
                            _captureMode = CaptureMode.None;
                            RaiseKeyCaptured(CaptureMode.RollKey, KeyBinding.FromMouse(button), false);
                        }
                        return (IntPtr)1;
                    }

                    // 2) Instant Roll key handling (mouse)
                    KeyBinding rollKey = _controller.RollKey;
                    if (rollKey != null && rollKey.MatchesMouse(button))
                    {
                        if (isDown)
                        {
                            if (_rollKeyHeld)
                            {
                                return (IntPtr)1;
                            }

                            if (_controller.MasterEnabled && ShouldInterceptNow())
                            {
                                _rollKeyHeld = true;
                                DispatchMouseTapAsync(button, _controller.TapDelayMs);
                                return (IntPtr)1;
                            }
                        }
                        else if (isUp)
                        {
                            if (_rollKeyHeld)
                            {
                                _rollKeyHeld = false;
                                return (IntPtr)1;
                            }
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
            return _controller.ShouldInterceptRollForProcess(fgProc);
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

            if (button == MouseBindButton.Middle)
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

        private void RaiseKeyCaptured(CaptureMode mode, KeyBinding binding, bool cancelled)
        {
            EventHandler<KeyCapturedEventArgs> handler = KeyCaptured;
            if (handler != null)
            {
                handler(this, new KeyCapturedEventArgs(mode, binding, cancelled));
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
