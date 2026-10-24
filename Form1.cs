using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Text; // اضافه شده برای لود فونت اختصاصی
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
            bool startInBackground = false;
            if (args.Length > 0 && args[0] == "-background")
            {
                startInBackground = true;
            }

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
        
        // متغیر نگهداری فونت اختصاصی
        private PrivateFontCollection pfc = new PrivateFontCollection();

        private Panel pnlTopBar;
        private Panel pnlContentArea;
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

        private Color colorPrimary = Color.FromArgb(15, 23, 42); 
        private Color colorAccent = Color.FromArgb(59, 130, 246); 
        private Color colorBg = Color.FromArgb(241, 245, 249); 
        private Color colorCard = Color.White;
        private Color colorText = Color.FromArgb(30, 41, 59);
        private Color colorHover = Color.FromArgb(30, 41, 59); 

        public MainForm(bool startInBackground)
        {
            this.AutoScaleMode = AutoScaleMode.None;
            isAppVisible = false; 
            
            LoadCustomFont(); // فراخوانی لود فونت در ابتدای کار
            LoadData();
            RegisterStartupRobust(); 
            InitializeNetworkBaseline();

            trayIcon = new NotifyIcon();
            try {
                trayIcon.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
                this.Icon = trayIcon.Icon;
            } catch {
                trayIcon.Icon = SystemIcons.Shield;
            }
            trayIcon.Text = "NetQuota Manager - فعال";
            trayIcon.Visible = true;

            ContextMenuStrip trayMenu = new ContextMenuStrip();
            trayMenu.Items.Add("باز کردن پنل", null, (s, e) => OpenAdminPanel());
            trayMenu.Items.Add("خروج کامل", null, (s, e) => ExitApplication());
            trayIcon.ContextMenuStrip = trayMenu;
            trayIcon.DoubleClick += (s, e) => OpenAdminPanel();

            this.WindowState = FormWindowState.Minimized;
            this.ShowInTaskbar = false;
            this.FormBorderStyle = FormBorderStyle.FixedSingle;
            this.MaximizeBox = false;

            BuildModernUI(); 

            this.Load += (s, e) => { if (!startInBackground) BeginInvoke(new Action(OpenAdminPanel)); };
            this.FormClosing += (s, e) => { if (e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; isAppVisible = false; this.Hide(); } };
            this.Resize += (s, e) => { if (this.WindowState == FormWindowState.Minimized) { isAppVisible = false; this.Hide(); } };

            networkTimer = new System.Windows.Forms.Timer() { Interval = 10000 };
            networkTimer.Tick += NetworkTimer_Tick;
            networkTimer.Start();

            speedTimer = new System.Windows.Forms.Timer() { Interval = 1000 };
            speedTimer.Tick += SpeedTimer_Tick;

            LogAlert("سرویس مدیریت اینترنت شروع به کار کرد.");
        }

        // --- سیستم بارگذاری فونت ایران‌سنس از روی فایل ---
        private void LoadCustomFont()
        {
            try
            {
                // پیدا کردن مسیر فایلی که در کنار برنامه قرار گرفته
                string fontPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "iransans.ttf");
                if (File.Exists(fontPath))
                {
                    pfc.AddFontFile(fontPath);
                }
            }
            catch { }
        }

        // متد تولید فونت با استفاده از فایل لود شده
        private Font GetCustomFont(float size, FontStyle style = FontStyle.Regular)
        {
            try
            {
                // اگر فونت با موفقیت خوانده شده باشد، از آن استفاده می‌کند
                if (pfc.Families.Length > 0)
                {
                    return new Font(pfc.Families[0], size, style);
                }
                // اگر فایل پیدا نشد، به تاهوما برمی‌گردد تا ظاهر به هم نریزد
                return new Font("Tahoma", size, style);
            }
            catch { return new Font("Tahoma", size, style); }
        }

        protected override void SetVisibleCore(bool value)
        {
            if (!isAppVisible) { value = false; if (!this.IsHandleCreated) CreateHandle(); }
            base.SetVisibleCore(value);
        }

        private void InitializeNetworkBaseline()
        {
            try { long t = GetCurrentActiveInterfaceBytes(); if (t >= 0) lastRecordedBytes = t; UpdatePrevSpeedBytes(); } catch { lastRecordedBytes = 0; }
        }
        private void UpdatePrevSpeedBytes()
        {
            long r = 0, s = 0;
            foreach (NetworkInterface nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus == OperationalStatus.Up && (nic.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 || nic.NetworkInterfaceType == NetworkInterfaceType.Ethernet))
                { try { r += nic.GetIPv4Statistics().BytesReceived; s += nic.GetIPv4Statistics().BytesSent; } catch { } }
            }
            prevReceivedBytes = r; prevSentBytes = s;
        }
        private long GetCurrentActiveInterfaceBytes()
        {
            long total = -1;
            foreach (NetworkInterface nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus == OperationalStatus.Up && (nic.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 || nic.NetworkInterfaceType == NetworkInterfaceType.Ethernet))
                { try { total += nic.GetIPv4Statistics().BytesReceived + nic.GetIPv4Statistics().BytesSent; } catch { } }
            }
            return total == -1 ? 0 : total;
        }
        private void NetworkTimer_Tick(object sender, EventArgs e)
        {
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
        private void SpeedTimer_Tick(object sender, EventArgs e)
        {
            long currentRecv = 0, currentSent = 0;
            foreach (NetworkInterface nic in NetworkInterface.GetAllNetworkInterfaces()) {
                if (nic.OperationalStatus == OperationalStatus.Up && (nic.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 || nic.NetworkInterfaceType == NetworkInterfaceType.Ethernet))
                { try { currentRecv += nic.GetIPv4Statistics().BytesReceived; currentSent += nic.GetIPv4Statistics().BytesSent; } catch { } }
            }
            long down = currentRecv - prevReceivedBytes; long up = currentSent - prevSentBytes;
            if (down < 0) down = 0; if (up < 0) up = 0;
            prevReceivedBytes = currentRecv; prevSentBytes = currentSent;
            UpdateSpeedUI(lblDownSpeed, barDownSpeed, down, colorAccent); 
            UpdateSpeedUI(lblUpSpeed, barUpSpeed, up, Color.FromArgb(16, 185, 129)); 
        }
        private void UpdateSpeedUI(Label lbl, Panel bar, long speedBytes, Color c)
        {
            double kbps = speedBytes / 1024.0;
            lbl.Text = kbps > 1024 ? $"{(kbps / 1024.0):F2} MB/s" : $"{kbps:F1} KB/s";
            int width = (int)((kbps / 5120.0) * 250);
            if (width > 250) width = 250; if (width < 5) width = 5; 
            bar.Width = width; bar.BackColor = c;
        }
        private void CutInternetAccess(bool manualOverride)
        {
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
        private void EnableInternetAccess(bool manualOverride)
        {
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
        private void LogAlert(string message)
        {
            if (lstAlertsLog != null) { lstAlertsLog.Items.Insert(0, $"[{DateTime.Now:yyyy/MM/dd HH:mm:ss}] - {message}"); }
        }
        private bool RegisterStartupRobust()
        {
            try {
                string exe = Application.ExecutablePath; string user = Environment.UserName;
                RegistryKey rk = Registry.CurrentUser.OpenSubKey("SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run", true);
                if (rk != null) rk.SetValue("NetQuotaManager", $"\"{exe}\" -background");
                Process p = Process.Start(new ProcessStartInfo("schtasks", $"/create /tn \"NetQuotaManager\" /tr \"\\\"{exe}\\\" -background\" /sc onlogon /rl highest /ru \"{user}\" /f") { CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden });
                p.WaitForExit(); return p.ExitCode == 0;
            } catch { return false; }
        }

        private void OpenAdminPanel()
        {
            if (isAppVisible) { this.WindowState = FormWindowState.Normal; this.BringToFront(); this.Activate(); return; }
            if (ShowLoginForm()) {
                isAppVisible = true; this.ShowInTaskbar = true; this.Visible = true; this.WindowState = FormWindowState.Normal;
                this.Show(); this.BringToFront(); this.Activate(); RefreshDashboardUI(); 
            }
        }
        private void ExitApplication() { if (ShowLoginForm()) { trayIcon.Visible = false; Environment.Exit(0); } }

        private bool ShowLoginForm()
        {
            Form loginForm = new Form() { Width = 600, Height = 400, FormBorderStyle = FormBorderStyle.FixedDialog, Text = "احراز هویت", StartPosition = FormStartPosition.CenterScreen, MaximizeBox = false, MinimizeBox = false, RightToLeft = RightToLeft.Yes, RightToLeftLayout = true, BackColor = colorCard, TopMost = true };
            Label lblHeader = new Label() { Text = "ورود به مدیریت", ForeColor = colorText, Font = GetCustomFont(16, FontStyle.Bold), Left = 50, Top = 80, AutoSize = true };
            TextBox txtPass = new TextBox() { Left = 50, Top = 140, Width = 300, Height = 35, PasswordChar = '●', Font = GetCustomFont(12) };
            Button btnLogin = new Button() { Text = "ورود", Left = 50, Top = 190, Width = 300, Height = 45, BackColor = colorAccent, ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Font = GetCustomFont(11, FontStyle.Bold), Cursor = Cursors.Hand };
            btnLogin.FlatAppearance.BorderSize = 0;
            
            bool success = false;
            btnLogin.Click += (s, e) => { if (txtPass.Text == adminPass) { success = true; loginForm.Close(); } else MessageBox.Show("رمز عبور اشتباه است!", "خطا", MessageBoxButtons.OK, MessageBoxIcon.Warning); };
            
            loginForm.Controls.Add(lblHeader); loginForm.Controls.Add(txtPass); loginForm.Controls.Add(btnLogin);
            loginForm.ShowDialog(); return success;
        }

        private void BuildModernUI()
        {
            this.Width = 1150; this.Height = 780;
            this.Text = "NetQuota Manager";
            this.StartPosition = FormStartPosition.CenterScreen;
            this.RightToLeft = RightToLeft.Yes;
            this.RightToLeftLayout = true;
            this.BackColor = colorBg;

            Panel sidebar = new Panel() { Width = 260, Dock = DockStyle.Right, BackColor = colorPrimary };
            Label lblBrand = new Label() { Text = "🛡️ مدیریت شبکه", ForeColor = Color.White, Font = GetCustomFont(14, FontStyle.Bold), Left = 20, Top = 30, AutoSize = true };
            sidebar.Controls.Add(lblBrand);

            pnlTopBar = new Panel() { Dock = DockStyle.Top, Height = 70, BackColor = colorCard };
            Panel borderBottom = new Panel() { Dock = DockStyle.Bottom, Height = 1, BackColor = Color.FromArgb(226, 232, 240) };
            pnlTopBar.Controls.Add(borderBottom);

            lblPageTitle = new Label() { Text = "داشبورد", ForeColor = colorText, Font = GetCustomFont(14, FontStyle.Bold), Left = 30, Top = 20, AutoSize = true };
            
            Button btnSettings = new Button() { Text = "⚙️ تنظیمات", Left = 30, Top = 15, Width = 130, Height = 40, FlatStyle = FlatStyle.Flat, ForeColor = colorText, BackColor = colorBg, Font = GetCustomFont(10, FontStyle.Bold), Cursor = Cursors.Hand };
            btnSettings.FlatAppearance.BorderSize = 0;
            btnSettings.Click += (s, e) => { SwitchPage(pnlSettings, "تنظیمات سیستم"); ResetSidebarColors(sidebar); };
            
            pnlTopBar.Controls.Add(lblPageTitle);
            pnlTopBar.Controls.Add(btnSettings);

            pnlContentArea = new Panel() { Dock = DockStyle.Fill, BackColor = colorBg };

            CreateDashboardPanel();
            CreateSettingsPanel(); 
            CreateMonitoringPanel();
            CreateAlertsPanel(); 
            CreateAccessControlPanel(); 

            string[] menuItems = { "داشبورد", "پایش لحظه‌ای", "هشدارهای مصرف", "کنترل دسترسی اینترنت" };
            int topPos = 100;
            foreach (var item in menuItems)
            {
                Button btnMenu = new Button() { Text = "  " + item, Left = 10, Top = topPos, Width = 240, Height = 50, FlatStyle = FlatStyle.Flat, ForeColor = Color.FromArgb(226, 232, 240), BackColor = item == "داشبورد" ? colorHover : Color.Transparent, TextAlign = ContentAlignment.MiddleLeft, Font = GetCustomFont(11), Tag = item, Cursor = Cursors.Hand };
                btnMenu.FlatAppearance.BorderSize = 0;
                
                btnMenu.MouseEnter += (s, e) => { if (btnMenu.BackColor != colorHover) btnMenu.BackColor = Color.FromArgb(30, 41, 59); };
                btnMenu.MouseLeave += (s, e) => { if (lblPageTitle.Text != btnMenu.Tag.ToString()) btnMenu.BackColor = Color.Transparent; };

                btnMenu.Click += (s, e) => {
                    ResetSidebarColors(sidebar);
                    btnMenu.BackColor = colorHover;
                    
                    if (btnMenu.Tag.ToString() == "داشبورد") { SwitchPage(pnlDashboard, "داشبورد"); RefreshDashboardUI(); }
                    else if (btnMenu.Tag.ToString() == "پایش لحظه‌ای") { SwitchPage(pnlMonitoring, "پایش لحظه‌ای"); UpdatePrevSpeedBytes(); speedTimer.Start(); }
                    else if (btnMenu.Tag.ToString() == "هشدارهای مصرف") { SwitchPage(pnlAlerts, "هشدارهای مصرف"); }
                    else if (btnMenu.Tag.ToString() == "کنترل دسترسی اینترنت") { SwitchPage(pnlAccessControl, "کنترل دسترسی اینترنت"); }
                };
                sidebar.Controls.Add(btnMenu);
                topPos += 55;
            }

            this.Controls.Add(pnlContentArea);
            this.Controls.Add(pnlTopBar);
            this.Controls.Add(sidebar); 
        }

        private void ResetSidebarColors(Panel sidebar)
        {
            foreach (Control ctrl in sidebar.Controls) if (ctrl is Button b) b.BackColor = Color.Transparent;
        }

        private void SwitchPage(Panel page, string title)
        {
            pnlDashboard.Visible = false; pnlSettings.Visible = false; pnlMonitoring.Visible = false; pnlAlerts.Visible = false; pnlAccessControl.Visible = false;
            speedTimer.Stop();
            page.Visible = true;
            lblPageTitle.Text = title;
        }

        private void CreateDashboardPanel()
        {
            pnlDashboard = new Panel() { Dock = DockStyle.Fill, BackColor = colorBg, Visible = true };

            Panel c1 = new Panel() { Left = 30, Top = 30, Width = 260, Height = 130, BackColor = colorCard };
            c1.Controls.Add(new Label() { Text = "سهمیه ماهانه", Font = GetCustomFont(10, FontStyle.Bold), ForeColor = Color.Gray, Left = 20, Top = 15, AutoSize = true });
            valUsage = new Label() { Font = GetCustomFont(10), ForeColor = colorText, Left = 10, Top = 50, Width = 240, Height = 70, TextAlign = ContentAlignment.MiddleCenter, AutoSize = false };
            c1.Controls.Add(valUsage);

            Panel c2 = new Panel() { Left = 310, Top = 30, Width = 240, Height = 130, BackColor = colorCard };
            c2.Controls.Add(new Label() { Text = "باقیمانده", Font = GetCustomFont(10, FontStyle.Bold), ForeColor = Color.Gray, Left = 20, Top = 15, AutoSize = true });
            valRemaining = new Label() { Font = GetCustomFont(10), ForeColor = colorText, Left = 10, Top = 50, Width = 220, Height = 70, TextAlign = ContentAlignment.MiddleCenter, AutoSize = false };
            c2.Controls.Add(valRemaining);

            Panel c3 = new Panel() { Left = 570, Top = 30, Width = 240, Height = 130, BackColor = colorCard };
            c3.Controls.Add(new Label() { Text = "وضعیت اتصال", Font = GetCustomFont(10, FontStyle.Bold), ForeColor = Color.Gray, Left = 20, Top = 15, AutoSize = true });
            valStatus = new Label() { Font = GetCustomFont(10), ForeColor = colorText, Left = 10, Top = 50, Width = 220, Height = 70, TextAlign = ContentAlignment.MiddleCenter, AutoSize = false };
            c3.Controls.Add(valStatus);

            pnlDashboard.Controls.Add(c1); pnlDashboard.Controls.Add(c2); pnlDashboard.Controls.Add(c3);

            Panel cardAction = new Panel() { Left = 30, Top = 180, Width = 780, Height = 160, BackColor = colorCard };
            cardAction.Controls.Add(new Label() { Text = "تنظیم حجم ماهانه جدید", Font = GetCustomFont(12, FontStyle.Bold), ForeColor = colorText, Left = 25, Top = 20, AutoSize = true });
            cardAction.Controls.Add(new Label() { Text = "گیگابایت:", Left = 25, Top = 72, AutoSize = true, Font = GetCustomFont(10) });
            NumericUpDown numGB = new NumericUpDown() { Left = 95, Top = 70, Width = 70, Font = GetCustomFont(11), Maximum = 1000, Value = 2 };
            cardAction.Controls.Add(numGB);
            cardAction.Controls.Add(new Label() { Text = "مگابایت:", Left = 180, Top = 72, AutoSize = true, Font = GetCustomFont(10) });
            NumericUpDown numMB = new NumericUpDown() { Left = 250, Top = 70, Width = 70, Font = GetCustomFont(11), Maximum = 1023 };
            cardAction.Controls.Add(numMB);

            Button btnUpdate = new Button() { Text = "اعمال حجم و اتصال", Left = 340, Top = 65, Width = 200, Height = 38, BackColor = Color.FromArgb(16, 185, 129), ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Font = GetCustomFont(10, FontStyle.Bold), Cursor = Cursors.Hand };
            btnUpdate.FlatAppearance.BorderSize = 0;
            btnUpdate.Click += (s, e) => {
                monthlyLimitBytes = ((long)numGB.Value * 1024 * 1024 * 1024) + ((long)numMB.Value * 1024 * 1024);
                currentUsageBytes = 0; hasShownWarning = false; 
                SaveData(); InitializeNetworkBaseline(); EnableInternetAccess(false); RefreshDashboardUI();
                LogAlert($"حجم ماهانه جدید اعمال شد: {numGB.Value} GB و {numMB.Value} MB");
                MessageBox.Show("حجم جدید اعمال شد.", "موفقیت", MessageBoxButtons.OK, MessageBoxIcon.Information);
            };
            cardAction.Controls.Add(btnUpdate); pnlDashboard.Controls.Add(cardAction);
            pnlContentArea.Controls.Add(pnlDashboard);
        }

        private void CreateMonitoringPanel()
        {
            pnlMonitoring = new Panel() { Dock = DockStyle.Fill, BackColor = colorBg, Visible = false };

            Panel cardDown = new Panel() { Left = 30, Top = 30, Width = 380, Height = 160, BackColor = colorCard };
            cardDown.Controls.Add(new Label() { Text = "⬇️ سرعت دانلود فعلی", Font = GetCustomFont(11, FontStyle.Bold), ForeColor = colorAccent, Left = 20, Top = 20, AutoSize = true });
            lblDownSpeed = new Label() { Text = "0.0 KB/s", Font = GetCustomFont(18, FontStyle.Bold), ForeColor = colorText, Left = 20, Top = 65, AutoSize = true };
            Panel trackDown = new Panel() { Left = 20, Top = 115, Width = 300, Height = 10, BackColor = Color.FromArgb(226, 232, 240) };
            barDownSpeed = new Panel() { Left = 0, Top = 0, Width = 5, Height = 10, BackColor = colorAccent };
            trackDown.Controls.Add(barDownSpeed);
            cardDown.Controls.Add(lblDownSpeed); cardDown.Controls.Add(trackDown); pnlMonitoring.Controls.Add(cardDown);

            Panel cardUp = new Panel() { Left = 430, Top = 30, Width = 380, Height = 160, BackColor = colorCard };
            cardUp.Controls.Add(new Label() { Text = "⬆️ سرعت آپلود فعلی", Font = GetCustomFont(11, FontStyle.Bold), ForeColor = Color.FromArgb(16, 185, 129), Left = 20, Top = 20, AutoSize = true });
            lblUpSpeed = new Label() { Text = "0.0 KB/s", Font = GetCustomFont(18, FontStyle.Bold), ForeColor = colorText, Left = 20, Top = 65, AutoSize = true };
            Panel trackUp = new Panel() { Left = 20, Top = 115, Width = 300, Height = 10, BackColor = Color.FromArgb(226, 232, 240) };
            barUpSpeed = new Panel() { Left = 0, Top = 0, Width = 5, Height = 10, BackColor = Color.FromArgb(16, 185, 129) };
            trackUp.Controls.Add(barUpSpeed);
            cardUp.Controls.Add(lblUpSpeed); cardUp.Controls.Add(trackUp); pnlMonitoring.Controls.Add(cardUp);
            pnlContentArea.Controls.Add(pnlMonitoring);
        }

        private void CreateAlertsPanel()
        {
            pnlAlerts = new Panel() { Dock = DockStyle.Fill, BackColor = colorBg, Visible = false };
            lstAlertsLog = new ListBox() { Left = 30, Top = 30, Width = 780, Height = 550, Font = GetCustomFont(10), ForeColor = colorText, BorderStyle = BorderStyle.None, IntegralHeight = false };
            pnlAlerts.Controls.Add(lstAlertsLog);
            pnlContentArea.Controls.Add(pnlAlerts);
        }

        private void CreateAccessControlPanel()
        {
            pnlAccessControl = new Panel() { Dock = DockStyle.Fill, BackColor = colorBg, Visible = false };
            Panel cardControl = new Panel() { Left = 30, Top = 30, Width = 780, Height = 180, BackColor = colorCard };
            
            Button btnBlock = new Button() { Text = "⛔ مسدودسازی کامل", Left = 50, Top = 60, Width = 300, Height = 55, BackColor = Color.FromArgb(239, 68, 68), ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Font = GetCustomFont(11, FontStyle.Bold), Cursor = Cursors.Hand };
            btnBlock.FlatAppearance.BorderSize = 0;
            btnBlock.Click += (s, e) => { if (MessageBox.Show("اینترنت قطع شود؟", "تایید", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes) CutInternetAccess(true); };

            Button btnAllow = new Button() { Text = "✅ آزادسازی اینترنت", Left = 430, Top = 60, Width = 300, Height = 55, BackColor = Color.FromArgb(16, 185, 129), ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Font = GetCustomFont(11, FontStyle.Bold), Cursor = Cursors.Hand };
            btnAllow.FlatAppearance.BorderSize = 0;
            btnAllow.Click += (s, e) => { if (MessageBox.Show("اینترنت فعال شود؟", "تایید", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes) EnableInternetAccess(true); };

            cardControl.Controls.Add(btnBlock); cardControl.Controls.Add(btnAllow); pnlAccessControl.Controls.Add(cardControl);
            pnlContentArea.Controls.Add(pnlAccessControl);
        }

        private void CreateSettingsPanel()
        {
            pnlSettings = new Panel() { Dock = DockStyle.Fill, BackColor = colorBg, Visible = false };

            Panel cardSecurity = new Panel() { Left = 30, Top = 30, Width = 380, Height = 250, BackColor = colorCard };
            cardSecurity.Controls.Add(new Label() { Text = "🔒 امنیت", Font = GetCustomFont(11, FontStyle.Bold), ForeColor = colorText, Left = 20, Top = 20, AutoSize = true });
            cardSecurity.Controls.Add(new Label() { Text = "رمز فعلی:", Font = GetCustomFont(10), Left = 20, Top = 70, AutoSize = true });
            TextBox txtOldPass = new TextBox() { Left = 130, Top = 68, Width = 200, Font = GetCustomFont(10), PasswordChar = '●' };
            cardSecurity.Controls.Add(txtOldPass);
            cardSecurity.Controls.Add(new Label() { Text = "رمز جدید:", Font = GetCustomFont(10), Left = 20, Top = 120, AutoSize = true });
            TextBox txtNewPass = new TextBox() { Left = 130, Top = 118, Width = 200, Font = GetCustomFont(10), PasswordChar = '●' };
            cardSecurity.Controls.Add(txtNewPass);
            Button btnChangePass = new Button() { Text = "ثبت رمز", Left = 130, Top = 170, Width = 200, Height = 40, BackColor = colorAccent, ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Font = GetCustomFont(10, FontStyle.Bold), Cursor = Cursors.Hand };
            btnChangePass.FlatAppearance.BorderSize = 0;
            btnChangePass.Click += (s, e) => {
                if (txtOldPass.Text == adminPass) {
                    if (txtNewPass.Text.Length >= 4) { adminPass = txtNewPass.Text; SaveData(); txtOldPass.Text = ""; txtNewPass.Text = ""; MessageBox.Show("رمز تغییر یافت."); } 
                    else MessageBox.Show("حداقل ۴ کاراکتر وارد کنید.");
                } else MessageBox.Show("رمز فعلی اشتباه است.");
            };
            cardSecurity.Controls.Add(btnChangePass); pnlSettings.Controls.Add(cardSecurity);

            Panel cardAlerts = new Panel() { Left = 430, Top = 30, Width = 380, Height = 250, BackColor = colorCard };
            cardAlerts.Controls.Add(new Label() { Text = "🔔 سیستم و هشدار", Font = GetCustomFont(11, FontStyle.Bold), ForeColor = colorText, Left = 20, Top = 20, AutoSize = true });
            cardAlerts.Controls.Add(new Label() { Text = "آستانه هشدار (٪):", Font = GetCustomFont(10), Left = 20, Top = 70, AutoSize = true });
            NumericUpDown numWarn = new NumericUpDown() { Left = 150, Top = 68, Width = 80, Font = GetCustomFont(10), Minimum = 10, Maximum = 99, Value = warningThreshold };
            cardAlerts.Controls.Add(numWarn);
            Button btnSaveAlerts = new Button() { Text = "ذخیره", Left = 240, Top = 65, Width = 90, Height = 30, BackColor = Color.FromArgb(16, 185, 129), ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Font = GetCustomFont(9, FontStyle.Bold), Cursor = Cursors.Hand };
            btnSaveAlerts.FlatAppearance.BorderSize = 0;
            btnSaveAlerts.Click += (s, e) => { warningThreshold = (int)numWarn.Value; SaveData(); MessageBox.Show("ذخیره شد."); };
            cardAlerts.Controls.Add(btnSaveAlerts);

            Button btnFixStartup = new Button() { Text = "تعمیر استارتاپ سیستم", Left = 20, Top = 120, Width = 310, Height = 40, BackColor = Color.DarkOrange, ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Font = GetCustomFont(9, FontStyle.Bold), Cursor = Cursors.Hand };
            btnFixStartup.FlatAppearance.BorderSize = 0;
            btnFixStartup.Click += (s, e) => { if(RegisterStartupRobust()) MessageBox.Show("موفقیت آمیز."); else MessageBox.Show("خطا!"); };
            cardAlerts.Controls.Add(btnFixStartup);

            Button btnResetUsage = new Button() { Text = "صفر کردن مصرف فعلی", Left = 20, Top = 170, Width = 310, Height = 40, BackColor = Color.FromArgb(239, 68, 68), ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Font = GetCustomFont(9, FontStyle.Bold), Cursor = Cursors.Hand };
            btnResetUsage.FlatAppearance.BorderSize = 0;
            btnResetUsage.Click += (s, e) => {
                if (MessageBox.Show("مصرف صفر شود؟", "تایید", MessageBoxButtons.YesNo) == DialogResult.Yes) {
                    currentUsageBytes = 0; hasShownWarning = false; SaveData(); InitializeNetworkBaseline(); MessageBox.Show("انجام شد.");
                }
            };
            cardAlerts.Controls.Add(btnResetUsage); pnlSettings.Controls.Add(cardAlerts);
            pnlContentArea.Controls.Add(pnlSettings);
        }

        private void RefreshDashboardUI()
        {
            if (valUsage == null) return;
            double limitMB = (double)monthlyLimitBytes / (1024 * 1024);
            double currentMB = (double)currentUsageBytes / (1024 * 1024);
            double remMB = limitMB - currentMB;
            if (remMB < 0) remMB = 0;
            double pct = limitMB > 0 ? (currentMB / limitMB) * 100 : 0;

            valUsage.Text = $"{currentMB:F2} MB از {limitMB:F2} MB\n\n( {pct:F1}٪ استفاده شده )";
            valUsage.ForeColor = pct >= warningThreshold ? Color.Red : colorText;

            valRemaining.Text = $"{remMB:F2} MB\n\n( {100 - pct:F1}٪ باقیمانده )";
            valStatus.Text = isInternetCut ? "🔴 مسدود شده" : "🟢 متصل و در حال پایش";
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
