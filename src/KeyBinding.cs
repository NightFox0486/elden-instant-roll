using System;
using System.Globalization;
using System.Text;

namespace EldenInstantRoll
{
    public enum BindingKind
    {
        Keyboard = 0,
        Mouse = 1
    }

    public enum MouseBindButton
    {
        None = 0,
        Mouse4 = 1,
        Mouse5 = 2,
        Middle = 3
    }

    public sealed class KeyBinding : IEquatable<KeyBinding>
    {
        public BindingKind Kind { get; private set; }
        public ushort ScanCode { get; private set; }
        public bool IsExtended { get; private set; }
        public MouseBindButton MouseButton { get; private set; }
        public string DisplayName { get; private set; }

        private KeyBinding(BindingKind kind, ushort scanCode, bool isExtended, MouseBindButton mouseButton, string displayName)
        {
            Kind = kind;
            ScanCode = scanCode;
            IsExtended = isExtended;
            MouseButton = mouseButton;
            DisplayName = string.IsNullOrEmpty(displayName) ? BuildDefaultDisplayName(kind, scanCode, isExtended, mouseButton) : displayName;
        }

        public static KeyBinding FromKeyboard(ushort scanCode, bool isExtended, string displayName)
        {
            string resolved = string.IsNullOrEmpty(displayName)
                ? ResolveKeyboardDisplayName(scanCode, isExtended)
                : displayName;
            return new KeyBinding(BindingKind.Keyboard, scanCode, isExtended, MouseBindButton.None, resolved);
        }

        public static KeyBinding FromMouse(MouseBindButton button)
        {
            if (button == MouseBindButton.None)
            {
                throw new ArgumentException("Mouse button cannot be None.", "button");
            }
            return new KeyBinding(BindingKind.Mouse, 0, false, button, FormatMouseButtonName(button));
        }

        public static KeyBinding DefaultRollKey()
        {
            // Physical Q key (scan code 0x10)
            return FromKeyboard(0x10, false, "Q");
        }

        public static KeyBinding DefaultToggleHotkey()
        {
            // Physical F8 key (scan code 0x42)
            return FromKeyboard(0x42, false, "F8");
        }

        public string FormatBadgeText()
        {
            if (Kind == BindingKind.Mouse)
            {
                return DisplayName;
            }
            string extSuffix = IsExtended ? "E" : string.Empty;
            return string.Format(CultureInfo.InvariantCulture, "{0} (sc{1:X3}{2})", DisplayName, ScanCode, extSuffix);
        }

        public string FormatShortText()
        {
            return DisplayName;
        }

        public string Serialize()
        {
            if (Kind == BindingKind.Mouse)
            {
                return "MOUSE:" + MouseButton.ToString();
            }
            string safeName = (DisplayName ?? string.Empty).Replace("|", " ").Trim();
            return string.Format(
                CultureInfo.InvariantCulture,
                "KEY:{0:X4}:{1}|{2}",
                ScanCode,
                IsExtended ? "1" : "0",
                safeName);
        }

        public static bool TryParse(string value, out KeyBinding binding)
        {
            binding = null;
            if (string.IsNullOrEmpty(value))
            {
                return false;
            }

            string trimmed = value.Trim();
            if (trimmed.StartsWith("MOUSE:", StringComparison.OrdinalIgnoreCase))
            {
                string buttonText = trimmed.Substring(6).Trim();
                MouseBindButton btn;
                if (Enum.TryParse<MouseBindButton>(buttonText, true, out btn) && btn != MouseBindButton.None)
                {
                    binding = FromMouse(btn);
                    return true;
                }
                return false;
            }

            if (trimmed.StartsWith("KEY:", StringComparison.OrdinalIgnoreCase))
            {
                string payload = trimmed.Substring(4);
                string label = null;
                int pipe = payload.IndexOf('|');
                if (pipe >= 0)
                {
                    label = payload.Substring(pipe + 1).Trim();
                    payload = payload.Substring(0, pipe);
                }

                string[] parts = payload.Split(':');
                if (parts.Length >= 1)
                {
                    ushort scanCode;
                    if (ushort.TryParse(parts[0], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out scanCode) && scanCode > 0)
                    {
                        bool isExt = parts.Length >= 2 && parts[1] == "1";
                        binding = FromKeyboard(scanCode, isExt, label);
                        return true;
                    }
                }
            }

            return false;
        }

        public bool MatchesKeyboard(uint scanCode, bool isExtended)
        {
            return Kind == BindingKind.Keyboard && ScanCode == (ushort)scanCode && IsExtended == isExtended;
        }

        public bool MatchesMouse(MouseBindButton button)
        {
            return Kind == BindingKind.Mouse && MouseButton == button;
        }

        public bool Equals(KeyBinding other)
        {
            if (ReferenceEquals(other, null))
            {
                return false;
            }
            if (Kind != other.Kind)
            {
                return false;
            }
            if (Kind == BindingKind.Mouse)
            {
                return MouseButton == other.MouseButton;
            }
            return ScanCode == other.ScanCode && IsExtended == other.IsExtended;
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as KeyBinding);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = (int)Kind * 397;
                if (Kind == BindingKind.Mouse)
                {
                    return hash ^ (int)MouseButton;
                }
                return hash ^ (ScanCode << 1) ^ (IsExtended ? 1 : 0);
            }
        }

        public override string ToString()
        {
            return FormatBadgeText();
        }

        private static string BuildDefaultDisplayName(BindingKind kind, ushort scanCode, bool isExtended, MouseBindButton mouseButton)
        {
            if (kind == BindingKind.Mouse)
            {
                return FormatMouseButtonName(mouseButton);
            }
            return ResolveKeyboardDisplayName(scanCode, isExtended);
        }

        private static string FormatMouseButtonName(MouseBindButton button)
        {
            switch (button)
            {
                case MouseBindButton.Mouse4:
                    return "Mouse4 (뒤로)";
                case MouseBindButton.Mouse5:
                    return "Mouse5 (앞으로)";
                case MouseBindButton.Middle:
                    return "Mouse Middle";
                default:
                    return "Mouse";
            }
        }

        public static string ResolveKeyboardDisplayName(ushort scanCode, bool isExtended)
        {
            // Explicit friendly labels for common keys so names stay consistent regardless of Windows locale
            if (!isExtended)
            {
                switch (scanCode)
                {
                    case 0x01: return "ESC";
                    case 0x0E: return "Backspace";
                    case 0x0F: return "Tab";
                    case 0x10: return "Q";
                    case 0x11: return "W";
                    case 0x12: return "E";
                    case 0x13: return "R";
                    case 0x14: return "T";
                    case 0x15: return "Y";
                    case 0x16: return "U";
                    case 0x17: return "I";
                    case 0x18: return "O";
                    case 0x19: return "P";
                    case 0x1C: return "Enter";
                    case 0x1D: return "LCtrl";
                    case 0x1E: return "A";
                    case 0x1F: return "S";
                    case 0x20: return "D";
                    case 0x21: return "F";
                    case 0x22: return "G";
                    case 0x23: return "H";
                    case 0x24: return "J";
                    case 0x25: return "K";
                    case 0x26: return "L";
                    case 0x2A: return "LShift";
                    case 0x2C: return "Z";
                    case 0x2D: return "X";
                    case 0x2E: return "C";
                    case 0x2F: return "V";
                    case 0x30: return "B";
                    case 0x31: return "N";
                    case 0x32: return "M";
                    case 0x36: return "RShift";
                    case 0x38: return "LAlt";
                    case 0x39: return "Space";
                    case 0x3A: return "CapsLock";
                    case 0x3B: return "F1";
                    case 0x3C: return "F2";
                    case 0x3D: return "F3";
                    case 0x3E: return "F4";
                    case 0x3F: return "F5";
                    case 0x40: return "F6";
                    case 0x41: return "F7";
                    case 0x42: return "F8";
                    case 0x43: return "F9";
                    case 0x44: return "F10";
                    case 0x57: return "F11";
                    case 0x58: return "F12";
                }
            }
            else
            {
                switch (scanCode)
                {
                    case 0x1C: return "NumEnter";
                    case 0x1D: return "RCtrl";
                    case 0x35: return "Num/";
                    case 0x38: return "RAlt";
                    case 0x47: return "Home";
                    case 0x48: return "Up";
                    case 0x49: return "PageUp";
                    case 0x4B: return "Left";
                    case 0x4D: return "Right";
                    case 0x4F: return "End";
                    case 0x50: return "Down";
                    case 0x51: return "PageDown";
                    case 0x52: return "Insert";
                    case 0x53: return "Delete";
                }
            }

            try
            {
                int lParam = (scanCode << 16) | (isExtended ? (1 << 24) : 0);
                StringBuilder sb = new StringBuilder(64);
                int len = NativeMethods.GetKeyNameText(lParam, sb, sb.Capacity);
                if (len > 0)
                {
                    return sb.ToString().Trim();
                }
            }
            catch
            {
                // Fallback below
            }

            return string.Format(CultureInfo.InvariantCulture, "Key 0x{0:X2}", scanCode);
        }
    }
}
