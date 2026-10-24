using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Text;
using System.IO;
using System.Net.NetworkInformation;
using System.Windows.Forms;
using Microsoft.Win32;

namespace NetQuotaManager
{
    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            bool startInBackground = args.Length > 0 && args[0] == "-background";
            Application.SetHighDpiMode(HighDpiMode.SystemAware);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm(startInBackground));
        }
    }

    public partial class MainForm : Form
    {
        private static readonly string DataFilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NetQuotaData.txt");
        private string adminPass = "009855Vh"; 
        private int warningThreshold = 90; 
        private long monthlyLimitBytes = 0; 
        private long currentUsageBytes = 0;
        private long lastRecordedBytes = -1;
        private bool isInternetCut = false;
        private bool hasShownWarning = false; 
        private List<string> disabledInterfaces = new List<string>();

        // فونت اختصاصی
        private PrivateFontCollection pfc = new PrivateFontCollection();
        private FontFamily iranSansFamily = null;

        // پنل‌های اصلی
        private Panel pnlTopBar;
        private Panel pnlSidebar;
        private Panel pnlContentArea;
        
        // صفحات
        private Panel pnlDashboard;
        private Panel pnlSettings;
        private Panel pnlMonitoring;
        private Panel pnlAlerts;
        private Panel pnlAccessControl;
        
        private Label lblPageTitle;
        private Label valUsage, valRemaining, valStatus;
        private Label lblDownSpeed, lblUpSpeed;
        private Panel barDownSpeed, barUpSpeed;
        private ListBox lstAlertsLog;
        
        private long prevReceivedBytes = 0;
        private long prevSentBytes = 0;

        private NotifyIcon trayIcon;
        private bool isAppVisible = false;
        private System.Windows.Forms.Timer networkTimer;
        private System.Windows.Forms.Timer speedTimer; 

        // پالت رنگی حرفه‌ای (Dark/Light Modern Theme)
        private Color colorSidebar = Color.FromArgb(15, 23, 42); 
        private Color colorSidebarHover = Color.FromArgb(30, 41, 59);
        private Color colorBg = Color.FromArgb(248, 250, 252); 
        private Color colorCard = Color.White;
        private Color colorText = Color.FromArgb(15, 23, 42);
        private Color colorTextMuted = Color.FromArgb(100, 116, 139);
        private Color colorAccent = Color.FromArgb(59, 130, 246); 
        private Color colorSuccess = Color.FromArgb(16, 185, 129);
        private Color colorDanger = Color.FromArgb(239, 68, 68);

        public MainForm(bool startInBackground)
        {
            this.AutoScaleMode = AutoScaleMode.None;
            isAppVisible = false; 
            
            LoadCustomFont();
            LoadData();
            RegisterStartupRobust(); 
            InitializeNetworkBaseline();

            trayIcon = new NotifyIcon();
            try { trayIcon.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); this.Icon = trayIcon.Icon; } 
            catch { trayIcon.Icon = SystemIcons.Shield; }
            trayIcon.Text = "NetQuota Manager - فعال";
            trayIcon.Visible = true;

            ContextMenuStrip trayMenu = new ContextMenuStrip();
            trayMenu.Items.Add("باز کردن پنل مدیریت", null, (s, e) => OpenAdminPanel());
            trayMenu.Items.Add("خروج از برنامه", null, (s, e) => ExitApplication());
            trayIcon.ContextMenuStrip = trayMenu;
            trayIcon.DoubleClick += (s, e) => OpenAdminPanel();

            this.WindowState = FormWindowState.Minimized;
            this.ShowInTaskbar = false;
            this.FormBorderStyle = FormBorderStyle.FixedSingle;
            this.MaximizeBox = false;

            BuildModernUI();
            ApplyGlobalFont(this); // اعمال قطعی فونت به تمام اجزای ساخته شده

            this.Load += (s, e) => { if (!startInBackground) BeginInvoke(new Action(OpenAdminPanel)); };
            this.FormClosing += (s, e) => { if (e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; isAppVisible = false; this.Hide(); } };
            this.Resize += (s, e) => { if (this.WindowState == FormWindowState.Minimized) { isAppVisible = false; this.Hide(); } };

            networkTimer = new System.Windows.Forms.Timer() { Interval = 10000 };
            networkTimer.Tick += NetworkTimer_Tick;
            networkTimer.Start();

            speedTimer = new System.Windows.Forms.Timer() { Interval = 1000 };
            speedTimer.Tick += SpeedTimer_Tick;

            LogAlert("سرویس مدیریت اینترنت فعال شد.");
        }

        private void LoadCustomFont()
        {
            try {
                string fontPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "iransans.ttf");
                if (File.Exists(fontPath)) { pfc.AddFontFile(fontPath); iranSansFamily = pfc.Families[0]; }
            } catch { }
        }

        private Font GetFont(float size, FontStyle style = FontStyle.Regular)
        {
            if (iranSansFamily != null) return new Font(iranSansFamily, size, style);
            return new Font("Tahoma", size, style);
        }

        // تابع بازگشتی برای اعمال قطعی فونت روی تمام قسمت‌های برنامه
        private void ApplyGlobalFont(Control parent)
        {
            foreach (Control c in parent.Controls)
            {
                if (c is Label || c is Button || c is TextBox || c is ListBox || c is NumericUpDown)
                {
                    c.Font = GetFont(c.Font.Size, c.Font.Style);
                }
                ApplyGlobalFont(c); // بررسی زیرمجموعه‌ها
            }
        }

        // متد رسم کادرهای لبه‌گرد (Rounded Corners) برای زیبایی UI
        private void RoundedPanel_Paint(object sender, PaintEventArgs e)
        {
            Panel p = sender as Panel;
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using (var path = new System.Drawing.Drawing2D.GraphicsPath())
            {
                int r = 15; // شعاع گردی
                path.AddArc(0, 0, r, r, 180, 90);
                path.AddArc(p.Width - r, 0, r, r, 270, 90);
                path.AddArc(p.Width - r, p.Height - r, r, r, 0, 90);
                path.AddArc(0, p.Height - r, r, r, 90, 90);
                path.CloseFigure();
                p.Region = new Region(path);
            }
        }

        // فرمت‌بندی هوشمند حجم‌ها (حذف نمایش زشت 1024000 MB)
        private string FormatBytes(long bytes)
        {
            if (bytes >= 1024L * 1024L * 1024L) return $"{(bytes / (1024.0 * 1024.0 * 1024.0)):F2} GB";
            if (bytes >= 1024L * 1024L) return $"{(bytes / (1024.0 * 1024.0)):F2} MB";
            return $"{(bytes / 1024.0):F2} KB";
        }

        protected override void SetVisibleCore(bool value)
        {
            if (!isAppVisible) { value = false; if (!this.IsHandleCreated) CreateHandle(); }
            base.SetVisibleCore(value);
        }

        // --- پردازش‌های اصلی شبکه ---
        private void InitializeNetworkBaseline() { try { long t = GetCurrentActiveInterfaceBytes(); if (t >= 0) lastRecordedBytes = t; UpdatePrevSpeedBytes(); } catch { lastRecordedBytes = 0; } }
        private void UpdatePrevSpeedBytes() { long r = 0, s = 0; foreach (NetworkInterface nic in NetworkInterface.GetAllNetworkInterfaces()) { if (nic.OperationalStatus == OperationalStatus.Up && (nic.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 || nic.NetworkInterfaceType == NetworkInterfaceType.Ethernet)) { try { r += nic.GetIPv4Statistics().BytesReceived; s += nic.GetIPv4Statistics().BytesSent; } catch { } } } prevReceivedBytes = r; prevSentBytes = s; }
        private long GetCurrentActiveInterfaceBytes() { long total = -1; foreach (NetworkInterface nic in NetworkInterface.GetAllNetworkInterfaces()) { if (nic.OperationalStatus == OperationalStatus.Up && (nic.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 || nic.NetworkInterfaceType == NetworkInterfaceType.Ethernet)) { try { total += nic.GetIPv4Statistics().BytesReceived + nic.GetIPv4Statistics().BytesSent; } catch { } } } return total == -1 ? 0 : total; }
        
        private void NetworkTimer_Tick(object sender, EventArgs e) {
            if (isInternetCut) return;
            long currentTotal = GetCurrentActiveInterfaceBytes();
            if (currentTotal >= 0 && lastRecordedBytes >= 0) {
                long diff = currentTotal - lastRecordedBytes;
                if (diff > 0) { currentUsageBytes += diff; SaveData(); if (isAppVisible) RefreshDashboardUI(); }
                lastRecordedBytes = currentTotal;
            }
            if (monthlyLimitBytes > 0) {
                if (!hasShownWarning && currentUsageBytes >= (monthlyLimitBytes * (warningThreshold / 100.0)) && currentUsageBytes < monthlyLimitBytes) {
                    hasShownWarning = true; LogAlert($"هشدار: مصرف به {warningThreshold}٪ رسید.");
                    MessageBox.Show($"هشدار: شما به {warningThreshold}٪ از سهمیه مصرف ماهانه خود رسیده‌اید!", "اخطار", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                if (currentUsageBytes >= monthlyLimitBytes) CutInternetAccess(false);
            }
        }
        
        private void SpeedTimer_Tick(object sender, EventArgs e) {
            long currentRecv = 0, currentSent = 0;
            foreach (NetworkInterface nic in NetworkInterface.GetAllNetworkInterfaces()) {
                if (nic.OperationalStatus == OperationalStatus.Up && (nic.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 || nic.NetworkInterfaceType == NetworkInterfaceType.Ethernet)) { try { currentRecv += nic.GetIPv4Statistics().BytesReceived; currentSent += nic.GetIPv4Statistics().BytesSent; } catch { } }
            }
            long down = currentRecv - prevReceivedBytes; long up = currentSent - prevSentBytes;
            if (down < 0) down = 0; if (up < 0) up = 0;
            prevReceivedBytes = currentRecv; prevSentBytes = currentSent;
            UpdateSpeedUI(lblDownSpeed, barDownSpeed, down, colorAccent); 
            UpdateSpeedUI(lblUpSpeed, barUpSpeed, up, colorSuccess); 
        }
        
        private void UpdateSpeedUI(Label lbl, Panel bar, long speedBytes, Color c) {
            double kbps = speedBytes / 1024.0;
            lbl.Text = kbps > 1024 ? $"{(kbps / 1024.0):F2} MB/s" : $"{kbps:F1} KB/s";
            int width = (int)((kbps / 5120.0) * 250);
            if (width > 250) width = 250; if (width < 5) width = 5; 
            bar.Width = width; bar.BackColor = c;
        }
        
        private void CutInternetAccess(bool manualOverride) {
            try {
                isInternetCut = true; disabledInterfaces.Clear(); 
                Process.Start(new ProcessStartInfo("netsh", "advfirewall firewall add rule name=\"NetQuotaBlock\" dir=out action=block enable=yes") { CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden });
                foreach (NetworkInterface nic in NetworkInterface.GetAllNetworkInterfaces()) {
                    if (nic.OperationalStatus == OperationalStatus.Up && (nic.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 || nic.NetworkInterfaceType == NetworkInterfaceType.Ethernet))
                    { disabledInterfaces.Add(nic.Name); Process.Start(new ProcessStartInfo("netsh", $"interface set interface \"{nic.Name}\" admin=disabled") { CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden }); }
                }
                if (isAppVisible) RefreshDashboardUI();
                if (manualOverride) { LogAlert("اینترنت دستی مسدود شد."); MessageBox.Show("اینترنت سیستم مسدود گردید.", "مسدود", MessageBoxButtons.OK, MessageBoxIcon.Information); }
                else { LogAlert("حجم به پایان رسید و اینترنت قطع شد."); MessageBox.Show("سقف حجم شما به پایان رسید.", "اتمام حجم", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            } catch { }
        }
        
        private void EnableInternetAccess(bool manualOverride) {
            try {
                isInternetCut = false;
                Process.Start(new ProcessStartInfo("netsh", "advfirewall firewall delete rule name=\"NetQuotaBlock\"") { CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden });
                foreach (string name in disabledInterfaces) { Process.Start(new ProcessStartInfo("netsh", $"interface set interface \"{name}\" admin=enabled") { CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden }); }
                string[] commonNames = { "Wi-Fi", "Ethernet", "Local Area Connection" };
                foreach (string name in commonNames) { if (!disabledInterfaces.Contains(name)) Process.Start(new ProcessStartInfo("netsh", $"interface set interface \"{name}\" admin=enabled") { CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden }); }
                if (isAppVisible) RefreshDashboardUI();
                if (manualOverride) { LogAlert("اینترنت دستی آزاد شد."); MessageBox.Show("اینترنت سیستم فعال گردید.", "فعال شد", MessageBoxButtons.OK, MessageBoxIcon.Information); }
            } catch { }
        }
        
        private void LogAlert(string message) { if (lstAlertsLog != null) { lstAlertsLog.Items.Insert(0, $"[{DateTime.Now:yyyy/MM/dd HH:mm:ss}] - {message}"); } }
        
        private bool RegisterStartupRobust() {
            try {
                string exe = Application.ExecutablePath; string user = Environment.UserName;
                RegistryKey rk = Registry.CurrentUser.OpenSubKey("SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run", true);
                if (rk != null) rk.SetValue("NetQuotaManager", $"\"{exe}\" -background");
                Process p = Process.Start(new ProcessStartInfo("schtasks", $"/create /tn \"NetQuotaManager\" /tr \"\\\"{exe}\\\" -background\" /sc onlogon /rl highest /ru \"{user}\" /f") { CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden });
                p.WaitForExit(); return p.ExitCode == 0;
            } catch { return false; }
        }

        // --- بخش رابط کاربری (UI) ---
        private void OpenAdminPanel() {
            if (isAppVisible) { this.WindowState = FormWindowState.Normal; this.BringToFront(); this.Activate(); return; }
            if (ShowLoginForm()) {
                isAppVisible = true; this.ShowInTaskbar = true; this.Visible = true; this.WindowState = FormWindowState.Normal;
                this.Show(); this.BringToFront(); this.Activate(); RefreshDashboardUI(); 
            }
        }
        private void ExitApplication() { if (ShowLoginForm()) { trayIcon.Visible = false; Environment.Exit(0); } }

        private bool ShowLoginForm() {
            Form loginForm = new Form() { Width = 500, Height = 350, FormBorderStyle = FormBorderStyle.FixedDialog, Text = "احراز هویت سیستم", StartPosition = FormStartPosition.CenterScreen, MaximizeBox = false, MinimizeBox = false, RightToLeft = RightToLeft.Yes, RightToLeftLayout = true, BackColor = colorCard, TopMost = true };
            Label lblHeader = new Label() { Text = "ورود به مدیریت", ForeColor = colorText, Font = GetFont(16, FontStyle.Bold), Left = 40, Top = 50, AutoSize = true };
            TextBox txtPass = new TextBox() { Left = 40, Top = 120, Width = 400, Height = 35, PasswordChar = '●', Font = GetFont(12) };
            Button btnLogin = new Button() { Text = "تایید و ورود", Left = 40, Top = 180, Width = 400, Height = 45, BackColor = colorAccent, ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Font = GetFont(11, FontStyle.Bold), Cursor = Cursors.Hand };
            btnLogin.FlatAppearance.BorderSize = 0;
            bool success = false;
            btnLogin.Click += (s, e) => { if (txtPass.Text == adminPass) { success = true; loginForm.Close(); } else MessageBox.Show("رمز عبور اشتباه است!", "خطا", MessageBoxButtons.OK, MessageBoxIcon.Warning); };
            loginForm.Controls.Add(lblHeader); loginForm.Controls.Add(txtPass); loginForm.Controls.Add(btnLogin);
            ApplyGlobalFont(loginForm);
            loginForm.ShowDialog(); return success;
        }

        private void BuildModernUI()
        {
            this.Width = 1150; this.Height = 780;
            this.Text = "مدیریت حرفه‌ای سهمیه اینترنت";
            this.StartPosition = FormStartPosition.CenterScreen;
            // تنظیمات چیدمان برای اطمینان از قرارگیری سایدبار در راست
            this.RightToLeft = RightToLeft.Yes;
            this.RightToLeftLayout = false; // عدم استفاده از چیدمان معکوس برای کنترل دقیق فیزیکی
            this.BackColor = colorBg;

            // 1. تاپ‌بار (Top Bar)
            pnlTopBar = new Panel() { Dock = DockStyle.Top, Height = 70, BackColor = colorCard };
            Panel borderBottom = new Panel() { Dock = DockStyle.Bottom, Height = 1, BackColor = Color.FromArgb(226, 232, 240) };
            pnlTopBar.Controls.Add(borderBottom);

            // عنوان صفحه در تاپ‌بار (سمت راست محتوا)
            lblPageTitle = new Label() { Text = "داشبورد مدیریت", ForeColor = colorText, Font = GetFont(14, FontStyle.Bold), Left = 750, Top = 22, AutoSize = true, RightToLeft = RightToLeft.Yes };
            
            // دکمه تنظیمات گرافیکی (بالا سمت راست برنامه - در تاپ‌بار)
            Button btnSettings = new Button() { Text = "⚙️", Left = 1070, Top = 15, Width = 45, Height = 45, FlatStyle = FlatStyle.Flat, ForeColor = colorText, BackColor = colorCard, Font = new Font("Segoe UI Emoji", 16), Cursor = Cursors.Hand };
            btnSettings.FlatAppearance.BorderSize = 0;
            btnSettings.Click += (s, e) => { SwitchPage(pnlSettings, "تنظیمات سیستم"); ResetSidebarColors(pnlSidebar); };
            
            pnlTopBar.Controls.Add(lblPageTitle);
            pnlTopBar.Controls.Add(btnSettings);

            // 2. سایدبار (منوها در سمت راست فیزیکی تصویر)
            pnlSidebar = new Panel() { Dock = DockStyle.Right, Width = 260, BackColor = colorSidebar };
            Label lblBrand = new Label() { Text = "🛡️ کنترل ترافیک", ForeColor = Color.White, Font = GetFont(15, FontStyle.Bold), Left = 40, Top = 25, AutoSize = true, RightToLeft = RightToLeft.Yes };
            pnlSidebar.Controls.Add(lblBrand);

            // 3. محیط محتوا
            pnlContentArea = new Panel() { Dock = DockStyle.Fill, BackColor = colorBg };

            CreateDashboardPanel();
            CreateSettingsPanel(); 
            CreateMonitoringPanel();
            CreateAlertsPanel(); 
            CreateAccessControlPanel(); 

            // ساخت دکمه‌های منو در سایدبار
            string[] menuItems = { "داشبورد", "پایش لحظه‌ای", "هشدارهای مصرف", "کنترل دسترسی اینترنت" };
            int topPos = 100;
            foreach (var item in menuItems)
            {
                Button btnMenu = new Button() { Text = "  " + item, Left = 10, Top = topPos, Width = 240, Height = 50, FlatStyle = FlatStyle.Flat, ForeColor = Color.FromArgb(226, 232, 240), BackColor = item == "داشبورد" ? colorSidebarHover : Color.Transparent, TextAlign = ContentAlignment.MiddleRight, Font = GetFont(11), Tag = item, Cursor = Cursors.Hand, RightToLeft = RightToLeft.Yes };
                btnMenu.FlatAppearance.BorderSize = 0;
                
                btnMenu.MouseEnter += (s, e) => { if (btnMenu.BackColor != colorSidebarHover) btnMenu.BackColor = Color.FromArgb(30, 41, 59); };
                btnMenu.MouseLeave += (s, e) => { if (lblPageTitle.Text != btnMenu.Tag.ToString() && lblPageTitle.Text != "داشبورد مدیریت" || (lblPageTitle.Text=="داشبورد مدیریت" && btnMenu.Tag.ToString()!="داشبورد")) btnMenu.BackColor = Color.Transparent; };

                btnMenu.Click += (s, e) => {
                    ResetSidebarColors(pnlSidebar);
                    btnMenu.BackColor = colorSidebarHover;
                    
                    if (btnMenu.Tag.ToString() == "داشبورد") { SwitchPage(pnlDashboard, "داشبورد مدیریت"); RefreshDashboardUI(); }
                    else if (btnMenu.Tag.ToString() == "پایش لحظه‌ای") { SwitchPage(pnlMonitoring, "پایش لحظه‌ای شبکه"); UpdatePrevSpeedBytes(); speedTimer.Start(); }
                    else if (btnMenu.Tag.ToString() == "هشدارهای مصرف") { SwitchPage(pnlAlerts, "لاگ رویدادها و هشدارها"); }
                    else if (btnMenu.Tag.ToString() == "کنترل دسترسی اینترنت") { SwitchPage(pnlAccessControl, "مدیریت دسترسی دستی"); }
                };
                pnlSidebar.Controls.Add(btnMenu);
                topPos += 55;
            }

            // ترتیب لایه‌ها برای قرارگیری صحیح
            this.Controls.Add(pnlContentArea);
            this.Controls.Add(pnlSidebar); 
            this.Controls.Add(pnlTopBar); 
        }

        private void ResetSidebarColors(Panel sidebar) { foreach (Control ctrl in sidebar.Controls) if (ctrl is Button b) b.BackColor = Color.Transparent; }

        private void SwitchPage(Panel page, string title) {
            pnlDashboard.Visible = false; pnlSettings.Visible = false; pnlMonitoring.Visible = false; pnlAlerts.Visible = false; pnlAccessControl.Visible = false;
            speedTimer.Stop(); page.Visible = true; lblPageTitle.Text = title;
        }

        private void CreateDashboardPanel()
        {
            pnlDashboard = new Panel() { Dock = DockStyle.Fill, BackColor = colorBg, Visible = true };

            // جایگذاری کارت‌ها به صورت دقیق با توجه به محیط RTL
            Panel c1 = new Panel() { Left = 600, Top = 30, Width = 250, Height = 140, BackColor = colorCard };
            c1.Paint += RoundedPanel_Paint;
            c1.Controls.Add(new Label() { Text = "سهمیه کل ماهانه", Font = GetFont(11, FontStyle.Bold), ForeColor = colorTextMuted, Left = 60, Top = 20, AutoSize = true, RightToLeft = RightToLeft.Yes, BackColor = Color.Transparent });
            valUsage = new Label() { Font = GetFont(11), ForeColor = colorText, Left = 10, Top = 55, Width = 230, Height = 70, TextAlign = ContentAlignment.MiddleCenter, AutoSize = false, RightToLeft = RightToLeft.Yes, BackColor = Color.Transparent };
            c1.Controls.Add(valUsage);

            Panel c2 = new Panel() { Left = 320, Top = 30, Width = 250, Height = 140, BackColor = colorCard };
            c2.Paint += RoundedPanel_Paint;
            c2.Controls.Add(new Label() { Text = "حجم باقیمانده", Font = GetFont(11, FontStyle.Bold), ForeColor = colorTextMuted, Left = 75, Top = 20, AutoSize = true, RightToLeft = RightToLeft.Yes, BackColor = Color.Transparent });
            valRemaining = new Label() { Font = GetFont(11), ForeColor = colorText, Left = 10, Top = 55, Width = 230, Height = 70, TextAlign = ContentAlignment.MiddleCenter, AutoSize = false, RightToLeft = RightToLeft.Yes, BackColor = Color.Transparent };
            c2.Controls.Add(valRemaining);

            Panel c3 = new Panel() { Left = 40, Top = 30, Width = 250, Height = 140, BackColor = colorCard };
            c3.Paint += RoundedPanel_Paint;
            c3.Controls.Add(new Label() { Text = "وضعیت سیستم", Font = GetFont(11, FontStyle.Bold), ForeColor = colorTextMuted, Left = 75, Top = 20, AutoSize = true, RightToLeft = RightToLeft.Yes, BackColor = Color.Transparent });
            valStatus = new Label() { Font = GetFont(11), ForeColor = colorText, Left = 10, Top = 55, Width = 230, Height = 70, TextAlign = ContentAlignment.MiddleCenter, AutoSize = false, RightToLeft = RightToLeft.Yes, BackColor = Color.Transparent };
            c3.Controls.Add(valStatus);

            pnlDashboard.Controls.Add(c1); pnlDashboard.Controls.Add(c2); pnlDashboard.Controls.Add(c3);

            Panel cardAction = new Panel() { Left = 40, Top = 200, Width = 810, Height = 160, BackColor = colorCard };
            cardAction.Paint += RoundedPanel_Paint;
            cardAction.Controls.Add(new Label() { Text = "تخصیص حجم جدید", Font = GetFont(13, FontStyle.Bold), ForeColor = colorText, Left = 600, Top = 25, AutoSize = true, RightToLeft = RightToLeft.Yes, BackColor = Color.Transparent });
            
            cardAction.Controls.Add(new Label() { Text = "گیگابایت:", Left = 680, Top = 82, AutoSize = true, Font = GetFont(10), RightToLeft = RightToLeft.Yes, BackColor = Color.Transparent });
            NumericUpDown numGB = new NumericUpDown() { Left = 590, Top = 80, Width = 80, Font = GetFont(11), Maximum = 1000, Value = 2, RightToLeft = RightToLeft.Yes };
            cardAction.Controls.Add(numGB);
            
            cardAction.Controls.Add(new Label() { Text = "مگابایت:", Left = 490, Top = 82, AutoSize = true, Font = GetFont(10), RightToLeft = RightToLeft.Yes, BackColor = Color.Transparent });
            NumericUpDown numMB = new NumericUpDown() { Left = 400, Top = 80, Width = 80, Font = GetFont(11), Maximum = 1023, RightToLeft = RightToLeft.Yes };
            cardAction.Controls.Add(numMB);

            Button btnUpdate = new Button() { Text = "اعمال حجم و اتصال", Left = 40, Top = 75, Width = 250, Height = 42, BackColor = colorSuccess, ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Font = GetFont(11, FontStyle.Bold), Cursor = Cursors.Hand };
            btnUpdate.FlatAppearance.BorderSize = 0;
            btnUpdate.Click += (s, e) => {
                monthlyLimitBytes = ((long)numGB.Value * 1024 * 1024 * 1024) + ((long)numMB.Value * 1024 * 1024);
                currentUsageBytes = 0; hasShownWarning = false; 
                SaveData(); InitializeNetworkBaseline(); EnableInternetAccess(false); RefreshDashboardUI();
                LogAlert($"حجم ماهانه جدید اعمال شد: {numGB.Value} GB و {numMB.Value} MB");
                MessageBox.Show("سهمیه جدید با موفقیت در سیستم ثبت شد.", "موفقیت", MessageBoxButtons.OK, MessageBoxIcon.Information);
            };
            cardAction.Controls.Add(btnUpdate); pnlDashboard.Controls.Add(cardAction);
            pnlContentArea.Controls.Add(pnlDashboard);
        }

        private void CreateMonitoringPanel()
        {
            pnlMonitoring = new Panel() { Dock = DockStyle.Fill, BackColor = colorBg, Visible = false };

            Panel cardDown = new Panel() { Left = 470, Top = 50, Width = 380, Height = 180, BackColor = colorCard };
            cardDown.Paint += RoundedPanel_Paint;
            cardDown.Controls.Add(new Label() { Text = "⬇️ سرعت دانلود فعلی", Font = GetFont(12, FontStyle.Bold), ForeColor = colorAccent, Left = 170, Top = 25, AutoSize = true, RightToLeft = RightToLeft.Yes, BackColor = Color.Transparent });
            lblDownSpeed = new Label() { Text = "0.0 KB/s", Font = GetFont(22, FontStyle.Bold), ForeColor = colorText, Left = 20, Top = 75, AutoSize = true, BackColor = Color.Transparent };
            Panel trackDown = new Panel() { Left = 40, Top = 135, Width = 300, Height = 8, BackColor = Color.FromArgb(226, 232, 240) };
            barDownSpeed = new Panel() { Left = 300, Top = 0, Width = 5, Height = 8, BackColor = colorAccent }; // پر شدن از راست
            trackDown.Controls.Add(barDownSpeed);
            cardDown.Controls.Add(lblDownSpeed); cardDown.Controls.Add(trackDown); pnlMonitoring.Controls.Add(cardDown);

            Panel cardUp = new Panel() { Left = 40, Top = 50, Width = 380, Height = 180, BackColor = colorCard };
            cardUp.Paint += RoundedPanel_Paint;
            cardUp.Controls.Add(new Label() { Text = "⬆️ سرعت آپلود فعلی", Font = GetFont(12, FontStyle.Bold), ForeColor = colorSuccess, Left = 170, Top = 25, AutoSize = true, RightToLeft = RightToLeft.Yes, BackColor = Color.Transparent });
            lblUpSpeed = new Label() { Text = "0.0 KB/s", Font = GetFont(22, FontStyle.Bold), ForeColor = colorText, Left = 20, Top = 75, AutoSize = true, BackColor = Color.Transparent };
            Panel trackUp = new Panel() { Left = 40, Top = 135, Width = 300, Height = 8, BackColor = Color.FromArgb(226, 232, 240) };
            barUpSpeed = new Panel() { Left = 300, Top = 0, Width = 5, Height = 8, BackColor = colorSuccess };
            trackUp.Controls.Add(barUpSpeed);
            cardUp.Controls.Add(lblUpSpeed); cardUp.Controls.Add(trackUp); pnlMonitoring.Controls.Add(cardUp);
            pnlContentArea.Controls.Add(pnlMonitoring);
        }

        private void CreateAlertsPanel()
        {
            pnlAlerts = new Panel() { Dock = DockStyle.Fill, BackColor = colorBg, Visible = false };
            Panel cardLogs = new Panel() { Left = 40, Top = 30, Width = 810, Height = 550, BackColor = colorCard };
            cardLogs.Paint += RoundedPanel_Paint;
            lstAlertsLog = new ListBox() { Left = 20, Top = 20, Width = 770, Height = 510, Font = GetFont(10), ForeColor = colorText, BorderStyle = BorderStyle.None, IntegralHeight = false, RightToLeft = RightToLeft.Yes, BackColor = colorCard };
            cardLogs.Controls.Add(lstAlertsLog);
            pnlAlerts.Controls.Add(cardLogs);
            pnlContentArea.Controls.Add(pnlAlerts);
        }

        private void CreateAccessControlPanel()
        {
            pnlAccessControl = new Panel() { Dock = DockStyle.Fill, BackColor = colorBg, Visible = false };
            Panel cardControl = new Panel() { Left = 40, Top = 50, Width = 810, Height = 200, BackColor = colorCard };
            cardControl.Paint += RoundedPanel_Paint;
            
            Button btnBlock = new Button() { Text = "⛔ مسدودسازی کامل شبکه", Left = 430, Top = 70, Width = 320, Height = 60, BackColor = colorDanger, ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Font = GetFont(12, FontStyle.Bold), Cursor = Cursors.Hand };
            btnBlock.FlatAppearance.BorderSize = 0;
            btnBlock.Click += (s, e) => { if (MessageBox.Show("آیا مطمئن هستید که می‌خواهید ارتباط سیستم قطع شود؟", "اخطار امنیتی", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes) CutInternetAccess(true); };

            Button btnAllow = new Button() { Text = "✅ آزادسازی دسترسی", Left = 60, Top = 70, Width = 320, Height = 60, BackColor = colorSuccess, ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Font = GetFont(12, FontStyle.Bold), Cursor = Cursors.Hand };
            btnAllow.FlatAppearance.BorderSize = 0;
            btnAllow.Click += (s, e) => { if (MessageBox.Show("اینترنت مجدداً فعال شود؟", "تایید", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes) EnableInternetAccess(true); };

            cardControl.Controls.Add(btnBlock); cardControl.Controls.Add(btnAllow); pnlAccessControl.Controls.Add(cardControl);
            pnlContentArea.Controls.Add(pnlAccessControl);
        }

        private void CreateSettingsPanel()
        {
            pnlSettings = new Panel() { Dock = DockStyle.Fill, BackColor = colorBg, Visible = false };

            Panel cardSecurity = new Panel() { Left = 470, Top = 30, Width = 380, Height = 260, BackColor = colorCard };
            cardSecurity.Paint += RoundedPanel_Paint;
            cardSecurity.Controls.Add(new Label() { Text = "🔒 امنیت و رمز عبور", Font = GetFont(12, FontStyle.Bold), ForeColor = colorText, Left = 170, Top = 20, AutoSize = true, RightToLeft = RightToLeft.Yes, BackColor = Color.Transparent });
            cardSecurity.Controls.Add(new Label() { Text = "رمز فعلی:", Font = GetFont(10), Left = 280, Top = 75, AutoSize = true, RightToLeft = RightToLeft.Yes, BackColor = Color.Transparent });
            TextBox txtOldPass = new TextBox() { Left = 40, Top = 72, Width = 220, Font = GetFont(11), PasswordChar = '●', RightToLeft = RightToLeft.Yes };
            cardSecurity.Controls.Add(txtOldPass);
            cardSecurity.Controls.Add(new Label() { Text = "رمز جدید:", Font = GetFont(10), Left = 280, Top = 135, AutoSize = true, RightToLeft = RightToLeft.Yes, BackColor = Color.Transparent });
            TextBox txtNewPass = new TextBox() { Left = 40, Top = 132, Width = 220, Font = GetFont(11), PasswordChar = '●', RightToLeft = RightToLeft.Yes };
            cardSecurity.Controls.Add(txtNewPass);
            Button btnChangePass = new Button() { Text = "ثبت رمز جدید", Left = 40, Top = 190, Width = 300, Height = 45, BackColor = colorAccent, ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Font = GetFont(10, FontStyle.Bold), Cursor = Cursors.Hand };
            btnChangePass.FlatAppearance.BorderSize = 0;
            btnChangePass.Click += (s, e) => {
                if (txtOldPass.Text == adminPass) {
                    if (txtNewPass.Text.Length >= 4) { adminPass = txtNewPass.Text; SaveData(); txtOldPass.Text = ""; txtNewPass.Text = ""; MessageBox.Show("رمز امنیتی مدیر تغییر یافت.", "موفقیت", MessageBoxButtons.OK, MessageBoxIcon.Information); } 
                    else MessageBox.Show("رمز باید حداقل ۴ کاراکتر باشد.", "اخطار", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                } else MessageBox.Show("رمز فعلی وارد شده اشتباه است.", "اخطار", MessageBoxButtons.OK, MessageBoxIcon.Error);
            };
            cardSecurity.Controls.Add(btnChangePass); pnlSettings.Controls.Add(cardSecurity);

            Panel cardAlerts = new Panel() { Left = 40, Top = 30, Width = 400, Height = 260, BackColor = colorCard };
            cardAlerts.Paint += RoundedPanel_Paint;
            cardAlerts.Controls.Add(new Label() { Text = "🔔 تنظیمات عملیاتی", Font = GetFont(12, FontStyle.Bold), ForeColor = colorText, Left = 200, Top = 20, AutoSize = true, RightToLeft = RightToLeft.Yes, BackColor = Color.Transparent });
            cardAlerts.Controls.Add(new Label() { Text = "آستانه هشدار (٪):", Font = GetFont(10), Left = 250, Top = 75, AutoSize = true, RightToLeft = RightToLeft.Yes, BackColor = Color.Transparent });
            NumericUpDown numWarn = new NumericUpDown() { Left = 150, Top = 72, Width = 80, Font = GetFont(11), Minimum = 10, Maximum = 99, Value = warningThreshold, RightToLeft = RightToLeft.Yes };
            cardAlerts.Controls.Add(numWarn);
            Button btnSaveAlerts = new Button() { Text = "ذخیره", Left = 40, Top = 70, Width = 90, Height = 32, BackColor = colorSuccess, ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Font = GetFont(9, FontStyle.Bold), Cursor = Cursors.Hand };
            btnSaveAlerts.FlatAppearance.BorderSize = 0;
            btnSaveAlerts.Click += (s, e) => { warningThreshold = (int)numWarn.Value; SaveData(); MessageBox.Show("تنظیمات با موفقیت ذخیره شد.", "تایید", MessageBoxButtons.OK, MessageBoxIcon.Information); };
            cardAlerts.Controls.Add(btnSaveAlerts);

            Button btnFixStartup = new Button() { Text = "تعمیر استارتاپ ویندوز", Left = 40, Top = 130, Width = 320, Height = 45, BackColor = Color.DarkOrange, ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Font = GetFont(10, FontStyle.Bold), Cursor = Cursors.Hand };
            btnFixStartup.FlatAppearance.BorderSize = 0;
            btnFixStartup.Click += (s, e) => { if(RegisterStartupRobust()) MessageBox.Show("استارتاپ سیستم تعمیر شد.", "موفقیت", MessageBoxButtons.OK, MessageBoxIcon.Information); else MessageBox.Show("دسترسی رد شد! لطفاً با Run as Administrator باز کنید.", "خطا", MessageBoxButtons.OK, MessageBoxIcon.Error); };
            cardAlerts.Controls.Add(btnFixStartup);

            Button btnResetUsage = new Button() { Text = "صفر کردن مصرف فعلی", Left = 40, Top = 190, Width = 320, Height = 45, BackColor = colorDanger, ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Font = GetFont(10, FontStyle.Bold), Cursor = Cursors.Hand };
            btnResetUsage.FlatAppearance.BorderSize = 0;
            btnResetUsage.Click += (s, e) => {
                if (MessageBox.Show("مصرف فعلی سیستم صفر شود؟ این عملیات قابل بازگشت نیست.", "تایید", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes) {
                    currentUsageBytes = 0; hasShownWarning = false; SaveData(); InitializeNetworkBaseline(); MessageBox.Show("حجم مصرفی کاربر صفر شد.", "انجام شد", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            };
            cardAlerts.Controls.Add(btnResetUsage); pnlSettings.Controls.Add(cardAlerts);
            pnlContentArea.Controls.Add(pnlSettings);
        }

        private void RefreshDashboardUI()
        {
            if (valUsage == null) return;
            
            // نمایش کاملاً زیبا و هوشمند حجم‌ها
            string formattedLimit = FormatBytes(monthlyLimitBytes);
            string formattedCurrent = FormatBytes(currentUsageBytes);
            long remainingBytes = monthlyLimitBytes - currentUsageBytes;
            if (remainingBytes < 0) remainingBytes = 0;
            string formattedRemaining = FormatBytes(remainingBytes);
            
            double pct = monthlyLimitBytes > 0 ? ((double)currentUsageBytes / monthlyLimitBytes) * 100 : 0;

            valUsage.Text = $"{formattedCurrent} از {formattedLimit}\n\n( {pct:F1}٪ استفاده شده )";
            valUsage.ForeColor = pct >= warningThreshold ? colorDanger : colorText;

            valRemaining.Text = $"{formattedRemaining}\n\n( {100 - pct:F1}٪ باقیمانده )";
            valStatus.Text = isInternetCut ? "🔴 مسدود شده" : "🟢 متصل و نرمال";
        }

        private void SaveData() { try { File.WriteAllText(DataFilePath, $"{monthlyLimitBytes},{currentUsageBytes},{adminPass},{warningThreshold}"); } catch { } }
        private void LoadData() {
            try {
                if (File.Exists(DataFilePath)) {
                    string[] parts = File.ReadAllText(DataFilePath).Split(',');
                    if (parts.Length >= 2) { long.TryParse(parts[0], out monthlyLimitBytes); long.TryParse(parts[1], out currentUsageBytes); }
                    if (parts.Length >= 3 && !string.IsNullOrEmpty(parts[2])) adminPass = parts[2];
                    if (parts.Length >= 4) int.TryParse(parts[3], out warningThreshold); 
                }
            } catch { }
        }
    }
}
