using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Windows.Forms;

namespace EldenInstantRoll
{
    public sealed class MainForm : Form
    {
        private sealed class ActionRowWidgets
        {
            public string ActionId;
            public bool IsCustom;
            public Panel RowPanel;
            public Panel AccentBar;
            public CheckBox EnabledCheckBox;
            public Label NameLabel;
            public Label KeyBadge;
            public ModernButton ChangeKeyButton;
            public ModernButton ClearOrDeleteButton;
        }

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
        private Label _activeSummaryLabel;

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

        // Card 2: Multi-action list
        private Label _listActiveCountLabel;
        private ModernButton _addCustomButton;
        private ModernButton _resetDefaultsButton;
        private Panel _actionsScrollPanel;
        private Label _actionFeedbackLabel;
        private readonly Dictionary<string, ActionRowWidgets> _actionRows = new Dictionary<string, ActionRowWidgets>(StringComparer.OrdinalIgnoreCase);

        // Card 3: Hotkey, delay, targets
        private Label _toggleHotkeyBadge;
        private ModernButton _toggleHotkeyButton;
        private TrackBar _delayTrackBar;
        private NumericUpDown _delayNumeric;
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
            RebuildActionRows();
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

            Text = "콩벌레 (EldenInstantRoll) v1.1.0";
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96F, 96F);
            ClientSize = new Size(640, 736);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = ThemeColors.Background;
            ForeColor = ThemeColors.TextPrimary;
            Font = new Font("Malgun Gothic", 9.2F, FontStyle.Regular, GraphicsUnit.Point);
            Icon = _iconActive;
            KeyPreview = true;

            int margin = 16;
            int cardWidth = ClientSize.Width - margin * 2;

            // ===== Header =====
            Label titleLabel = new Label();
            titleLabel.Text = "콩벌레 (EldenInstantRoll)  v1.1.0";
            titleLabel.Font = new Font("Malgun Gothic", 13.5F, FontStyle.Bold);
            titleLabel.ForeColor = ThemeColors.Gold;
            titleLabel.Location = new Point(margin, 14);
            titleLabel.AutoSize = true;
            Controls.Add(titleLabel);

            Label subtitleLabel = new Label();
            subtitleLabel.Text = "엘든링 · 밤의 통치자 키 누름 즉시 발동 (다중 키 지원)";
            subtitleLabel.Font = new Font("Malgun Gothic", 8.6F, FontStyle.Regular);
            subtitleLabel.ForeColor = ThemeColors.TextSecondary;
            subtitleLabel.Location = new Point(margin + 1, 42);
            subtitleLabel.AutoSize = true;
            Controls.Add(subtitleLabel);

            _statusBadge = new StatusBadgeControl();
            _statusBadge.Font = new Font("Malgun Gothic", 8.8F, FontStyle.Bold);
            _statusBadge.Size = new Size(132, 28);
            _statusBadge.Location = new Point(ClientSize.Width - margin - _statusBadge.Width, 14);
            Controls.Add(_statusBadge);

            _activeSummaryLabel = new Label();
            _activeSummaryLabel.Text = "활성 키: 1개";
            _activeSummaryLabel.Font = new Font("Malgun Gothic", 8.3F, FontStyle.Bold);
            _activeSummaryLabel.ForeColor = ThemeColors.Emerald;
            _activeSummaryLabel.TextAlign = ContentAlignment.MiddleRight;
            _activeSummaryLabel.Location = new Point(ClientSize.Width - margin - 260, 44);
            _activeSummaryLabel.Size = new Size(260, 18);
            Controls.Add(_activeSummaryLabel);

            // ===== Card 1: Master Control (Top Priority) =====
            RoundedCardPanel cardMaster = new RoundedCardPanel();
            cardMaster.Location = new Point(margin, 70);
            cardMaster.Size = new Size(cardWidth, 126);
            Controls.Add(cardMaster);

            _masterTitleLabel = new Label();
            _masterTitleLabel.Text = "마스터 활성화 (ON)";
            _masterTitleLabel.Font = new Font("Malgun Gothic", 11F, FontStyle.Bold);
            _masterTitleLabel.ForeColor = ThemeColors.TextPrimary;
            _masterTitleLabel.BackColor = ThemeColors.CardBg;
            _masterTitleLabel.Location = new Point(16, 12);
            _masterTitleLabel.Size = new Size(420, 22);
            cardMaster.Controls.Add(_masterTitleLabel);

            _masterHintLabel = new Label();
            _masterHintLabel.Text = "끄면 모든 즉시 발동이 중지되며 하위 자동 켜짐 2개가 모두 꺼지고 비활성화(레지스트리 삭제)됩니다.";
            _masterHintLabel.Font = new Font("Malgun Gothic", 8F, FontStyle.Regular);
            _masterHintLabel.ForeColor = ThemeColors.TextSecondary;
            _masterHintLabel.BackColor = ThemeColors.CardBg;
            _masterHintLabel.Location = new Point(16, 36);
            _masterHintLabel.Size = new Size(505, 18);
            cardMaster.Controls.Add(_masterHintLabel);

            _masterToggle = new ToggleSwitch();
            _masterToggle.Size = new Size(56, 30);
            _masterToggle.Location = new Point(cardMaster.Width - 16 - _masterToggle.Width, 16);
            _masterToggle.OnColor = ThemeColors.Emerald;
            cardMaster.Controls.Add(_masterToggle);

            Panel divider1 = new Panel();
            divider1.BackColor = ThemeColors.Divider;
            divider1.Location = new Point(16, 62);
            divider1.Size = new Size(cardMaster.Width - 32, 1);
            cardMaster.Controls.Add(divider1);

            // Child Toggle 1 (Left half)
            _autoGameTitleLabel = new Label();
            _autoGameTitleLabel.Text = "게임 감지 자동 켜짐";
            _autoGameTitleLabel.Font = new Font("Malgun Gothic", 9.3F, FontStyle.Bold);
            _autoGameTitleLabel.ForeColor = ThemeColors.TextPrimary;
            _autoGameTitleLabel.BackColor = ThemeColors.CardBg;
            _autoGameTitleLabel.Location = new Point(16, 73);
            _autoGameTitleLabel.Size = new Size(216, 20);
            cardMaster.Controls.Add(_autoGameTitleLabel);

            _autoGameDescLabel = new Label();
            _autoGameDescLabel.Text = "대상 게임 활성 시만 작동 (끄면 수동)";
            _autoGameDescLabel.Font = new Font("Malgun Gothic", 7.8F, FontStyle.Regular);
            _autoGameDescLabel.ForeColor = ThemeColors.TextSecondary;
            _autoGameDescLabel.BackColor = ThemeColors.CardBg;
            _autoGameDescLabel.Location = new Point(16, 94);
            _autoGameDescLabel.Size = new Size(220, 18);
            cardMaster.Controls.Add(_autoGameDescLabel);

            _autoGameToggle = new ToggleSwitch();
            _autoGameToggle.Size = new Size(44, 24);
            _autoGameToggle.Location = new Point(242, 80);
            _autoGameToggle.OnColor = ThemeColors.Gold;
            cardMaster.Controls.Add(_autoGameToggle);

            Panel vDivider = new Panel();
            vDivider.BackColor = ThemeColors.Divider;
            vDivider.Location = new Point(302, 74);
            vDivider.Size = new Size(1, 38);
            cardMaster.Controls.Add(vDivider);

            // Child Toggle 2 (Right half)
            _autoStartTitleLabel = new Label();
            _autoStartTitleLabel.Text = "윈도우 부팅 시 자동 실행";
            _autoStartTitleLabel.Font = new Font("Malgun Gothic", 9.3F, FontStyle.Bold);
            _autoStartTitleLabel.ForeColor = ThemeColors.TextPrimary;
            _autoStartTitleLabel.BackColor = ThemeColors.CardBg;
            _autoStartTitleLabel.Location = new Point(318, 73);
            _autoStartTitleLabel.Size = new Size(214, 20);
            cardMaster.Controls.Add(_autoStartTitleLabel);

            _autoStartDescLabel = new Label();
            _autoStartDescLabel.Text = "시작 시 트레이 실행 (마스터 OFF 시 해제)";
            _autoStartDescLabel.Font = new Font("Malgun Gothic", 7.8F, FontStyle.Regular);
            _autoStartDescLabel.ForeColor = ThemeColors.TextSecondary;
            _autoStartDescLabel.BackColor = ThemeColors.CardBg;
            _autoStartDescLabel.Location = new Point(318, 94);
            _autoStartDescLabel.Size = new Size(222, 18);
            cardMaster.Controls.Add(_autoStartDescLabel);

            _autoStartToggle = new ToggleSwitch();
            _autoStartToggle.Size = new Size(44, 24);
            _autoStartToggle.Location = new Point(cardMaster.Width - 16 - _autoStartToggle.Width, 80);
            _autoStartToggle.OnColor = ThemeColors.Gold;
            cardMaster.Controls.Add(_autoStartToggle);

            // ===== Card 2: Core Nightreign Multi-Action List =====
            RoundedCardPanel cardActions = new RoundedCardPanel();
            cardActions.Location = new Point(margin, 208);
            cardActions.Size = new Size(cardWidth, 354);
            Controls.Add(cardActions);

            Label actionsHeader = new Label();
            actionsHeader.Text = "즉시 발동 키 목록";
            actionsHeader.Font = new Font("Malgun Gothic", 10F, FontStyle.Bold);
            actionsHeader.ForeColor = ThemeColors.Gold;
            actionsHeader.BackColor = ThemeColors.CardBg;
            actionsHeader.Location = new Point(16, 12);
            actionsHeader.AutoSize = true;
            cardActions.Controls.Add(actionsHeader);

            _listActiveCountLabel = new Label();
            _listActiveCountLabel.Text = "활성 키: 1개";
            _listActiveCountLabel.Font = new Font("Malgun Gothic", 8.2F, FontStyle.Bold);
            _listActiveCountLabel.ForeColor = ThemeColors.Emerald;
            _listActiveCountLabel.BackColor = ThemeColors.CardBg;
            _listActiveCountLabel.Location = new Point(144, 14);
            _listActiveCountLabel.Size = new Size(236, 18);
            cardActions.Controls.Add(_listActiveCountLabel);

            _addCustomButton = new ModernButton();
            _addCustomButton.Text = "+ 키 추가";
            _addCustomButton.Font = new Font("Malgun Gothic", 8.2F, FontStyle.Bold);
            _addCustomButton.Size = new Size(84, 26);
            _addCustomButton.Location = new Point(cardActions.Width - 16 - 92 - 8 - 84, 10);
            cardActions.Controls.Add(_addCustomButton);

            _resetDefaultsButton = new ModernButton();
            _resetDefaultsButton.Text = "기본값 초기화";
            _resetDefaultsButton.Font = new Font("Malgun Gothic", 8.2F, FontStyle.Regular);
            _resetDefaultsButton.Size = new Size(92, 26);
            _resetDefaultsButton.Location = new Point(cardActions.Width - 16 - 92, 10);
            cardActions.Controls.Add(_resetDefaultsButton);

            Label guideLabel = new Label();
            guideLabel.Text = "체크된 키만 누르는 즉시 짧은 탭으로 발동됩니다. (차징·홀드가 필요한 키는 1클릭으로 체크 해제)";
            guideLabel.Font = new Font("Malgun Gothic", 8F, FontStyle.Regular);
            guideLabel.ForeColor = ThemeColors.TextSecondary;
            guideLabel.BackColor = ThemeColors.CardBg;
            guideLabel.Location = new Point(16, 40);
            guideLabel.Size = new Size(cardActions.Width - 32, 18);
            cardActions.Controls.Add(guideLabel);

            Panel listBorder = new Panel();
            listBorder.BackColor = ThemeColors.InputBorder;
            listBorder.Location = new Point(14, 62);
            listBorder.Size = new Size(cardActions.Width - 28, 260);
            listBorder.Padding = new Padding(1);
            cardActions.Controls.Add(listBorder);

            _actionsScrollPanel = new Panel();
            _actionsScrollPanel.Dock = DockStyle.Fill;
            _actionsScrollPanel.BackColor = ThemeColors.InputBg;
            _actionsScrollPanel.AutoScroll = true;
            listBorder.Controls.Add(_actionsScrollPanel);

            _actionFeedbackLabel = new Label();
            _actionFeedbackLabel.Text = "한/영 무관 스캔코드 기반 · 마우스 키 미사용 시 마우스 후크 미설치 (0ms 지연)";
            _actionFeedbackLabel.Font = new Font("Malgun Gothic", 8.1F, FontStyle.Regular);
            _actionFeedbackLabel.ForeColor = ThemeColors.TextSecondary;
            _actionFeedbackLabel.BackColor = ThemeColors.CardBg;
            _actionFeedbackLabel.Location = new Point(16, 329);
            _actionFeedbackLabel.Size = new Size(cardActions.Width - 32, 18);
            cardActions.Controls.Add(_actionFeedbackLabel);

            // ===== Card 3: Hotkey, Delay & Target Games =====
            RoundedCardPanel cardOptions = new RoundedCardPanel();
            cardOptions.Location = new Point(margin, 572);
            cardOptions.Size = new Size(cardWidth, 112);
            Controls.Add(cardOptions);

            // Row 1: Toggle hotkey + Tap delay
            Label toggleKeyLabel = new Label();
            toggleKeyLabel.Text = "ON/OFF 단축키";
            toggleKeyLabel.Font = new Font("Malgun Gothic", 9F, FontStyle.Bold);
            toggleKeyLabel.ForeColor = ThemeColors.TextPrimary;
            toggleKeyLabel.BackColor = ThemeColors.CardBg;
            toggleKeyLabel.Location = new Point(16, 16);
            toggleKeyLabel.Size = new Size(98, 26);
            toggleKeyLabel.TextAlign = ContentAlignment.MiddleLeft;
            cardOptions.Controls.Add(toggleKeyLabel);

            _toggleHotkeyBadge = new Label();
            _toggleHotkeyBadge.BackColor = ThemeColors.InputBg;
            _toggleHotkeyBadge.ForeColor = ThemeColors.TextPrimary;
            _toggleHotkeyBadge.BorderStyle = BorderStyle.FixedSingle;
            _toggleHotkeyBadge.Font = new Font("Consolas", 9.5F, FontStyle.Bold);
            _toggleHotkeyBadge.TextAlign = ContentAlignment.MiddleCenter;
            _toggleHotkeyBadge.Location = new Point(116, 15);
            _toggleHotkeyBadge.Size = new Size(106, 28);
            cardOptions.Controls.Add(_toggleHotkeyBadge);

            _toggleHotkeyButton = new ModernButton();
            _toggleHotkeyButton.Text = "변경";
            _toggleHotkeyButton.Font = new Font("Malgun Gothic", 8.5F, FontStyle.Bold);
            _toggleHotkeyButton.Location = new Point(228, 15);
            _toggleHotkeyButton.Size = new Size(56, 28);
            cardOptions.Controls.Add(_toggleHotkeyButton);

            Label delayLabel = new Label();
            delayLabel.Text = "유지 시간";
            delayLabel.Font = new Font("Malgun Gothic", 9F, FontStyle.Bold);
            delayLabel.ForeColor = ThemeColors.TextPrimary;
            delayLabel.BackColor = ThemeColors.CardBg;
            delayLabel.Location = new Point(304, 16);
            delayLabel.Size = new Size(64, 26);
            delayLabel.TextAlign = ContentAlignment.MiddleLeft;
            cardOptions.Controls.Add(delayLabel);

            _delayTrackBar = new TrackBar();
            _delayTrackBar.Minimum = AppSettings.MinTapDelayMs;
            _delayTrackBar.Maximum = AppSettings.MaxTapDelayMs;
            _delayTrackBar.TickFrequency = 5;
            _delayTrackBar.TickStyle = TickStyle.None;
            _delayTrackBar.BackColor = ThemeColors.CardBg;
            _delayTrackBar.Location = new Point(368, 16);
            _delayTrackBar.Size = new Size(134, 26);
            cardOptions.Controls.Add(_delayTrackBar);

            _delayNumeric = new NumericUpDown();
            _delayNumeric.Minimum = AppSettings.MinTapDelayMs;
            _delayNumeric.Maximum = AppSettings.MaxTapDelayMs;
            _delayNumeric.BackColor = ThemeColors.InputBg;
            _delayNumeric.ForeColor = ThemeColors.TextPrimary;
            _delayNumeric.BorderStyle = BorderStyle.FixedSingle;
            _delayNumeric.Font = new Font("Consolas", 9.5F, FontStyle.Bold);
            _delayNumeric.Location = new Point(508, 17);
            _delayNumeric.Size = new Size(54, 24);
            cardOptions.Controls.Add(_delayNumeric);

            Label msUnitLabel = new Label();
            msUnitLabel.Text = "ms";
            msUnitLabel.Font = new Font("Malgun Gothic", 8.8F, FontStyle.Regular);
            msUnitLabel.ForeColor = ThemeColors.TextSecondary;
            msUnitLabel.BackColor = ThemeColors.CardBg;
            msUnitLabel.Location = new Point(566, 19);
            msUnitLabel.AutoSize = true;
            cardOptions.Controls.Add(msUnitLabel);

            Panel divider3 = new Panel();
            divider3.BackColor = ThemeColors.Divider;
            divider3.Location = new Point(16, 52);
            divider3.Size = new Size(cardOptions.Width - 32, 1);
            cardOptions.Controls.Add(divider3);

            // Row 2: Target games + foreground status
            _nightreignCheckBox = new CheckBox();
            _nightreignCheckBox.Text = "밤의 통치자 (nightreign.exe)";
            _nightreignCheckBox.ForeColor = ThemeColors.TextPrimary;
            _nightreignCheckBox.BackColor = ThemeColors.CardBg;
            _nightreignCheckBox.Font = new Font("Malgun Gothic", 8.7F, FontStyle.Regular);
            _nightreignCheckBox.Location = new Point(16, 61);
            _nightreignCheckBox.AutoSize = true;
            cardOptions.Controls.Add(_nightreignCheckBox);

            _eldenRingCheckBox = new CheckBox();
            _eldenRingCheckBox.Text = "엘든링 본편 (eldenring.exe)";
            _eldenRingCheckBox.ForeColor = ThemeColors.TextPrimary;
            _eldenRingCheckBox.BackColor = ThemeColors.CardBg;
            _eldenRingCheckBox.Font = new Font("Malgun Gothic", 8.7F, FontStyle.Regular);
            _eldenRingCheckBox.Location = new Point(214, 61);
            _eldenRingCheckBox.AutoSize = true;
            cardOptions.Controls.Add(_eldenRingCheckBox);

            _foregroundStatusLabel = new Label();
            _foregroundStatusLabel.Text = "활성 창: -";
            _foregroundStatusLabel.Font = new Font("Malgun Gothic", 8F, FontStyle.Regular);
            _foregroundStatusLabel.ForeColor = ThemeColors.TextSecondary;
            _foregroundStatusLabel.BackColor = ThemeColors.CardBg;
            _foregroundStatusLabel.TextAlign = ContentAlignment.MiddleRight;
            _foregroundStatusLabel.Location = new Point(402, 62);
            _foregroundStatusLabel.Size = new Size(cardOptions.Width - 418, 18);
            cardOptions.Controls.Add(_foregroundStatusLabel);

            Label customProcLabel = new Label();
            customProcLabel.Text = "추가 프로세스 (.exe, 쉼표 구분):";
            customProcLabel.Font = new Font("Malgun Gothic", 8.2F, FontStyle.Regular);
            customProcLabel.ForeColor = ThemeColors.TextSecondary;
            customProcLabel.BackColor = ThemeColors.CardBg;
            customProcLabel.Location = new Point(16, 86);
            customProcLabel.AutoSize = true;
            cardOptions.Controls.Add(customProcLabel);

            _customProcessesTextBox = new TextBox();
            _customProcessesTextBox.BackColor = ThemeColors.InputBg;
            _customProcessesTextBox.ForeColor = ThemeColors.TextPrimary;
            _customProcessesTextBox.BorderStyle = BorderStyle.FixedSingle;
            _customProcessesTextBox.Font = new Font("Consolas", 8.8F, FontStyle.Regular);
            _customProcessesTextBox.Location = new Point(206, 83);
            _customProcessesTextBox.Size = new Size(cardOptions.Width - 222, 22);
            cardOptions.Controls.Add(_customProcessesTextBox);

            // ===== Footer =====
            _minimizeOnCloseCheckBox = new CheckBox();
            _minimizeOnCloseCheckBox.Text = "창 닫기(X) 시 트레이로 최소화";
            _minimizeOnCloseCheckBox.ForeColor = ThemeColors.TextSecondary;
            _minimizeOnCloseCheckBox.BackColor = ThemeColors.Background;
            _minimizeOnCloseCheckBox.Font = new Font("Malgun Gothic", 8.5F, FontStyle.Regular);
            _minimizeOnCloseCheckBox.Location = new Point(margin, 696);
            _minimizeOnCloseCheckBox.AutoSize = true;
            Controls.Add(_minimizeOnCloseCheckBox);

            _configPathLabel = new Label();
            _configPathLabel.Text = "설정: " + ShortenPath(_controller.SettingsPath, 38);
            _configPathLabel.Font = new Font("Malgun Gothic", 7.8F, FontStyle.Regular);
            _configPathLabel.ForeColor = ThemeColors.TextDisabled;
            _configPathLabel.BackColor = ThemeColors.Background;
            _configPathLabel.TextAlign = ContentAlignment.MiddleCenter;
            _configPathLabel.Location = new Point(214, 698);
            _configPathLabel.Size = new Size(256, 18);
            Controls.Add(_configPathLabel);

            _hideToTrayButton = new ModernButton();
            _hideToTrayButton.Text = "트레이로 숨기기";
            _hideToTrayButton.Font = new Font("Malgun Gothic", 8.6F, FontStyle.Bold);
            _hideToTrayButton.Location = new Point(ClientSize.Width - margin - 118, 692);
            _hideToTrayButton.Size = new Size(118, 28);
            Controls.Add(_hideToTrayButton);

            ResumeLayout(false);
        }

        private void RebuildActionRows()
        {
            _actionsScrollPanel.SuspendLayout();
            try
            {
                _actionsScrollPanel.Controls.Clear();
                _actionRows.Clear();

                List<ActionBindingItem> actions = _controller.GetActionsSnapshot();
                int y = 6;
                bool needsScroll = actions.Count > 7;
                int rowWidth = _actionsScrollPanel.ClientSize.Width - (needsScroll ? SystemInformation.VerticalScrollBarWidth : 0) - 10;
                if (rowWidth < 520)
                {
                    rowWidth = 540;
                }

                bool showedCustomHeader = false;
                for (int i = 0; i < actions.Count; i++)
                {
                    ActionBindingItem item = actions[i];
                    if (item.IsCustom && !showedCustomHeader)
                    {
                        showedCustomHeader = true;
                        Panel catPanel = new Panel();
                        catPanel.Location = new Point(5, y);
                        catPanel.Size = new Size(rowWidth, 24);
                        catPanel.BackColor = ThemeColors.InputBg;

                        Label catLabel = new Label();
                        catLabel.Text = "▸  " + ActionBindingItem.CategoryCustom;
                        catLabel.Font = new Font("Malgun Gothic", 8.5F, FontStyle.Bold);
                        catLabel.ForeColor = ThemeColors.Gold;
                        catLabel.BackColor = ThemeColors.InputBg;
                        catLabel.Location = new Point(4, 4);
                        catLabel.AutoSize = true;
                        catPanel.Controls.Add(catLabel);

                        Panel catLine = new Panel();
                        catLine.BackColor = ThemeColors.Divider;
                        catLine.Location = new Point(4, 22);
                        catLine.Size = new Size(rowWidth - 8, 1);
                        catPanel.Controls.Add(catLine);

                        _actionsScrollPanel.Controls.Add(catPanel);
                        y += 26;
                    }

                    ActionRowWidgets row = CreateActionRow(item, y, rowWidth);
                    _actionsScrollPanel.Controls.Add(row.RowPanel);
                    _actionRows[item.Id] = row;
                    y += 35;
                }

                Panel spacer = new Panel();
                spacer.Location = new Point(0, y);
                spacer.Size = new Size(10, 4);
                _actionsScrollPanel.Controls.Add(spacer);
            }
            finally
            {
                _actionsScrollPanel.ResumeLayout(true);
            }
        }

        private ActionRowWidgets CreateActionRow(ActionBindingItem item, int y, int rowWidth)
        {
            string actionId = item.Id;
            bool isCustom = item.IsCustom;

            Panel rowPanel = new Panel();
            rowPanel.Location = new Point(4, y);
            rowPanel.Size = new Size(rowWidth, 31);
            rowPanel.BackColor = item.Enabled ? Color.FromArgb(24, 42, 38) : Color.FromArgb(30, 33, 40);

            Panel accentBar = new Panel();
            accentBar.Location = new Point(0, 0);
            accentBar.Size = new Size(3, 31);
            accentBar.BackColor = item.Enabled ? ThemeColors.Emerald : Color.Transparent;
            rowPanel.Controls.Add(accentBar);

            CheckBox chk = new CheckBox();
            chk.Location = new Point(10, 6);
            chk.Size = new Size(18, 18);
            chk.BackColor = rowPanel.BackColor;
            chk.Checked = item.Enabled;
            rowPanel.Controls.Add(chk);

            Label nameLabel = new Label();
            nameLabel.Text = item.Name;
            nameLabel.Font = new Font("Malgun Gothic", 8.8F, item.Enabled ? FontStyle.Bold : FontStyle.Regular);
            nameLabel.ForeColor = item.Enabled ? ThemeColors.TextPrimary : ThemeColors.TextSecondary;
            nameLabel.BackColor = rowPanel.BackColor;
            nameLabel.Location = new Point(32, 0);
            nameLabel.Size = new Size(212, 31);
            nameLabel.TextAlign = ContentAlignment.MiddleLeft;
            nameLabel.Cursor = Cursors.Hand;
            nameLabel.Click += delegate
            {
                chk.Checked = !chk.Checked;
            };
            rowPanel.Controls.Add(nameLabel);

            Label keyBadge = new Label();
            keyBadge.BackColor = ThemeColors.Background;
            keyBadge.ForeColor = item.Binding != null ? ThemeColors.TextPrimary : ThemeColors.TextDisabled;
            keyBadge.BorderStyle = BorderStyle.FixedSingle;
            keyBadge.Font = new Font("Consolas", 8.8F, FontStyle.Bold);
            keyBadge.TextAlign = ContentAlignment.MiddleCenter;
            keyBadge.Location = new Point(250, 4);
            keyBadge.Size = new Size(156, 23);
            keyBadge.Text = item.Binding != null ? item.Binding.FormatBadgeText() : "미지정";
            rowPanel.Controls.Add(keyBadge);

            ModernButton changeBtn = new ModernButton();
            changeBtn.Text = "키 변경";
            changeBtn.Font = new Font("Malgun Gothic", 8.1F, FontStyle.Bold);
            changeBtn.CornerRadius = 5;
            changeBtn.Location = new Point(414, 3);
            changeBtn.Size = new Size(72, 25);
            rowPanel.Controls.Add(changeBtn);

            ModernButton clearBtn = new ModernButton();
            clearBtn.Text = isCustom ? "삭제" : "해제";
            clearBtn.Font = new Font("Malgun Gothic", 8.1F, FontStyle.Regular);
            clearBtn.CornerRadius = 5;
            clearBtn.Location = new Point(492, 3);
            clearBtn.Size = new Size(52, 25);
            if (isCustom)
            {
                clearBtn.ForeColor = Color.FromArgb(248, 113, 113);
            }
            rowPanel.Controls.Add(clearBtn);

            chk.CheckedChanged += delegate
            {
                if (_updatingUi) return;
                string err;
                bool ok = _controller.TrySetActionEnabled(actionId, chk.Checked, out err);
                if (!ok)
                {
                    SetFeedback(err ?? "상태를 변경할 수 없습니다.", ThemeColors.Danger);
                    SyncActionRowValuesFromController();
                }
                else
                {
                    SetFeedback(
                        chk.Checked
                            ? "'" + nameLabel.Text + "' 즉시 발동이 켜졌습니다."
                            : "'" + nameLabel.Text + "' 즉시 발동이 꺼졌습니다 (원본 입력 통과).",
                        chk.Checked ? ThemeColors.Emerald : ThemeColors.TextSecondary);
                }
            };

            changeBtn.Click += delegate
            {
                if (_hookEngine.CurrentCaptureMode == CaptureMode.ActionKey &&
                    string.Equals(_hookEngine.CaptureTargetActionId, actionId, StringComparison.OrdinalIgnoreCase))
                {
                    _hookEngine.CancelCapture();
                }
                else
                {
                    StartActionKeyCapture(actionId, nameLabel.Text);
                }
            };

            clearBtn.Click += delegate
            {
                _hookEngine.CancelCapture();
                if (isCustom)
                {
                    _controller.RemoveCustomAction(actionId);
                    RebuildActionRows();
                    SyncUiFromController();
                    SetFeedback("사용자 키 항목이 삭제되었습니다.", ThemeColors.TextSecondary);
                }
                else
                {
                    _controller.ClearActionKey(actionId);
                    SetFeedback("'" + nameLabel.Text + "' 키 지정이 해제되었습니다.", ThemeColors.TextSecondary);
                }
            };

            ActionRowWidgets widgets = new ActionRowWidgets();
            widgets.ActionId = actionId;
            widgets.IsCustom = isCustom;
            widgets.RowPanel = rowPanel;
            widgets.AccentBar = accentBar;
            widgets.EnabledCheckBox = chk;
            widgets.NameLabel = nameLabel;
            widgets.KeyBadge = keyBadge;
            widgets.ChangeKeyButton = changeBtn;
            widgets.ClearOrDeleteButton = clearBtn;
            return widgets;
        }

        private void SyncActionRowValuesFromController()
        {
            List<ActionBindingItem> actions = _controller.GetActionsSnapshot();
            if (actions.Count != _actionRows.Count)
            {
                RebuildActionRows();
            }

            bool prevUpdating = _updatingUi;
            _updatingUi = true;
            try
            {
                for (int i = 0; i < actions.Count; i++)
                {
                    ActionBindingItem item = actions[i];
                    ActionRowWidgets row;
                    if (!_actionRows.TryGetValue(item.Id, out row))
                    {
                        continue;
                    }

                    Color rowBg = item.Enabled ? Color.FromArgb(24, 42, 38) : Color.FromArgb(30, 33, 40);
                    row.RowPanel.BackColor = rowBg;
                    row.AccentBar.BackColor = item.Enabled ? ThemeColors.Emerald : Color.Transparent;
                    row.EnabledCheckBox.BackColor = rowBg;
                    row.EnabledCheckBox.Checked = item.Enabled;
                    row.NameLabel.BackColor = rowBg;
                    row.NameLabel.ForeColor = item.Enabled ? ThemeColors.TextPrimary : ThemeColors.TextSecondary;
                    row.NameLabel.Font = new Font("Malgun Gothic", 8.8F, item.Enabled ? FontStyle.Bold : FontStyle.Regular);

                    if (item.Binding != null)
                    {
                        row.KeyBadge.Text = item.Binding.FormatBadgeText();
                        row.KeyBadge.ForeColor = item.Enabled ? ThemeColors.Emerald : ThemeColors.TextPrimary;
                    }
                    else
                    {
                        row.KeyBadge.Text = "미지정";
                        row.KeyBadge.ForeColor = ThemeColors.TextDisabled;
                    }

                    bool isCapturingThis = (_hookEngine.CurrentCaptureMode == CaptureMode.ActionKey &&
                        string.Equals(_hookEngine.CaptureTargetActionId, item.Id, StringComparison.OrdinalIgnoreCase));
                    row.ChangeKeyButton.Text = isCapturingThis ? "대기 중..." : "키 변경";
                }
            }
            finally
            {
                _updatingUi = prevUpdating;
            }
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

            _addCustomButton.Click += delegate
            {
                _hookEngine.CancelCapture();
                ActionBindingItem added = _controller.AddCustomAction(null);
                RebuildActionRows();
                SyncUiFromController();

                ActionRowWidgets row;
                if (_actionRows.TryGetValue(added.Id, out row))
                {
                    _actionsScrollPanel.ScrollControlIntoView(row.RowPanel);
                }
                StartActionKeyCapture(added.Id, added.Name);
            };

            _resetDefaultsButton.Click += delegate
            {
                _hookEngine.CancelCapture();
                DialogResult result = MessageBox.Show(
                    this,
                    "모든 액션 키와 체크 상태를 엘밤통 기본값으로 초기화할까요?\n(사용자 추가 키 항목도 함께 삭제됩니다.)",
                    "기본값 초기화",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question);
                if (result == DialogResult.Yes)
                {
                    _controller.ResetActionsToDefaults();
                    RebuildActionRows();
                    SyncUiFromController();
                    SetFeedback("엘밤통 기본 키 목록으로 초기화되었습니다.", ThemeColors.Gold);
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
                    StartToggleHotkeyCapture();
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

        private void StartActionKeyCapture(string actionId, string actionName)
        {
            _hookEngine.BeginActionKeyCapture(actionId);
            _toggleHotkeyButton.Text = "변경";
            SyncActionRowValuesFromController();
            SetFeedback(
                "[" + actionName + "] 원하는 키 또는 마우스 버튼(좌/우/휠/Mouse4/Mouse5)을 누르세요... (ESC 취소)",
                ThemeColors.Gold);
        }

        private void StartToggleHotkeyCapture()
        {
            _hookEngine.BeginToggleHotkeyCapture();
            _toggleHotkeyButton.Text = "대기...";
            SyncActionRowValuesFromController();
            SetFeedback("원하는 ON/OFF 단축키(키보드)를 누르세요... (ESC 취소)", ThemeColors.Gold);
        }

        private void HandleKeyCaptured(KeyCapturedEventArgs args)
        {
            _toggleHotkeyButton.Text = "변경";
            _hookEngine.RefreshMouseHookState();
            SyncActionRowValuesFromController();

            if (args.Cancelled || args.CapturedBinding == null)
            {
                SetFeedback("키 변경이 취소되었습니다.", ThemeColors.TextSecondary);
                return;
            }

            string error;
            if (args.Mode == CaptureMode.ActionKey)
            {
                bool ok = _controller.TrySetActionKey(args.TargetActionId, args.CapturedBinding, out error);
                if (ok)
                {
                    SetFeedback("키가 [" + args.CapturedBinding.FormatBadgeText() + "]로 저장되었습니다.", ThemeColors.Emerald);
                }
                else
                {
                    SetFeedback(error ?? "키를 변경할 수 없습니다.", ThemeColors.Danger);
                }
            }
            else if (args.Mode == CaptureMode.ToggleHotkey)
            {
                bool ok = _controller.TrySetToggleHotkey(args.CapturedBinding, out error);
                if (ok)
                {
                    SetFeedback("ON/OFF 단축키가 [" + args.CapturedBinding.FormatBadgeText() + "]로 저장되었습니다.", ThemeColors.Emerald);
                }
                else
                {
                    SetFeedback(error ?? "단축키를 변경할 수 없습니다.", ThemeColors.Danger);
                }
            }
        }

        private void SetFeedback(string message, Color color)
        {
            _actionFeedbackLabel.ForeColor = color;
            _actionFeedbackLabel.Text = message;
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

                string summary = _controller.FormatActiveKeysSummary(3);
                _activeSummaryLabel.Text = summary;
                _activeSummaryLabel.ForeColor = _controller.GetActiveActionCount() > 0 ? ThemeColors.Emerald : ThemeColors.TextSecondary;
                _listActiveCountLabel.Text = _controller.FormatActiveKeysSummary(1);
                _listActiveCountLabel.ForeColor = _controller.GetActiveActionCount() > 0 ? ThemeColors.Emerald : ThemeColors.TextSecondary;

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

            SyncActionRowValuesFromController();
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
                "콩벌레 [{0}] {1} | {2}",
                statusKo,
                _controller.FormatCompactActiveKeysForTray(),
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
