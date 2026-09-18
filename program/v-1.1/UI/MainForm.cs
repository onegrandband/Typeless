using System.Drawing.Imaging;
using System.Diagnostics;
using AutoTyper.Core;
using AutoTyper.Helpers;
using System.Drawing.Drawing2D;

namespace AutoTyper.UI;

public class MainForm : Form
{
    // P/Invoke
    [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    private const int WM_HOTKEY = 0x0312;

    private delegate void WinEventDelegate(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime);
    [DllImport("user32.dll")] private static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr hmodWinEventProc, WinEventDelegate lpfnWinEventProc, uint idProcess, uint idThread, uint dwFlags);
    [DllImport("user32.dll")] private static extern bool UnhookWinEvent(IntPtr hWinEventHook);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_SHOWWINDOW = 0x0040;

    private const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
    private const uint WINEVENT_OUTOFCONTEXT = 0x0000;
    private const int SW_SHOW = 5;

    private IntPtr winEventHook = IntPtr.Zero;
    private WinEventDelegate? winEventDelegate;
    private IntPtr lastExternalForeground = IntPtr.Zero;

    private TextBox inputBox = null!;
    private NumericUpDown wpsControl = null!;
    private Button startButton = null!, stopButton = null!;
    private ComboBox modeBox = null!;
    private Label statusLabel = null!;
    private TextBox startHotkeyBox = null!, stopHotkeyBox = null!;
    private Button registerHotkeysButton = null!;
    private CheckBox loopCheck = null!;
    private ComboBox rateUnitBox = null!;

    private Button captureReadButton = null!;
    private ComboBox captureAreaBox = null!;
    private CheckBox autoLoopOCRCheck = null!;
    private Label ocrStatusLabel = null!;

    private Panel titleBar = null!;
    private Label titleLabel = null!;
    private Button minButton = null!, maxButton = null!, closeButton = null!;
    private bool dragging = false;
    private Point dragStart;
    private Dictionary<Button, bool> titleHover = new();
    private Dictionary<Button, bool> titlePressed = new();

    private CheckBox partyModeCheck = null!;
    private System.Windows.Forms.Timer partyTimer = null!;
    private Random rng = new();
    private NotifyIcon trayIcon = null!;
    private CheckBox emojiCheck = null!;
    private ComboBox emojiBox = null!;
    private Button shuffleButton = null!, previewButton = null!;
    private CheckBox autoTypeCheck = null!;

    private CancellationTokenSource? cts;
    private Task? typingTask;

    private const int HOTKEY_START_ID = 0x1000;
    private const int HOTKEY_STOP_ID = 0x1001;

    public MainForm()
    {
        InitializeComponents();
        this.FormBorderStyle = FormBorderStyle.None;
        this.StartPosition = FormStartPosition.CenterScreen;
        this.MinimumSize = new Size(600, 400);
        this.Text = "Typeless";

        // Keep overlay on top of fullscreen games/DirectX windows
        this.TopMost = true;
        this.Load += (s, e) => SetWindowPos(this.Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_SHOWWINDOW);

        try
        {
            winEventDelegate = new WinEventDelegate(WinEventProc);
            winEventHook = SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND, IntPtr.Zero, winEventDelegate, 0, 0, WINEVENT_OUTOFCONTEXT);
        }
        catch { winEventHook = IntPtr.Zero; }
    }

    private void InitializeComponents()
    {
        titleBar = new Panel { Dock = DockStyle.Top, Height = 30, BackColor = Color.FromArgb(215, 219, 233) };
        titleBar.MouseDown += TitleBar_MouseDown;
        titleBar.MouseMove += TitleBar_MouseMove;
        titleBar.MouseUp += TitleBar_MouseUp;
        this.Controls.Add(titleBar);

        titleLabel = new Label { Text = "Typeless", AutoSize = true, Location = new Point(8, 6), Font = new Font("Segoe UI", 9F) };
        titleBar.Controls.Add(titleLabel);

        minButton = CreateTitleButton("min");
        maxButton = CreateTitleButton("max");
        closeButton = CreateTitleButton("close");
        minButton.Click += TitleButtons_Click; maxButton.Click += TitleButtons_Click; closeButton.Click += TitleButtons_Click;

        inputBox = new TextBox { Multiline = true, ScrollBars = ScrollBars.Vertical, Location = new Point(12, 44), Size = new Size(560, 180) };
        this.Controls.Add(inputBox);

        var wpsLabel = new Label { Text = "Rate:", Location = new Point(12, 236), AutoSize = true };
        this.Controls.Add(wpsLabel);

        wpsControl = new NumericUpDown { Location = new Point(140, 232), Minimum = 0.01M, Maximum = 10000M, DecimalPlaces = 2, Increment = 0.5M, Value = 50.00M, Width = 80 };
        this.Controls.Add(wpsControl);

        rateUnitBox = new ComboBox { Location = new Point(230, 232), Width = 120, DropDownStyle = ComboBoxStyle.DropDownList };
        rateUnitBox.Items.AddRange(new[] { "Words per second", "Words per minute" });
        rateUnitBox.SelectedIndex = 1;
        this.Controls.Add(rateUnitBox);

        var modeLabel = new Label { Text = "Mode:", Location = new Point(12, 266), AutoSize = true };
        this.Controls.Add(modeLabel);
        modeBox = new ComboBox { Location = new Point(140, 262), Width = 200, DropDownStyle = ComboBoxStyle.DropDownList };
        modeBox.Items.AddRange(new[] { "Type (keystrokes)", "Paste (clipboard + Ctrl+V)", "Screen Reader (OCR)" });
        modeBox.SelectedIndex = 0;
        modeBox.SelectedIndexChanged += (s, e) => UpdateUIForMode();
        this.Controls.Add(modeBox);

        startButton = new Button { Text = "Start", Location = new Point(12, 300), Width = 80 };
        stopButton = new Button { Text = "Stop", Location = new Point(100, 300), Width = 80, Enabled = false };
        startButton.Click += StartButton_Click; stopButton.Click += StopButton_Click;
        this.Controls.Add(startButton); this.Controls.Add(stopButton);

        statusLabel = new Label { Text = "Status: Idle", Location = new Point(12, 340), AutoSize = true };
        this.Controls.Add(statusLabel);

        var hkLabel1 = new Label { Text = "Start hotkey (e.g. Ctrl+Alt+S):", Location = new Point(280, 232), AutoSize = true };
        this.Controls.Add(hkLabel1);
        startHotkeyBox = new TextBox { Location = new Point(280, 252), Width = 180, Text = "Ctrl+Alt+S" };
        this.Controls.Add(startHotkeyBox);

        var hkLabel2 = new Label { Text = "Stop hotkey (e.g. Ctrl+Alt+X):", Location = new Point(280, 286), AutoSize = true };
        this.Controls.Add(hkLabel2);
        stopHotkeyBox = new TextBox { Location = new Point(280, 306), Width = 180, Text = "Ctrl+Alt+X" };
        this.Controls.Add(stopHotkeyBox);

        registerHotkeysButton = new Button { Text = "Register Hotkeys", Location = new Point(480, 252), Width = 120 };
        registerHotkeysButton.Click += RegisterHotkeysButton_Click;
        this.Controls.Add(registerHotkeysButton);

        loopCheck = new CheckBox { Text = "Loop", Location = new Point(200, 300) };
        this.Controls.Add(loopCheck);

        autoTypeCheck = new CheckBox { Text = "Auto-Type", Location = new Point(360, 300), Checked = true, AutoSize = true };
        this.Controls.Add(autoTypeCheck);

        var createShortcutButton = new Button { Text = "Create Desktop Shortcut", Location = new Point(480, 220), Width = 160 };
        createShortcutButton.Click += CreateShortcutButton_Click;
        this.Controls.Add(createShortcutButton);

        partyModeCheck = new CheckBox { Text = "Party Mode", Location = new Point(12, 372) };
        partyModeCheck.CheckedChanged += (s, e) =>
        {
            if (partyModeCheck.Checked) partyTimer.Start();
            else { partyTimer.Stop(); titleBar.BackColor = Color.FromArgb(215, 219, 233); }
        };
        this.Controls.Add(partyModeCheck);

        partyTimer = new System.Windows.Forms.Timer { Interval = 300 };
        partyTimer.Tick += (s, e) => { titleBar.BackColor = Color.FromArgb(200 + rng.Next(56), rng.Next(256), rng.Next(256), rng.Next(256)); };

        emojiCheck = new CheckBox { Text = "Emoji prefix", Location = new Point(120, 372) };
        this.Controls.Add(emojiCheck);
        emojiBox = new ComboBox { Location = new Point(220, 368), Width = 120, DropDownStyle = ComboBoxStyle.DropDownList };
        emojiBox.Items.AddRange(new[] { "\ud83d\ude0a", "\ud83d\udd25", "\u2728", "\ud83c\udf89", "\ud83d\ude80" });
        emojiBox.SelectedIndex = 0;
        this.Controls.Add(emojiBox);

        shuffleButton = new Button { Text = "Shuffle", Location = new Point(480, 300), Width = 80 };
        shuffleButton.Click += (s, e) =>
        {
            var parts = inputBox.Text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            var list = new List<string>(parts);
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
            inputBox.Text = string.Join(Environment.NewLine, list);
        };
        this.Controls.Add(shuffleButton);

        previewButton = new Button { Text = "Preview", Location = new Point(560, 300), Width = 80 };
        previewButton.Click += (s, e) =>
        {
            var txt = inputBox.Text.Trim();
            if (string.IsNullOrEmpty(txt)) MessageBox.Show("No text to preview.", "Preview", MessageBoxButtons.OK, MessageBoxIcon.Information);
            else MessageBox.Show(txt.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)[0], "Preview", MessageBoxButtons.OK, MessageBoxIcon.Information);
        };
        this.Controls.Add(previewButton);

        var captureAreaLabel = new Label { Text = "Capture:", Location = new Point(12, 410), AutoSize = true };
        this.Controls.Add(captureAreaLabel);
        captureAreaBox = new ComboBox { Location = new Point(80, 406), Width = 140, DropDownStyle = ComboBoxStyle.DropDownList };
        captureAreaBox.Items.AddRange(new[] { "Full Screen", "Active Window", "Selection" });
        captureAreaBox.SelectedIndex = 0;
        this.Controls.Add(captureAreaBox);

        captureReadButton = new Button { Text = "Capture & Read", Location = new Point(230, 404), Width = 120 };
        captureReadButton.Click += CaptureReadButton_Click;
        this.Controls.Add(captureReadButton);

        autoLoopOCRCheck = new CheckBox { Text = "Auto-read loop", Location = new Point(360, 410) };
        this.Controls.Add(autoLoopOCRCheck);

        ocrStatusLabel = new Label { Text = "OCR: Ready", Location = new Point(12, 440), AutoSize = true };
        this.Controls.Add(ocrStatusLabel);

        trayIcon = new NotifyIcon { Icon = SystemIcons.Application, Visible = false, Text = "Typeless" };
        trayIcon.DoubleClick += (s, e) => { this.Show(); this.WindowState = FormWindowState.Normal; trayIcon.Visible = false; };

        this.Resize += MainForm_Resize;

        try
        {
            var fg = GetForegroundWindow();
            if (fg != IntPtr.Zero)
            {
                GetWindowThreadProcessId(fg, out uint pid);
                if (pid != (uint)Process.GetCurrentProcess().Id) lastExternalForeground = fg;
            }
        }
        catch { }

        this.ClientSize = new Size(720, 480);
        LayoutTitleButtons();
    }

    private Button CreateTitleButton(string tag)
    {
        var b = new Button { Size = new Size(30, 24), FlatStyle = FlatStyle.Flat, BackColor = Color.Transparent, TabStop = false, Tag = tag };
        b.FlatAppearance.BorderSize = 0;
        b.Paint += TitleButton_Paint;
        b.MouseEnter += (s, e) => { titleHover[b] = true; b.Invalidate(); };
        b.MouseLeave += (s, e) => { titleHover[b] = false; titlePressed[b] = false; b.Invalidate(); };
        b.MouseDown += (s, e) => { if (e.Button == MouseButtons.Left) { titlePressed[b] = true; b.Invalidate(); } };
        b.MouseUp += (s, e) => { titlePressed[b] = false; b.Invalidate(); };
        titleBar.Controls.Add(b);
        titleHover[b] = false; titlePressed[b] = false;
        return b;
    }

    private void CreateShortcutButton_Click(object? sender, EventArgs e)
    {
        try
        {
            string exePath = Application.ExecutablePath;
            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            string shortcutPath = Path.Combine(desktop, "Typeless.lnk");
            SetupHelper.CreateShortcut(exePath, "", shortcutPath);
            MessageBox.Show("Desktop shortcut created.", "Shortcut", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show("Failed to create shortcut: " + ex.Message, "Shortcut", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void UpdateUIForMode()
    {
        bool isOCRMode = (modeBox.SelectedIndex == 2);
        captureAreaBox.Visible = isOCRMode;
        captureReadButton.Visible = isOCRMode;
        autoLoopOCRCheck.Visible = isOCRMode;
        ocrStatusLabel.Visible = isOCRMode;
        inputBox.Visible = !isOCRMode;
    }

    private async void CaptureReadButton_Click(object? sender, EventArgs e)
    {
        try
        {
            ocrStatusLabel.Text = "OCR: Capturing...";
            this.Refresh();

            Bitmap? screenCapture = null;
            int captureMode = captureAreaBox.SelectedIndex;

            if (captureMode == 0)
            {
                screenCapture = ScreenCapture.CaptureScreen();
            }
            else if (captureMode == 1)
            {
                var fg = GetForegroundWindow();
                if (fg == IntPtr.Zero)
                {
                    ocrStatusLabel.Text = "OCR: No active window";
                    return;
                }
                screenCapture = ScreenCapture.CaptureWindow(fg);
                if (screenCapture == null)
                {
                    ocrStatusLabel.Text = "OCR: Failed to capture window";
                    return;
                }
            }
            else if (captureMode == 2)
            {
                using (var selector = new RegionSelector())
                {
                    if (selector.ShowDialog(this) == DialogResult.OK && selector.SelectedRegion.Width > 4 && selector.SelectedRegion.Height > 4)
                    {
                        screenCapture = ScreenCapture.CaptureRectangle(selector.SelectedRegion);
                    }
                    else
                    {
                        ocrStatusLabel.Text = "OCR: Selection cancelled/too small";
                        return;
                    }
                }
            }

            if (screenCapture == null)
            {
                ocrStatusLabel.Text = "OCR: Capture failed";
                return;
            }

            ocrStatusLabel.Text = "OCR: Recognizing text...";
            this.Refresh();

            string recognizedText = await OCRHelper.RecognizeTextAsync(screenCapture);
            screenCapture.Dispose();

            string targetWord = PromptParser.ExtractTypeThisWord(recognizedText);

            if (string.IsNullOrWhiteSpace(targetWord))
            {
                ocrStatusLabel.Text = "OCR: No 'type this' prompt found";
                return;
            }

            inputBox.Text = targetWord;
            ocrStatusLabel.Text = $"OCR: Found target \"{targetWord}\"";

            if (autoLoopOCRCheck.Checked || autoTypeCheck.Checked)
            {
                StartTyping();
            }
        }
        catch (Exception ex)
        {
            ocrStatusLabel.Text = "OCR: Error - " + ex.Message.Substring(0, Math.Min(50, ex.Message.Length));
            MessageBox.Show("OCR failed: " + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void WinEventProc(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
    {
        try
        {
            if (hwnd == IntPtr.Zero) return;
            if (!IsWindow(hwnd)) return;
            GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid != (uint)Process.GetCurrentProcess().Id)
            {
                lastExternalForeground = hwnd;
            }
        }
        catch { }
    }

    private void LayoutTitleButtons()
    {
        int right = this.ClientSize.Width - 3;
        closeButton.Location = new Point(right - closeButton.Width, 3);
        maxButton.Location = new Point(closeButton.Left - maxButton.Width - 1, 3);
        minButton.Location = new Point(maxButton.Left - minButton.Width - 1, 3);
    }

    private void MainForm_Resize(object? sender, EventArgs e)
    {
        LayoutTitleButtons();
        if (this.WindowState == FormWindowState.Minimized)
        {
            if (trayIcon != null) { trayIcon.Visible = true; trayIcon.ShowBalloonTip(1000, "Typeless", "Minimized to tray", ToolTipIcon.Info); }
            this.Hide();
        }
        else
        {
            if (trayIcon != null) trayIcon.Visible = false;
            this.Show();
        }
    }

    private void TitleButtons_Click(object? sender, EventArgs e)
    {
        var b = (Button)sender!;
        var tag = (string)b.Tag;
        if (tag == "min") this.WindowState = FormWindowState.Minimized;
        else if (tag == "max") this.WindowState = (this.WindowState == FormWindowState.Maximized) ? FormWindowState.Normal : FormWindowState.Maximized;
        else if (tag == "close") this.Close();
    }

    private void TitleButton_Paint(object? sender, PaintEventArgs e)
    {
        var b = (Button)sender!;
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.Clear(Color.Transparent);

        bool hover = titleHover.ContainsKey(b) && titleHover[b];
        bool pressed = titlePressed.ContainsKey(b) && titlePressed[b];

        string tag = (string)b.Tag;
        Color accent = tag == "close" ? Color.FromArgb(220, 70, 70) : (tag == "max" ? Color.FromArgb(240, 200, 80) : Color.FromArgb(80, 180, 90));
        if (pressed) accent = ControlPaint.Dark(accent);

        if (hover)
        {
            var hb = Color.FromArgb(160, accent.R, accent.G, accent.B);
            using var br = new SolidBrush(hb);
            e.Graphics.FillRectangle(br, new Rectangle(0, 0, b.Width, b.Height));
        }

        Rectangle r = new Rectangle(8, 8, b.Width - 16, b.Height - 16);
        using var pen = new Pen(Color.White, 2);
        if (tag == "min") e.Graphics.DrawLine(pen, r.Left, r.Bottom - 1, r.Right, r.Bottom - 1);
        else if (tag == "max") e.Graphics.DrawRectangle(pen, r);
        else { e.Graphics.DrawLine(pen, r.Left, r.Top, r.Right, r.Bottom); e.Graphics.DrawLine(pen, r.Right, r.Top, r.Left, r.Bottom); }
    }

    private void TitleBar_MouseDown(object? sender, MouseEventArgs e) { if (e.Button == MouseButtons.Left) { dragging = true; dragStart = e.Location; } }
    private void TitleBar_MouseMove(object? sender, MouseEventArgs e) { if (dragging) this.Location = new Point(this.Location.X + e.X - dragStart.X, this.Location.Y + e.Y - dragStart.Y); }
    private void TitleBar_MouseUp(object? sender, MouseEventArgs e) { dragging = false; }

    private void RegisterHotkeysButton_Click(object? sender, EventArgs e)
    {
        UnregisterAllHotkeys();
        bool ok1 = TryParseHotkey(startHotkeyBox.Text.Trim(), out uint mods1, out uint vk1);
        bool ok2 = TryParseHotkey(stopHotkeyBox.Text.Trim(), out uint mods2, out uint vk2);
        if (ok1) RegisterHotKey(this.Handle, HOTKEY_START_ID, mods1, vk1);
        if (ok2) RegisterHotKey(this.Handle, HOTKEY_STOP_ID, mods2, vk2);
        MessageBox.Show("Hotkeys registered (if valid). Use Start/Stop buttons or hotkeys.", "Hotkeys", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void UnregisterAllHotkeys()
    {
        UnregisterHotKey(this.Handle, HOTKEY_START_ID);
        UnregisterHotKey(this.Handle, HOTKEY_STOP_ID);
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_HOTKEY)
        {
            int id = m.WParam.ToInt32();
            if (id == HOTKEY_START_ID) Invoke(new Action(() => StartTyping()));
            if (id == HOTKEY_STOP_ID) Invoke(new Action(() => StopTyping()));
        }
        base.WndProc(ref m);
    }

    private bool TryParseHotkey(string text, out uint mods, out uint vk)
    {
        mods = 0; vk = 0;
        if (string.IsNullOrEmpty(text)) return false;
        var parts = text.Split('+');
        string keyPart = parts[^1].Trim();
        for (int i = 0; i < parts.Length - 1; i++)
        {
            var p = parts[i].Trim().ToLowerInvariant();
            if (p == "ctrl" || p == "control") mods |= 0x0002;
            else if (p == "alt") mods |= 0x0001;
            else if (p == "shift") mods |= 0x0004;
            else if (p == "win" || p == "windows") mods |= 0x0008;
        }
        try { Keys k = (Keys)Enum.Parse(typeof(Keys), keyPart, true); vk = (uint)k; return true; }
        catch { return false; }
    }

    private void StartButton_Click(object? sender, EventArgs e) => StartTyping();
    private void StopButton_Click(object? sender, EventArgs e) => StopTyping();

    private void StartTyping()
    {
        if (typingTask != null && !typingTask.IsCompleted) return;
        string text = inputBox.Text;
        if (string.IsNullOrWhiteSpace(text)) { MessageBox.Show("Please enter words or phrases to auto-type.", "Input required", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }

        var words = text.Split(new[] { '\r', '\n', '\t', ' ' }, StringSplitOptions.RemoveEmptyEntries).ToList();
        if (words.Count == 0) { MessageBox.Show("No words found after parsing input.", "Input error", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }

        double rateVal = (double)wpsControl.Value;
        double wps = rateUnitBox?.SelectedIndex == 1 ? rateVal / 60.0 : rateVal;
        if (wps < 0.01) wps = 0.01;
        if (wps > 1000) wps = 1000;

        int mode = modeBox.SelectedIndex;
        if (mode == 2) mode = 1; // OCR results are pasted

        try
        {
            if (lastExternalForeground != IntPtr.Zero && IsWindow(lastExternalForeground))
            {
                ShowWindow(lastExternalForeground, SW_SHOW);
                SetForegroundWindow(lastExternalForeground);
                Thread.Sleep(100);
            }
            else
            {
                var fg = GetForegroundWindow();
                if (fg != IntPtr.Zero)
                {
                    GetWindowThreadProcessId(fg, out uint pid);
                    if (pid != (uint)Process.GetCurrentProcess().Id)
                        lastExternalForeground = fg;
                }
            }
        }
        catch { }

        cts = new CancellationTokenSource();
        typingTask = Task.Run(() => TypingLoop(words, wps, mode, cts.Token));
        startButton.Enabled = false; stopButton.Enabled = true; statusLabel.Text = "Status: Running";
    }

    private void StopTyping()
    {
        cts?.Cancel();
        startButton.Enabled = true; stopButton.Enabled = false; statusLabel.Text = "Status: Stopped";
    }

    private async Task TypingLoop(List<string> words, double wps, int mode, CancellationToken token)
    {
        double intervalMs = 1000.0 / wps;
        var sw = new Stopwatch();
        try
        {
            do
            {
                foreach (var w in words)
                {
                    if (token.IsCancellationRequested) return;
                    string toSend = w;
                    if (emojiCheck != null && emojiCheck.Checked && emojiBox != null && emojiBox.SelectedItem != null)
                        toSend = emojiBox.SelectedItem.ToString() + " " + toSend;

                    if (mode == 0)
                    {
                        int units = Math.Max(1, toSend.Length + 1);
                        double perUnitMs = intervalMs / units;
                        foreach (var ch in toSend)
                        {
                            if (token.IsCancellationRequested) return;
                            sw.Restart();
                            InputHelper.SendCharByInput(ch);
                            sw.Stop();
                            double remaining = perUnitMs - sw.Elapsed.TotalMilliseconds;
                            if (remaining > 1)
                            {
                                try { await Task.Delay(TimeSpan.FromMilliseconds(remaining), token); }
                                catch (TaskCanceledException) { return; }
                            }
                            else { await Task.Yield(); }
                        }

                        if (token.IsCancellationRequested) return;
                        sw.Restart();
                        InputHelper.SendKey((ushort)Keys.Space);
                        sw.Stop();
                        double remSpace = perUnitMs - sw.Elapsed.TotalMilliseconds;
                        if (remSpace > 1)
                        {
                            try { await Task.Delay(TimeSpan.FromMilliseconds(remSpace), token); }
                            catch (TaskCanceledException) { return; }
                        }
                        else { await Task.Yield(); }
                    }
                    else
                    {
                        sw.Restart();
                        await InputHelper.PasteStringAsync(toSend + " ");
                        sw.Stop();

                        double elapsed = sw.Elapsed.TotalMilliseconds;
                        double remaining = intervalMs - elapsed;
                        if (remaining > 1)
                        {
                            try { await Task.Delay(TimeSpan.FromMilliseconds(remaining), token); }
                            catch (TaskCanceledException) { return; }
                        }
                        else { await Task.Yield(); }
                    }
                }
            } while (loopCheck.Checked && !token.IsCancellationRequested);
        }
        catch (TaskCanceledException) { }
        finally { Invoke(new Action(() => { startButton.Enabled = true; stopButton.Enabled = false; statusLabel.Text = "Status: Idle"; })); }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        UnregisterAllHotkeys();
        cts?.Cancel();
        try { if (winEventHook != IntPtr.Zero) UnhookWinEvent(winEventHook); } catch { }
        base.OnFormClosing(e);
    }
}
