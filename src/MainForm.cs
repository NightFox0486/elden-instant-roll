using System;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Windows.Forms;

namespace EldenInstantRoll
{
    public sealed class MainForm : Form
    {
        private readonly AppController _controller;
        private readonly InputHookEngine _hookEngine;
        private readonly uint _activateMessageId;
        private bool _allowShowDisplay;
        private bool _forceExit;
        private bool _updatingUi;

        // Dynamic icons
        private Icon _iconActive;
        private Icon _iconWaiting;
        private Icon _iconOff;
        private EngineRunState _lastDisplayedRunState = (EngineRunState)(-1);

        // Tray
        private NotifyIcon _trayIcon;
        private ContextMenuStrip _trayMenu;
        private ToolStripMenuItem _trayToggleItem;
        private ToolStripMenuItem _trayOpenItem;
        private ToolStripMenuItem _trayExitItem;

        // Header
        private StatusBadgeControl _statusBadge;

        // Card 1: Master & child toggles
        private ToggleSwitch _masterToggle;
        private Label _masterTitleLabel;
        private Label _masterHintLabel;

        private ToggleSwitch _autoGameToggle;
        private Label _autoGameTitleLabel;
        private Label _autoGameDescLabel;

        private ToggleSwitch _autoStartToggle;
        private Label _autoStartTitleLabel;
        private Label _autoStartDescLabel;

        // Card 2: Keybindings & delay
        private Label _rollKeyBadge;
        private ModernButton _rollKeyButton;
        private Label _toggleHotkeyBadge;
        private ModernButton _toggleHotkeyButton;
        private TrackBar _delayTrackBar;
        private NumericUpDown _delayNumeric;
        private Label _keyFeedbackLabel;

        // Card 3: Target games
        private CheckBox _nightreignCheckBox;
        private CheckBox _eldenRingCheckBox;
        private TextBox _customProcessesTextBox;
        private Label _foregroundStatusLabel;

        // Footer
        private CheckBox _minimizeOnCloseCheckBox;
        private ModernButton _hideToTrayButton;
        private Label _configPathLabel;

        // Live foreground monitor timer
        private Timer _statusTimer;

        public MainForm(AppController controller, InputHookEngine hookEngine, uint activateMessageId, bool startMinimized)
        {
            _controller = controller;
            _hookEngine = hookEngine;
            _activateMessageId = activateMessageId;
            _allowShowDisplay = !startMinimized;

            InitializeIcons();
            InitializeFormLayout();
            InitializeTrayIcon();
            WireEvents();

            // Ensure handle exists even when started silently minimized to tray
            IntPtr unusedHandle = this.Handle;
            NativeMethods.TryEnableDarkTitleBar(this.Handle);

            _hookEngine.RefreshForegroundProcess();
            SyncUiFromController();
            StartStatusTimer();
        }

        private void InitializeIcons()
        {
            _iconActive = IconFactory.CreateStatusIcon(EngineRunState.Active, 32);
            _iconWaiting = IconFactory.CreateStatusIcon(EngineRunState.WaitingForGame, 32);
            _iconOff = IconFactory.CreateStatusIcon(EngineRunState.Off, 32);
        }

        private void InitializeFormLayout()
        {
            SuspendLayout();

            Text = "콩벌레 (EldenInstantRoll)";
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96F, 96F);
            ClientSize = new Size(510, 676);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = ThemeColors.Background;
            ForeColor = ThemeColors.TextPrimary;
            Font = new Font("Malgun Gothic", 9.5F, FontStyle.Regular, GraphicsUnit.Point);
            Icon = _iconActive;
            KeyPreview = true;

            int margin = 18;
            int cardWidth = ClientSize.Width - margin * 2;

            // ===== Header =====
            Label titleLabel = new Label();
            titleLabel.Text = "콩벌레 (EldenInstantRoll)";
            titleLabel.Font = new Font("Malgun Gothic", 13.5F, FontStyle.Bold);
            titleLabel.ForeColor = ThemeColors.Gold;
            titleLabel.Location = new Point(margin, 16);
            titleLabel.AutoSize = true;
            Controls.Add(titleLabel);

            Label subtitleLabel = new Label();
            subtitleLabel.Text = "엘든링 · 밤의 통치자 키 누름 즉시 구르기";
            subtitleLabel.Font = new Font("Malgun Gothic", 8.8F, FontStyle.Regular);
            subtitleLabel.ForeColor = ThemeColors.TextSecondary;
            subtitleLabel.Location = new Point(margin + 1, 45);
            subtitleLabel.AutoSize = true;
            Controls.Add(subtitleLabel);

            _statusBadge = new StatusBadgeControl();
            _statusBadge.Font = new Font("Malgun Gothic", 9F, FontStyle.Bold);
            _statusBadge.Size = new Size(138, 30);
            _statusBadge.Location = new Point(ClientSize.Width - margin - _statusBadge.Width, 22);
            Controls.Add(_statusBadge);

            // ===== Card 1: Master Control (Top Priority) =====
            RoundedCardPanel cardMaster = new RoundedCardPanel();
            cardMaster.Location = new Point(margin, 76);
            cardMaster.Size = new Size(cardWidth, 186);
            Controls.Add(cardMaster);

            _masterTitleLabel = new Label();
            _masterTitleLabel.Text = "마스터 ON / OFF";
            _masterTitleLabel.Font = new Font("Malgun Gothic", 11.5F, FontStyle.Bold);
            _masterTitleLabel.ForeColor = ThemeColors.TextPrimary;
            _masterTitleLabel.BackColor = ThemeColors.CardBg;
            _masterTitleLabel.Location = new Point(16, 14);
            _masterTitleLabel.Size = new Size(340, 24);
            cardMaster.Controls.Add(_masterTitleLabel);

            _masterHintLabel = new Label();
            _masterHintLabel.Text = "끄면 즉시 중지되며 하위 자동 켜짐 2개가 모두 꺼지고 비활성화됩니다.";
            _masterHintLabel.Font = new Font("Malgun Gothic", 8.2F, FontStyle.Regular);
            _masterHintLabel.ForeColor = ThemeColors.TextSecondary;
            _masterHintLabel.BackColor = ThemeColors.CardBg;
            _masterHintLabel.Location = new Point(16, 40);
            _masterHintLabel.Size = new Size(368, 18);
            cardMaster.Controls.Add(_masterHintLabel);

            _masterToggle = new ToggleSwitch();
            _masterToggle.Size = new Size(56, 30);
            _masterToggle.Location = new Point(cardMaster.Width - 16 - _masterToggle.Width, 18);
            _masterToggle.OnColor = ThemeColors.Emerald;
            cardMaster.Controls.Add(_masterToggle);

            Panel divider1 = new Panel();
            divider1.BackColor = ThemeColors.Divider;
            divider1.Location = new Point(16, 68);
            divider1.Size = new Size(cardMaster.Width - 32, 1);
            cardMaster.Controls.Add(divider1);

            // Child Toggle 1: AutoEnableOnGame
            _autoGameTitleLabel = new Label();
            _autoGameTitleLabel.Text = "게임 감지 자동 켜짐";
            _autoGameTitleLabel.Font = new Font("Malgun Gothic", 9.8F, FontStyle.Bold);
            _autoGameTitleLabel.ForeColor = ThemeColors.TextPrimary;
            _autoGameTitleLabel.BackColor = ThemeColors.CardBg;
            _autoGameTitleLabel.Location = new Point(16, 80);
            _autoGameTitleLabel.Size = new Size(350, 20);
            cardMaster.Controls.Add(_autoGameTitleLabel);

            _autoGameDescLabel = new Label();
            _autoGameDescLabel.Text = "선택된 게임 창이 활성일 때만 자동 작동 (끄면 모든 창에서 수동 작동)";
            _autoGameDescLabel.Font = new Font("Malgun Gothic", 8.2F, FontStyle.Regular);
            _autoGameDescLabel.ForeColor = ThemeColors.TextSecondary;
            _autoGameDescLabel.BackColor = ThemeColors.CardBg;
            _autoGameDescLabel.Location = new Point(16, 101);
            _autoGameDescLabel.Size = new Size(375, 18);
            cardMaster.Controls.Add(_autoGameDescLabel);

            _autoGameToggle = new ToggleSwitch();
            _autoGameToggle.Size = new Size(46, 24);
            _autoGameToggle.Location = new Point(cardMaster.Width - 16 - _autoGameToggle.Width, 87);
            _autoGameToggle.OnColor = ThemeColors.Gold;
            cardMaster.Controls.Add(_autoGameToggle);

            // Child Toggle 2: AutoStartWithWindows
            _autoStartTitleLabel = new Label();
            _autoStartTitleLabel.Text = "윈도우 부팅 시 자동 실행";
            _autoStartTitleLabel.Font = new Font("Malgun Gothic", 9.8F, FontStyle.Bold);
            _autoStartTitleLabel.ForeColor = ThemeColors.TextPrimary;
            _autoStartTitleLabel.BackColor = ThemeColors.CardBg;
            _autoStartTitleLabel.Location = new Point(16, 132);
            _autoStartTitleLabel.Size = new Size(350, 20);
            cardMaster.Controls.Add(_autoStartTitleLabel);

            _autoStartDescLabel = new Label();
            _autoStartDescLabel.Text = "Windows 시작 시 트레이로 자동 실행 (마스터 OFF 시 즉시 해제)";
            _autoStartDescLabel.Font = new Font("Malgun Gothic", 8.2F, FontStyle.Regular);
            _autoStartDescLabel.ForeColor = ThemeColors.TextSecondary;
            _autoStartDescLabel.BackColor = ThemeColors.CardBg;
            _autoStartDescLabel.Location = new Point(16, 153);
            _autoStartDescLabel.Size = new Size(375, 18);
            cardMaster.Controls.Add(_autoStartDescLabel);

            _autoStartToggle = new ToggleSwitch();
            _autoStartToggle.Size = new Size(46, 24);
            _autoStartToggle.Location = new Point(cardMaster.Width - 16 - _autoStartToggle.Width, 139);
            _autoStartToggle.OnColor = ThemeColors.Gold;
            cardMaster.Controls.Add(_autoStartToggle);

            // ===== Card 2: Key & Input Settings =====
            RoundedCardPanel cardKeys = new RoundedCardPanel();
            cardKeys.Location = new Point(margin, 276);
            cardKeys.Size = new Size(cardWidth, 184);
            Controls.Add(cardKeys);

            Label keysHeader = new Label();
            keysHeader.Text = "키 및 입력 설정 (한/영 무관 스캔코드)";
            keysHeader.Font = new Font("Malgun Gothic", 10F, FontStyle.Bold);
            keysHeader.ForeColor = ThemeColors.Gold;
            keysHeader.BackColor = ThemeColors.CardBg;
            keysHeader.Location = new Point(16, 12);
            keysHeader.AutoSize = true;
            cardKeys.Controls.Add(keysHeader);

            // Row 1: Roll Key
            Label rollKeyLabel = new Label();
            rollKeyLabel.Text = "구르기 키";
            rollKeyLabel.Font = new Font("Malgun Gothic", 9.5F, FontStyle.Regular);
            rollKeyLabel.ForeColor = ThemeColors.TextPrimary;
            rollKeyLabel.BackColor = ThemeColors.CardBg;
            rollKeyLabel.Location = new Point(16, 46);
            rollKeyLabel.Size = new Size(120, 26);
            rollKeyLabel.TextAlign = ContentAlignment.MiddleLeft;
            cardKeys.Controls.Add(rollKeyLabel);

            _rollKeyBadge = new Label();
            _rollKeyBadge.BackColor = ThemeColors.InputBg;
            _rollKeyBadge.ForeColor = ThemeColors.TextPrimary;
            _rollKeyBadge.BorderStyle = BorderStyle.FixedSingle;
            _rollKeyBadge.Font = new Font("Consolas", 10F, FontStyle.Bold);
            _rollKeyBadge.TextAlign = ContentAlignment.MiddleCenter;
            _rollKeyBadge.Location = new Point(144, 44);
            _rollKeyBadge.Size = new Size(192, 30);
            cardKeys.Controls.Add(_rollKeyBadge);

            _rollKeyButton = new ModernButton();
            _rollKeyButton.Text = "키 변경";
            _rollKeyButton.Font = new Font("Malgun Gothic", 9F, FontStyle.Bold);
            _rollKeyButton.Location = new Point(346, 44);
            _rollKeyButton.Size = new Size(112, 30);
            cardKeys.Controls.Add(_rollKeyButton);

            // Row 2: Toggle Hotkey
            Label toggleKeyLabel = new Label();
            toggleKeyLabel.Text = "ON/OFF 단축키";
            toggleKeyLabel.Font = new Font("Malgun Gothic", 9.5F, FontStyle.Regular);
            toggleKeyLabel.ForeColor = ThemeColors.TextPrimary;
            toggleKeyLabel.BackColor = ThemeColors.CardBg;
            toggleKeyLabel.Location = new Point(16, 86);
            toggleKeyLabel.Size = new Size(120, 26);
            toggleKeyLabel.TextAlign = ContentAlignment.MiddleLeft;
            cardKeys.Controls.Add(toggleKeyLabel);

            _toggleHotkeyBadge = new Label();
            _toggleHotkeyBadge.BackColor = ThemeColors.InputBg;
            _toggleHotkeyBadge.ForeColor = ThemeColors.TextPrimary;
            _toggleHotkeyBadge.BorderStyle = BorderStyle.FixedSingle;
            _toggleHotkeyBadge.Font = new Font("Consolas", 10F, FontStyle.Bold);
            _toggleHotkeyBadge.TextAlign = ContentAlignment.MiddleCenter;
            _toggleHotkeyBadge.Location = new Point(144, 84);
            _toggleHotkeyBadge.Size = new Size(192, 30);
            cardKeys.Controls.Add(_toggleHotkeyBadge);

            _toggleHotkeyButton = new ModernButton();
            _toggleHotkeyButton.Text = "단축키 변경";
            _toggleHotkeyButton.Font = new Font("Malgun Gothic", 9F, FontStyle.Bold);
            _toggleHotkeyButton.Location = new Point(346, 84);
            _toggleHotkeyButton.Size = new Size(112, 30);
            cardKeys.Controls.Add(_toggleHotkeyButton);

            // Row 3: Tap Delay
            Label delayLabel = new Label();
            delayLabel.Text = "입력 유지 시간";
            delayLabel.Font = new Font("Malgun Gothic", 9.5F, FontStyle.Regular);
            delayLabel.ForeColor = ThemeColors.TextPrimary;
            delayLabel.BackColor = ThemeColors.CardBg;
            delayLabel.Location = new Point(16, 126);
            delayLabel.Size = new Size(120, 26);
            delayLabel.TextAlign = ContentAlignment.MiddleLeft;
            cardKeys.Controls.Add(delayLabel);

            _delayTrackBar = new TrackBar();
            _delayTrackBar.Minimum = AppSettings.MinTapDelayMs;
            _delayTrackBar.Maximum = AppSettings.MaxTapDelayMs;
            _delayTrackBar.TickFrequency = 5;
            _delayTrackBar.TickStyle = TickStyle.None;
            _delayTrackBar.BackColor = ThemeColors.CardBg;
            _delayTrackBar.Location = new Point(142, 125);
            _delayTrackBar.Size = new Size(216, 28);
            cardKeys.Controls.Add(_delayTrackBar);

            _delayNumeric = new NumericUpDown();
            _delayNumeric.Minimum = AppSettings.MinTapDelayMs;
            _delayNumeric.Maximum = AppSettings.MaxTapDelayMs;
            _delayNumeric.BackColor = ThemeColors.InputBg;
            _delayNumeric.ForeColor = ThemeColors.TextPrimary;
            _delayNumeric.BorderStyle = BorderStyle.FixedSingle;
            _delayNumeric.Font = new Font("Consolas", 10F, FontStyle.Bold);
            _delayNumeric.Location = new Point(366, 126);
            _delayNumeric.Size = new Size(58, 26);
            cardKeys.Controls.Add(_delayNumeric);

            Label msUnitLabel = new Label();
            msUnitLabel.Text = "ms";
            msUnitLabel.Font = new Font("Malgun Gothic", 9F, FontStyle.Regular);
            msUnitLabel.ForeColor = ThemeColors.TextSecondary;
            msUnitLabel.BackColor = ThemeColors.CardBg;
            msUnitLabel.Location = new Point(428, 128);
            msUnitLabel.AutoSize = true;
            cardKeys.Controls.Add(msUnitLabel);

            _keyFeedbackLabel = new Label();
            _keyFeedbackLabel.Text = "키보드 아무 키 또는 마우스 사이드 버튼(Mouse4/Mouse5/휠클릭) 지원";
            _keyFeedbackLabel.Font = new Font("Malgun Gothic", 8.2F, FontStyle.Regular);
            _keyFeedbackLabel.ForeColor = ThemeColors.TextSecondary;
            _keyFeedbackLabel.BackColor = ThemeColors.CardBg;
            _keyFeedbackLabel.Location = new Point(16, 158);
            _keyFeedbackLabel.Size = new Size(cardKeys.Width - 32, 18);
            cardKeys.Controls.Add(_keyFeedbackLabel);

            // ===== Card 3: Target Games =====
            RoundedCardPanel cardTargets = new RoundedCardPanel();
            cardTargets.Location = new Point(margin, 474);
            cardTargets.Size = new Size(cardWidth, 134);
            Controls.Add(cardTargets);

            Label targetHeader = new Label();
            targetHeader.Text = "대상 게임 선택";
            targetHeader.Font = new Font("Malgun Gothic", 10F, FontStyle.Bold);
            targetHeader.ForeColor = ThemeColors.Gold;
            targetHeader.BackColor = ThemeColors.CardBg;
            targetHeader.Location = new Point(16, 12);
            targetHeader.AutoSize = true;
            cardTargets.Controls.Add(targetHeader);

            _foregroundStatusLabel = new Label();
            _foregroundStatusLabel.Text = "활성 창: -";
            _foregroundStatusLabel.Font = new Font("Malgun Gothic", 8.2F, FontStyle.Regular);
            _foregroundStatusLabel.ForeColor = ThemeColors.TextSecondary;
            _foregroundStatusLabel.BackColor = ThemeColors.CardBg;
            _foregroundStatusLabel.TextAlign = ContentAlignment.MiddleRight;
            _foregroundStatusLabel.Location = new Point(180, 12);
            _foregroundStatusLabel.Size = new Size(cardTargets.Width - 196, 18);
            cardTargets.Controls.Add(_foregroundStatusLabel);

            _nightreignCheckBox = new CheckBox();
            _nightreignCheckBox.Text = "엘든링: 밤의 통치자 (nightreign.exe)";
            _nightreignCheckBox.ForeColor = ThemeColors.TextPrimary;
            _nightreignCheckBox.BackColor = ThemeColors.CardBg;
            _nightreignCheckBox.Font = new Font("Malgun Gothic", 9.3F, FontStyle.Regular);
            _nightreignCheckBox.Location = new Point(16, 38);
            _nightreignCheckBox.AutoSize = true;
            cardTargets.Controls.Add(_nightreignCheckBox);

            _eldenRingCheckBox = new CheckBox();
            _eldenRingCheckBox.Text = "엘든링 본편 (eldenring.exe)";
            _eldenRingCheckBox.ForeColor = ThemeColors.TextPrimary;
            _eldenRingCheckBox.BackColor = ThemeColors.CardBg;
            _eldenRingCheckBox.Font = new Font("Malgun Gothic", 9.3F, FontStyle.Regular);
            _eldenRingCheckBox.Location = new Point(16, 65);
            _eldenRingCheckBox.AutoSize = true;
            cardTargets.Controls.Add(_eldenRingCheckBox);

            Label customProcLabel = new Label();
            customProcLabel.Text = "추가 프로세스 (.exe, 쉼표 구분):";
            customProcLabel.Font = new Font("Malgun Gothic", 8.5F, FontStyle.Regular);
            customProcLabel.ForeColor = ThemeColors.TextSecondary;
            customProcLabel.BackColor = ThemeColors.CardBg;
            customProcLabel.Location = new Point(16, 98);
            customProcLabel.AutoSize = true;
            cardTargets.Controls.Add(customProcLabel);

            _customProcessesTextBox = new TextBox();
            _customProcessesTextBox.BackColor = ThemeColors.InputBg;
            _customProcessesTextBox.ForeColor = ThemeColors.TextPrimary;
            _customProcessesTextBox.BorderStyle = BorderStyle.FixedSingle;
            _customProcessesTextBox.Font = new Font("Consolas", 9.2F, FontStyle.Regular);
            _customProcessesTextBox.Location = new Point(216, 94);
            _customProcessesTextBox.Size = new Size(cardTargets.Width - 232, 24);
            cardTargets.Controls.Add(_customProcessesTextBox);

            // ===== Footer =====
            _minimizeOnCloseCheckBox = new CheckBox();
            _minimizeOnCloseCheckBox.Text = "창 닫기(X) 시 트레이로 최소화";
            _minimizeOnCloseCheckBox.ForeColor = ThemeColors.TextSecondary;
            _minimizeOnCloseCheckBox.BackColor = ThemeColors.Background;
            _minimizeOnCloseCheckBox.Font = new Font("Malgun Gothic", 8.8F, FontStyle.Regular);
            _minimizeOnCloseCheckBox.Location = new Point(margin, 620);
            _minimizeOnCloseCheckBox.AutoSize = true;
            Controls.Add(_minimizeOnCloseCheckBox);

            _hideToTrayButton = new ModernButton();
            _hideToTrayButton.Text = "트레이로 숨기기";
            _hideToTrayButton.Font = new Font("Malgun Gothic", 8.8F, FontStyle.Bold);
            _hideToTrayButton.Location = new Point(ClientSize.Width - margin - 122, 616);
            _hideToTrayButton.Size = new Size(122, 28);
            Controls.Add(_hideToTrayButton);

            _configPathLabel = new Label();
            _configPathLabel.Text = "설정 파일: " + ShortenPath(_controller.SettingsPath, 58);
            _configPathLabel.Font = new Font("Malgun Gothic", 7.8F, FontStyle.Regular);
            _configPathLabel.ForeColor = ThemeColors.TextDisabled;
            _configPathLabel.BackColor = ThemeColors.Background;
            _configPathLabel.Location = new Point(margin, 650);
            _configPathLabel.Size = new Size(cardWidth, 18);
            Controls.Add(_configPathLabel);

            ResumeLayout(false);
        }

        private void InitializeTrayIcon()
        {
            _trayMenu = new ContextMenuStrip();
            _trayOpenItem = new ToolStripMenuItem("창 열기");
            _trayOpenItem.Font = new Font("Malgun Gothic", 9F, FontStyle.Bold);
            _trayOpenItem.Click += delegate { RestoreFromTray(); };

            _trayToggleItem = new ToolStripMenuItem("마스터 ON / OFF");
            _trayToggleItem.CheckOnClick = false;
            _trayToggleItem.Click += delegate { _controller.ToggleMaster(); };

            _trayExitItem = new ToolStripMenuItem("종료");
            _trayExitItem.Click += delegate
            {
                _forceExit = true;
                Close();
            };

            _trayMenu.Items.Add(_trayOpenItem);
            _trayMenu.Items.Add(_trayToggleItem);
            _trayMenu.Items.Add(new ToolStripSeparator());
            _trayMenu.Items.Add(_trayExitItem);

            _trayIcon = new NotifyIcon();
            _trayIcon.Icon = _iconActive;
            _trayIcon.ContextMenuStrip = _trayMenu;
            _trayIcon.Visible = true;
            _trayIcon.DoubleClick += delegate { RestoreFromTray(); };
        }

        private void WireEvents()
        {
            _controller.StateChanged += delegate
            {
                if (IsHandleCreated && InvokeRequired)
                {
                    BeginInvoke((MethodInvoker)OnControllerStateChanged);
                }
                else
                {
                    OnControllerStateChanged();
                }
            };

            _hookEngine.ToggleHotkeyPressed += delegate
            {
                if (IsHandleCreated)
                {
                    BeginInvoke((MethodInvoker)delegate
                    {
                        _controller.ToggleMaster();
                    });
                }
            };

            _hookEngine.KeyCaptured += delegate(object sender, KeyCapturedEventArgs args)
            {
                if (IsHandleCreated)
                {
                    BeginInvoke((MethodInvoker)delegate
                    {
                        HandleKeyCaptured(args);
                    });
                }
            };

            _masterToggle.CheckedChanged += delegate
            {
                if (_updatingUi) return;
                _controller.SetMasterEnabled(_masterToggle.Checked);
            };

            _autoGameToggle.CheckedChanged += delegate
            {
                if (_updatingUi) return;
                _controller.SetAutoEnableOnGame(_autoGameToggle.Checked);
            };

            _autoStartToggle.CheckedChanged += delegate
            {
                if (_updatingUi) return;
                bool ok = _controller.SetAutoStartWithWindows(_autoStartToggle.Checked);
                if (!ok)
                {
                    SyncUiFromController();
                }
            };

            _rollKeyButton.Click += delegate
            {
                if (_hookEngine.CurrentCaptureMode == CaptureMode.RollKey)
                {
                    _hookEngine.CancelCapture();
                }
                else
                {
                    StartCapture(CaptureMode.RollKey);
                }
            };

            _toggleHotkeyButton.Click += delegate
            {
                if (_hookEngine.CurrentCaptureMode == CaptureMode.ToggleHotkey)
                {
                    _hookEngine.CancelCapture();
                }
                else
                {
                    StartCapture(CaptureMode.ToggleHotkey);
                }
            };

            _delayTrackBar.ValueChanged += delegate
            {
                if (_updatingUi) return;
                _controller.SetTapDelayMs(_delayTrackBar.Value);
            };

            _delayNumeric.ValueChanged += delegate
            {
                if (_updatingUi) return;
                _controller.SetTapDelayMs((int)_delayNumeric.Value);
            };

            _nightreignCheckBox.CheckedChanged += delegate { PushTargetChanges(); };
            _eldenRingCheckBox.CheckedChanged += delegate { PushTargetChanges(); };
            _customProcessesTextBox.TextChanged += delegate { PushTargetChanges(); };

            _minimizeOnCloseCheckBox.CheckedChanged += delegate
            {
                if (_updatingUi) return;
                _controller.SetMinimizeToTrayOnClose(_minimizeOnCloseCheckBox.Checked);
            };

            _hideToTrayButton.Click += delegate { HideToTray(); };
        }

        private void StartStatusTimer()
        {
            _statusTimer = new Timer();
            _statusTimer.Interval = 350;
            _statusTimer.Tick += delegate
            {
                string fg = _hookEngine.RefreshForegroundProcess();
                UpdateLiveStatusIndicators(fg);
            };
            _statusTimer.Start();
        }

        private void PushTargetChanges()
        {
            if (_updatingUi)
            {
                return;
            }
            _controller.SetTargets(
                _nightreignCheckBox.Checked,
                _eldenRingCheckBox.Checked,
                _customProcessesTextBox.Text);
        }

        private void StartCapture(CaptureMode mode)
        {
            _hookEngine.BeginCapture(mode);

            if (mode == CaptureMode.RollKey)
            {
                _rollKeyButton.Text = "입력 대기...";
                _toggleHotkeyButton.Text = "단축키 변경";
                _keyFeedbackLabel.ForeColor = ThemeColors.Gold;
                _keyFeedbackLabel.Text = "원하는 키(또는 Mouse4/Mouse5/휠클릭)를 누르세요... (ESC 취소)";
            }
            else if (mode == CaptureMode.ToggleHotkey)
            {
                _toggleHotkeyButton.Text = "입력 대기...";
                _rollKeyButton.Text = "키 변경";
                _keyFeedbackLabel.ForeColor = ThemeColors.Gold;
                _keyFeedbackLabel.Text = "원하는 ON/OFF 단축키(키보드)를 누르세요... (ESC 취소)";
            }
        }

        private void HandleKeyCaptured(KeyCapturedEventArgs args)
        {
            _rollKeyButton.Text = "키 변경";
            _toggleHotkeyButton.Text = "단축키 변경";
            _hookEngine.RefreshMouseHookState();

            if (args.Cancelled || args.CapturedBinding == null)
            {
                _keyFeedbackLabel.ForeColor = ThemeColors.TextSecondary;
                _keyFeedbackLabel.Text = "키 변경이 취소되었습니다.";
                return;
            }

            string error;
            bool ok;
            if (args.Mode == CaptureMode.RollKey)
            {
                ok = _controller.TrySetRollKey(args.CapturedBinding, out error);
            }
            else
            {
                ok = _controller.TrySetToggleHotkey(args.CapturedBinding, out error);
            }

            if (ok)
            {
                _keyFeedbackLabel.ForeColor = ThemeColors.Emerald;
                _keyFeedbackLabel.Text = args.Mode == CaptureMode.RollKey
                    ? "구르기 키가 [" + args.CapturedBinding.FormatBadgeText() + "]로 저장되었습니다."
                    : "ON/OFF 단축키가 [" + args.CapturedBinding.FormatBadgeText() + "]로 저장되었습니다.";
            }
            else
            {
                _keyFeedbackLabel.ForeColor = ThemeColors.Danger;
                _keyFeedbackLabel.Text = error ?? "키를 변경할 수 없습니다.";
            }
        }

        private void OnControllerStateChanged()
        {
            _hookEngine.ResetHeldStates();
            _hookEngine.RefreshMouseHookState();
            SyncUiFromController();
        }

        private void SyncUiFromController()
        {
            _updatingUi = true;
            try
            {
                bool masterOn = _controller.MasterEnabled;
                bool childEnabled = _controller.ChildTogglesEnabled;

                _masterToggle.SetCheckedSilent(masterOn);
                _masterTitleLabel.Text = masterOn ? "마스터 활성화 (ON)" : "마스터 꺼짐 (OFF)";
                _masterTitleLabel.ForeColor = masterOn ? ThemeColors.TextPrimary : ThemeColors.TextSecondary;

                _autoGameToggle.SetCheckedSilent(_controller.AutoEnableOnGame);
                _autoGameToggle.Enabled = childEnabled;
                _autoGameTitleLabel.ForeColor = childEnabled ? ThemeColors.TextPrimary : ThemeColors.TextDisabled;
                _autoGameDescLabel.ForeColor = childEnabled ? ThemeColors.TextSecondary : ThemeColors.TextDisabled;

                _autoStartToggle.SetCheckedSilent(_controller.AutoStartWithWindows);
                _autoStartToggle.Enabled = childEnabled;
                _autoStartTitleLabel.ForeColor = childEnabled ? ThemeColors.TextPrimary : ThemeColors.TextDisabled;
                _autoStartDescLabel.ForeColor = childEnabled ? ThemeColors.TextSecondary : ThemeColors.TextDisabled;

                _rollKeyBadge.Text = _controller.RollKey.FormatBadgeText();
                _toggleHotkeyBadge.Text = _controller.ToggleHotkey.FormatBadgeText();

                int delay = AppSettings.ClampTapDelay(_controller.TapDelayMs);
                if (_delayTrackBar.Value != delay) _delayTrackBar.Value = delay;
                if (_delayNumeric.Value != delay) _delayNumeric.Value = delay;

                _nightreignCheckBox.Checked = _controller.TargetNightreign;
                _eldenRingCheckBox.Checked = _controller.TargetEldenRing;
                if (!string.Equals(_customProcessesTextBox.Text, _controller.CustomProcesses, StringComparison.Ordinal))
                {
                    _customProcessesTextBox.Text = _controller.CustomProcesses;
                }

                _minimizeOnCloseCheckBox.Checked = _controller.MinimizeToTrayOnClose;

                _trayToggleItem.Checked = masterOn;
                _trayToggleItem.Text = masterOn
                    ? "마스터 끄기 (OFF)  [" + _controller.ToggleHotkey.FormatShortText() + "]"
                    : "마스터 켜기 (ON)  [" + _controller.ToggleHotkey.FormatShortText() + "]";
            }
            finally
            {
                _updatingUi = false;
            }

            UpdateLiveStatusIndicators(_controller.LastForegroundProcess);
        }

        private void UpdateLiveStatusIndicators(string foregroundProcess)
        {
            EngineRunState state = _controller.GetRunStateForProcess(foregroundProcess);

            string fgLabel = string.IsNullOrEmpty(foregroundProcess) ? "활성 창: -" : "활성 창: " + foregroundProcess;
            if (!string.Equals(_foregroundStatusLabel.Text, fgLabel, StringComparison.Ordinal))
            {
                _foregroundStatusLabel.Text = fgLabel;
            }

            if (state != _lastDisplayedRunState)
            {
                _lastDisplayedRunState = state;
                if (state == EngineRunState.Active)
                {
                    _statusBadge.SetBadge("작동 중", ThemeColors.Emerald, ThemeColors.EmeraldDark, ThemeColors.Emerald, ThemeColors.TextPrimary);
                    Icon = _iconActive;
                    _trayIcon.Icon = _iconActive;
                }
                else if (state == EngineRunState.WaitingForGame)
                {
                    _statusBadge.SetBadge("게임 대기 중", ThemeColors.Gold, ThemeColors.GoldDark, ThemeColors.Gold, ThemeColors.TextPrimary);
                    Icon = _iconWaiting;
                    _trayIcon.Icon = _iconWaiting;
                }
                else
                {
                    _statusBadge.SetBadge("꺼짐", ThemeColors.Muted, ThemeColors.MutedDark, ThemeColors.CardBorder, ThemeColors.TextSecondary);
                    Icon = _iconOff;
                    _trayIcon.Icon = _iconOff;
                }
            }

            // NotifyIcon.Text limit is 63 chars in WinForms
            string statusKo = state == EngineRunState.Active
                ? "작동 중"
                : (state == EngineRunState.WaitingForGame ? "게임 대기 중" : "꺼짐");
            string tooltip = string.Format(
                CultureInfo.InvariantCulture,
                "콩벌레 (EldenInstantRoll) [{0}] {1} | {2}",
                statusKo,
                _controller.RollKey.FormatShortText(),
                _controller.ToggleHotkey.FormatShortText());
            if (tooltip.Length > 63)
            {
                tooltip = tooltip.Substring(0, 63);
            }
            if (!string.Equals(_trayIcon.Text, tooltip, StringComparison.Ordinal))
            {
                _trayIcon.Text = tooltip;
            }
        }

        protected override void SetVisibleCore(bool value)
        {
            if (!_allowShowDisplay && value)
            {
                base.SetVisibleCore(false);
                return;
            }
            base.SetVisibleCore(value);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (WindowState == FormWindowState.Minimized)
            {
                HideToTray();
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!_forceExit && e.CloseReason == CloseReason.UserClosing && _controller.MinimizeToTrayOnClose)
            {
                e.Cancel = true;
                HideToTray();
                return;
            }
            base.OnFormClosing(e);
        }

        protected override void WndProc(ref Message m)
        {
            if (_activateMessageId != 0 && m.Msg == (int)_activateMessageId)
            {
                RestoreFromTray();
                return;
            }
            base.WndProc(ref m);
        }

        public void HideToTray()
        {
            _hookEngine.CancelCapture();
            ShowInTaskbar = false;
            Hide();
        }

        public void RestoreFromTray()
        {
            _allowShowDisplay = true;
            ShowInTaskbar = true;
            if (!Visible)
            {
                Show();
            }
            if (WindowState == FormWindowState.Minimized)
            {
                WindowState = FormWindowState.Normal;
            }
            NativeMethods.ShowWindow(Handle, NativeMethods.SW_RESTORE);
            NativeMethods.SetForegroundWindow(Handle);
            Activate();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_statusTimer != null)
                {
                    _statusTimer.Stop();
                    _statusTimer.Dispose();
                    _statusTimer = null;
                }
                if (_trayIcon != null)
                {
                    _trayIcon.Visible = false;
                    _trayIcon.Dispose();
                    _trayIcon = null;
                }
                if (_trayMenu != null)
                {
                    _trayMenu.Dispose();
                    _trayMenu = null;
                }
                if (_iconActive != null) { _iconActive.Dispose(); _iconActive = null; }
                if (_iconWaiting != null) { _iconWaiting.Dispose(); _iconWaiting = null; }
                if (_iconOff != null) { _iconOff.Dispose(); _iconOff = null; }
            }
            base.Dispose(disposing);
        }

        private static string ShortenPath(string path, int maxLength)
        {
            if (string.IsNullOrEmpty(path) || path.Length <= maxLength)
            {
                return path ?? string.Empty;
            }
            string fileName = Path.GetFileName(path);
            int keep = Math.Max(10, maxLength - fileName.Length - 4);
            if (keep >= path.Length)
            {
                return path;
            }
            return path.Substring(0, keep) + "...\\" + fileName;
        }
    }
}
