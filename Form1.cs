using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net.NetworkInformation;
using System.Windows.Forms;
using Microsoft.Win32;

namespace NetQuotaManager
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            Application.SetHighDpiMode(HighDpiMode.SystemAware);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
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

        // پنل‌های تمام صفحات
        private Panel pnlDashboard;
        private Panel pnlSettings;
        private Panel pnlMonitoring;
        private Panel pnlAlerts;
        private Panel pnlAccessControl;
        
        // المان‌های صفحات
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

        public MainForm()
        {
            this.AutoScaleMode = AutoScaleMode.None;
            
            LoadData();
            AddToStartup();
            InitializeNetworkBaseline();

            trayIcon = new NotifyIcon();
            trayIcon.Icon = SystemIcons.Shield;
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

            BuildMainDashboard();

            this.FormClosing += (s, e) => 
            {
                if (e.CloseReason == CloseReason.UserClosing)
                {
                    e.Cancel = true;
                    this.Hide();
                    isAppVisible = false;
                }
            };

            networkTimer = new System.Windows.Forms.Timer() { Interval = 10000 };
            networkTimer.Tick += NetworkTimer_Tick;
            networkTimer.Start();

            speedTimer = new System.Windows.Forms.Timer() { Interval = 1000 };
            speedTimer.Tick += SpeedTimer_Tick;

            LogAlert("سرویس مدیریت اینترنت شروع به کار کرد.");
        }

        protected override void SetVisibleCore(bool value)
        {
            if (!isAppVisible) value = false;
            base.SetVisibleCore(value);
        }

        private void InitializeNetworkBaseline()
        {
            try
            {
                long totalCurrent = GetCurrentActiveInterfaceBytes();
                if (totalCurrent >= 0) lastRecordedBytes = totalCurrent;
                UpdatePrevSpeedBytes(); 
            }
            catch { lastRecordedBytes = 0; }
        }

        private void UpdatePrevSpeedBytes()
        {
            long r = 0, s = 0;
            foreach (NetworkInterface nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus == OperationalStatus.Up && (nic.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 || nic.NetworkInterfaceType == NetworkInterfaceType.Ethernet))
                {
                    try { r += nic.GetIPv4Statistics().BytesReceived; s += nic.GetIPv4Statistics().BytesSent; } catch { }
                }
            }
            prevReceivedBytes = r;
            prevSentBytes = s;
        }

        private long GetCurrentActiveInterfaceBytes()
        {
            long total = -1;
            foreach (NetworkInterface nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus == OperationalStatus.Up && (nic.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 || nic.NetworkInterfaceType == NetworkInterfaceType.Ethernet))
                {
                    try { total += nic.GetIPv4Statistics().BytesReceived + nic.GetIPv4Statistics().BytesSent; } catch { }
                }
            }
            return total == -1 ? 0 : total;
        }

        private void NetworkTimer_Tick(object sender, EventArgs e)
        {
            if (isInternetCut) return;

            long currentTotal = GetCurrentActiveInterfaceBytes();
            if (currentTotal >= 0 && lastRecordedBytes >= 0)
            {
                long diff = currentTotal - lastRecordedBytes;
                if (diff > 0)
                {
                    currentUsageBytes += diff;
                    SaveData();
                    if (isAppVisible) RefreshDashboardUI(); 
                }
                lastRecordedBytes = currentTotal;
            }

            if (monthlyLimitBytes > 0)
            {
                if (!hasShownWarning && currentUsageBytes >= (monthlyLimitBytes * (warningThreshold / 100.0)) && currentUsageBytes < monthlyLimitBytes)
                {
                    hasShownWarning = true;
                    LogAlert($"هشدار: مصرف کاربر به {warningThreshold}٪ رسید.");
                    MessageBox.Show($"هشدار: شما به {warningThreshold}٪ از سهمیه مصرف ماهانه خود رسیده‌اید! لطفاً مصرف خود را مدیریت کنید.", "اخطار مصرف نزدیک به اتمام", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }

                if (currentUsageBytes >= monthlyLimitBytes)
                {
                    CutInternetAccess(false);
                }
            }
        }

        private void SpeedTimer_Tick(object sender, EventArgs e)
        {
            long currentRecv = 0, currentSent = 0;
            foreach (NetworkInterface nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus == OperationalStatus.Up && (nic.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 || nic.NetworkInterfaceType == NetworkInterfaceType.Ethernet))
                {
                    try { currentRecv += nic.GetIPv4Statistics().BytesReceived; currentSent += nic.GetIPv4Statistics().BytesSent; } catch { }
                }
            }

            long downSpeedBytes = currentRecv - prevReceivedBytes;
            long upSpeedBytes = currentSent - prevSentBytes;
            
            if (downSpeedBytes < 0) downSpeedBytes = 0;
            if (upSpeedBytes < 0) upSpeedBytes = 0;

            prevReceivedBytes = currentRecv;
            prevSentBytes = currentSent;

            UpdateSpeedUI(lblDownSpeed, barDownSpeed, downSpeedBytes, Color.FromArgb(13, 110, 253)); 
            UpdateSpeedUI(lblUpSpeed, barUpSpeed, upSpeedBytes, Color.FromArgb(40, 167, 69)); 
        }

        private void UpdateSpeedUI(Label lbl, Panel bar, long speedBytes, Color c)
        {
            double kbps = speedBytes / 1024.0;
            lbl.Text = kbps > 1024 ? $"{(kbps / 1024.0):F2} MB/s" : $"{kbps:F1} KB/s";
            int maxWidth = 250;
            int width = (int)((kbps / 5120.0) * maxWidth);
            if (width > maxWidth) width = maxWidth;
            if (width < 5) width = 5; 
            bar.Width = width;
            bar.BackColor = c;
        }

        private void CutInternetAccess(bool manualOverride)
        {
            try
            {
                isInternetCut = true;
                disabledInterfaces.Clear(); 
                Process.Start(new ProcessStartInfo("netsh", "advfirewall firewall add rule name=\"NetQuotaBlock\" dir=out action=block enable=yes") { CreateNoWindow = true, UseShellExecute = false, WindowStyle = ProcessWindowStyle.Hidden });

                foreach (NetworkInterface nic in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (nic.OperationalStatus == OperationalStatus.Up && (nic.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 || nic.NetworkInterfaceType == NetworkInterfaceType.Ethernet))
                    {
                        disabledInterfaces.Add(nic.Name);
                        Process.Start(new ProcessStartInfo("netsh", $"interface set interface \"{nic.Name}\" admin=disabled") { CreateNoWindow = true, UseShellExecute = false, WindowStyle = ProcessWindowStyle.Hidden });
                    }
                }
                
                if (isAppVisible) RefreshDashboardUI();
                
                if (manualOverride)
                {
                    LogAlert("دسترسی اینترنت به صورت دستی مسدود شد.");
                    MessageBox.Show("اینترنت سیستم به صورت دستی مسدود گردید.", "مسدود شد", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                else
                {
                    LogAlert("حجم مصرفی به پایان رسید و اینترنت به طور خودکار قطع شد.");
                    MessageBox.Show("سقف حجم شما به پایان رسید و اینترنت به طور کامل مسدود شد.", "اتمام حجم", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            catch { }
        }

        private void EnableInternetAccess(bool manualOverride)
        {
            try
            {
                isInternetCut = false;
                Process.Start(new ProcessStartInfo("netsh", "advfirewall firewall delete rule name=\"NetQuotaBlock\"") { CreateNoWindow = true, UseShellExecute = false, WindowStyle = ProcessWindowStyle.Hidden });

                foreach (string name in disabledInterfaces)
                {
                    Process.Start(new ProcessStartInfo("netsh", $"interface set interface \"{name}\" admin=enabled") { CreateNoWindow = true, UseShellExecute = false, WindowStyle = ProcessWindowStyle.Hidden });
                }

                string[] commonNames = { "Wi-Fi", "Ethernet", "Local Area Connection" };
                foreach (string name in commonNames)
                {
                    if (!disabledInterfaces.Contains(name))
                    {
                        Process.Start(new ProcessStartInfo("netsh", $"interface set interface \"{name}\" admin=enabled") { CreateNoWindow = true, UseShellExecute = false, WindowStyle = ProcessWindowStyle.Hidden });
                    }
                }

                if (isAppVisible) RefreshDashboardUI();

                if (manualOverride)
                {
                    LogAlert("دسترسی اینترنت به صورت دستی آزاد شد.");
                    MessageBox.Show("اینترنت سیستم مجدداً فعال گردید.", "فعال شد", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
            catch { }
        }

        private void LogAlert(string message)
        {
            if (lstAlertsLog != null)
            {
                string logEntry = $"[{DateTime.Now:yyyy/MM/dd HH:mm:ss}] - {message}";
                lstAlertsLog.Items.Insert(0, logEntry);
            }
        }

        private void AddToStartup()
        {
            try
            {
                RegistryKey rk = Registry.CurrentUser.OpenSubKey("SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run", true);
                rk.SetValue("NetQuotaManager", Application.ExecutablePath);
            }
            catch { }
        }

        private void OpenAdminPanel()
        {
            if (isAppVisible) return;
            if (ShowLoginForm())
            {
                isAppVisible = true;
                RefreshDashboardUI(); 
                this.Show();
                this.WindowState = FormWindowState.Normal;
                this.BringToFront();
            }
        }

        private void ExitApplication()
        {
            if (ShowLoginForm())
            {
                trayIcon.Visible = false;
                Environment.Exit(0);
            }
        }

        private bool ShowLoginForm()
        {
            Form loginForm = new Form() { Width = 850, Height = 550, FormBorderStyle = FormBorderStyle.FixedDialog, Text = "ورود مدیر", StartPosition = FormStartPosition.CenterScreen, MaximizeBox = false, MinimizeBox = false, RightToLeft = RightToLeft.Yes, RightToLeftLayout = true, BackColor = Color.White };
            Label lblLoginHeader = new Label() { Text = "ورود مدیریت", ForeColor = Color.FromArgb(30, 30, 40), Font = new Font("Tahoma", 18, FontStyle.Bold), Left = 50, Top = 100, AutoSize = true };
            TextBox txtPassword = new TextBox() { Left = 50, Top = 180, Width = 350, Height = 35, PasswordChar = '●', Font = new Font("Tahoma", 12) };
            Button btnSignIn = new Button() { Text = "تایید و ورود", Left = 50, Top = 230, Width = 350, Height = 45, BackColor = Color.FromArgb(13, 110, 253), ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Font = new Font("Tahoma", 11, FontStyle.Bold) };

            bool success = false;
            btnSignIn.Click += (s, e) => { if (txtPassword.Text == adminPass) { success = true; loginForm.Close(); } else MessageBox.Show("رمز عبور اشتباه است!"); };
            
            loginForm.Controls.Add(lblLoginHeader); loginForm.Controls.Add(txtPassword); loginForm.Controls.Add(btnSignIn);
            loginForm.ShowDialog();
            return success;
        }

        private void BuildMainDashboard()
        {
            this.Width = 1150; this.Height = 780;
            this.Text = "مدیریت سهمیه شبکه";
            this.StartPosition = FormStartPosition.CenterScreen;
            this.RightToLeft = RightToLeft.Yes;
            this.RightToLeftLayout = true;
            this.BackColor = Color.FromArgb(245, 247, 250);

            Panel sidebar = new Panel() { Width = 250, Dock = DockStyle.Right, BackColor = Color.FromArgb(15, 32, 67) };
            Label lblBrand = new Label() { Text = "🛡️ مدیریت سهمیه شبکه", ForeColor = Color.White, Font = new Font("Tahoma", 11, FontStyle.Bold), Left = 15, Top = 30, AutoSize = true };
            sidebar.Controls.Add(lblBrand);

            CreateDashboardPanel();
            CreateSettingsPanel(); 
            CreateMonitoringPanel();
            CreateAlertsPanel(); // ساخت پنل هشدارها
            CreateAccessControlPanel(); // ساخت پنل کنترل دسترسی

            string[] menuItems = { "داشبورد", "پایش لحظه‌ای", "هشدارهای مصرف", "کنترل دسترسی اینترنت", "تنظیمات" };
            int topPos = 90;
            foreach (var item in menuItems)
            {
                Button btnMenu = new Button() { Text = "   " + item, Left = 10, Top = topPos, Width = 230, Height = 45, FlatStyle = FlatStyle.Flat, ForeColor = Color.FromArgb(210, 220, 235), BackColor = item == "داشبورد" ? Color.FromArgb(28, 54, 108) : Color.Transparent, TextAlign = ContentAlignment.TopRight, Font = new Font("Tahoma", 10), Tag = item };
                btnMenu.FlatAppearance.BorderSize = 0;
                btnMenu.Click += (s, e) => {
                    foreach (Control ctrl in sidebar.Controls) if (ctrl is Button b) b.BackColor = Color.Transparent;
                    btnMenu.BackColor = Color.FromArgb(28, 54, 108);

                    pnlDashboard.Visible = false;
                    pnlSettings.Visible = false;
                    pnlMonitoring.Visible = false;
                    pnlAlerts.Visible = false;
                    pnlAccessControl.Visible = false;
                    
                    speedTimer.Stop(); 

                    if (btnMenu.Tag.ToString() == "داشبورد") { pnlDashboard.Visible = true; RefreshDashboardUI(); }
                    else if (btnMenu.Tag.ToString() == "تنظیمات") { pnlSettings.Visible = true; }
                    else if (btnMenu.Tag.ToString() == "پایش لحظه‌ای") { pnlMonitoring.Visible = true; UpdatePrevSpeedBytes(); speedTimer.Start(); }
                    else if (btnMenu.Tag.ToString() == "هشدارهای مصرف") { pnlAlerts.Visible = true; }
                    else if (btnMenu.Tag.ToString() == "کنترل دسترسی اینترنت") { pnlAccessControl.Visible = true; }
                };
                sidebar.Controls.Add(btnMenu);
                topPos += 52;
            }

            this.Controls.Add(pnlDashboard);
            this.Controls.Add(pnlSettings);
            this.Controls.Add(pnlMonitoring);
            this.Controls.Add(pnlAlerts);
            this.Controls.Add(pnlAccessControl);
            this.Controls.Add(sidebar);
        }

        private void CreateDashboardPanel()
        {
            pnlDashboard = new Panel() { Dock = DockStyle.Fill, BackColor = Color.FromArgb(245, 247, 250), Visible = true };
            Label lblHeaderTitle = new Label() { Text = "داشبورد مدیریت", Font = new Font("Tahoma", 16, FontStyle.Bold), Left = 30, Top = 25, AutoSize = true };
            pnlDashboard.Controls.Add(lblHeaderTitle);

            Panel c1 = new Panel() { Left = 30, Top = 90, Width = 330, Height = 150, BackColor = Color.White };
            c1.Controls.Add(new Label() { Text = "سهمیه ماهانه", Font = new Font("Tahoma", 10, FontStyle.Bold), ForeColor = Color.Gray, Left = 20, Top = 15, AutoSize = true });
            valUsage = new Label() { Font = new Font("Tahoma", 10), ForeColor = Color.FromArgb(40,40,50), Left = 10, Top = 50, Width = 310, Height = 90, TextAlign = ContentAlignment.MiddleCenter, AutoSize = false };
            c1.Controls.Add(valUsage);

            Panel c2 = new Panel() { Left = 380, Top = 90, Width = 230, Height = 150, BackColor = Color.White };
            c2.Controls.Add(new Label() { Text = "باقیمانده", Font = new Font("Tahoma", 10, FontStyle.Bold), ForeColor = Color.Gray, Left = 20, Top = 15, AutoSize = true });
            valRemaining = new Label() { Font = new Font("Tahoma", 10), ForeColor = Color.FromArgb(40,40,50), Left = 10, Top = 50, Width = 210, Height = 90, TextAlign = ContentAlignment.MiddleCenter, AutoSize = false };
            c2.Controls.Add(valRemaining);

            Panel c3 = new Panel() { Left = 630, Top = 90, Width = 220, Height = 150, BackColor = Color.White };
            c3.Controls.Add(new Label() { Text = "وضعیت اتصال", Font = new Font("Tahoma", 10, FontStyle.Bold), ForeColor = Color.Gray, Left = 20, Top = 15, AutoSize = true });
            valStatus = new Label() { Font = new Font("Tahoma", 10), ForeColor = Color.FromArgb(40,40,50), Left = 10, Top = 50, Width = 200, Height = 90, TextAlign = ContentAlignment.MiddleCenter, AutoSize = false };
            c3.Controls.Add(valStatus);

            pnlDashboard.Controls.Add(c1); pnlDashboard.Controls.Add(c2); pnlDashboard.Controls.Add(c3);

            Panel cardAction = new Panel() { Left = 30, Top = 270, Width = 820, Height = 160, BackColor = Color.White };
            cardAction.Controls.Add(new Label() { Text = "تنظیم و آپدیت حجم ماهانه جدید", Font = new Font("Tahoma", 12, FontStyle.Bold), Left = 25, Top = 20, AutoSize = true });
            cardAction.Controls.Add(new Label() { Text = "گیگابایت:", Left = 25, Top = 72, AutoSize = true, Font = new Font("Tahoma", 10) });
            NumericUpDown numGB = new NumericUpDown() { Left = 95, Top = 70, Width = 70, Font = new Font("Tahoma", 11), Maximum = 1000, Value = 2 };
            cardAction.Controls.Add(numGB);
            cardAction.Controls.Add(new Label() { Text = "مگابایت:", Left = 180, Top = 72, AutoSize = true, Font = new Font("Tahoma", 10) });
            NumericUpDown numMB = new NumericUpDown() { Left = 250, Top = 70, Width = 70, Font = new Font("Tahoma", 11), Maximum = 1023 };
            cardAction.Controls.Add(numMB);

            Button btnUpdate = new Button() { Text = "اعمال حجم و وصل اینترنت", Left = 340, Top = 65, Width = 220, Height = 35, BackColor = Color.FromArgb(40, 167, 69), ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Font = new Font("Tahoma", 9, FontStyle.Bold) };
            
            btnUpdate.Click += (s, e) => {
                monthlyLimitBytes = ((long)numGB.Value * 1024 * 1024 * 1024) + ((long)numMB.Value * 1024 * 1024);
                currentUsageBytes = 0; 
                hasShownWarning = false; 
                SaveData();
                InitializeNetworkBaseline(); 
                EnableInternetAccess(false); 
                RefreshDashboardUI();
                LogAlert($"حجم ماهانه جدید اعمال شد: {numGB.Value} GB و {numMB.Value} MB");
                MessageBox.Show("حجم جدید با موفقیت اعمال شد و اینترنت متصل گردید.", "موفقیت", MessageBoxButtons.OK, MessageBoxIcon.Information);
            };
            
            cardAction.Controls.Add(btnUpdate);
            pnlDashboard.Controls.Add(cardAction);
        }

        private void CreateMonitoringPanel()
        {
            pnlMonitoring = new Panel() { Dock = DockStyle.Fill, BackColor = Color.FromArgb(245, 247, 250), Visible = false };
            Label lblHeaderTitle = new Label() { Text = "پایش لحظه‌ای ترافیک", Font = new Font("Tahoma", 16, FontStyle.Bold), Left = 30, Top = 25, AutoSize = true };
            Label lblHeaderSub = new Label() { Text = "نمایش سرعت زنده اینترنت سیستم (به‌روزرسانی هر ثانیه)", Font = new Font("Tahoma", 10), ForeColor = Color.Gray, Left = 30, Top = 60, AutoSize = true };
            pnlMonitoring.Controls.Add(lblHeaderTitle); pnlMonitoring.Controls.Add(lblHeaderSub);

            Panel cardDown = new Panel() { Left = 30, Top = 110, Width = 400, Height = 180, BackColor = Color.White };
            cardDown.Controls.Add(new Label() { Text = "⬇️ سرعت دانلود فعلی", Font = new Font("Tahoma", 12, FontStyle.Bold), ForeColor = Color.FromArgb(13, 110, 253), Left = 20, Top = 20, AutoSize = true });
            lblDownSpeed = new Label() { Text = "0.0 KB/s", Font = new Font("Tahoma", 20, FontStyle.Bold), ForeColor = Color.FromArgb(40,40,50), Left = 20, Top = 65, AutoSize = true };
            Panel trackDown = new Panel() { Left = 20, Top = 120, Width = 250, Height = 15, BackColor = Color.FromArgb(230, 230, 235) };
            barDownSpeed = new Panel() { Left = 0, Top = 0, Width = 5, Height = 15, BackColor = Color.FromArgb(13, 110, 253) };
            trackDown.Controls.Add(barDownSpeed);
            cardDown.Controls.Add(lblDownSpeed); cardDown.Controls.Add(trackDown); pnlMonitoring.Controls.Add(cardDown);

            Panel cardUp = new Panel() { Left = 450, Top = 110, Width = 400, Height = 180, BackColor = Color.White };
            cardUp.Controls.Add(new Label() { Text = "⬆️ سرعت آپلود فعلی", Font = new Font("Tahoma", 12, FontStyle.Bold), ForeColor = Color.FromArgb(40, 167, 69), Left = 20, Top = 20, AutoSize = true });
            lblUpSpeed = new Label() { Text = "0.0 KB/s", Font = new Font("Tahoma", 20, FontStyle.Bold), ForeColor = Color.FromArgb(40,40,50), Left = 20, Top = 65, AutoSize = true };
            Panel trackUp = new Panel() { Left = 20, Top = 120, Width = 250, Height = 15, BackColor = Color.FromArgb(230, 230, 235) };
            barUpSpeed = new Panel() { Left = 0, Top = 0, Width = 5, Height = 15, BackColor = Color.FromArgb(40, 167, 69) };
            trackUp.Controls.Add(barUpSpeed);
            cardUp.Controls.Add(lblUpSpeed); cardUp.Controls.Add(trackUp); pnlMonitoring.Controls.Add(cardUp);
        }

        private void CreateAlertsPanel()
        {
            pnlAlerts = new Panel() { Dock = DockStyle.Fill, BackColor = Color.FromArgb(245, 247, 250), Visible = false };
            Label lblHeaderTitle = new Label() { Text = "هشدارهای مصرف و رویدادها", Font = new Font("Tahoma", 16, FontStyle.Bold), Left = 30, Top = 25, AutoSize = true };
            Label lblHeaderSub = new Label() { Text = "لیست گزارش‌ها و اتفاقات مهم ثبت‌شده در سیستم", Font = new Font("Tahoma", 10), ForeColor = Color.Gray, Left = 30, Top = 60, AutoSize = true };
            pnlAlerts.Controls.Add(lblHeaderTitle); pnlAlerts.Controls.Add(lblHeaderSub);

            lstAlertsLog = new ListBox() { Left = 30, Top = 100, Width = 820, Height = 500, Font = new Font("Tahoma", 10), ForeColor = Color.FromArgb(40,40,50), IntegralHeight = false };
            pnlAlerts.Controls.Add(lstAlertsLog);
        }

        private void CreateAccessControlPanel()
        {
            pnlAccessControl = new Panel() { Dock = DockStyle.Fill, BackColor = Color.FromArgb(245, 247, 250), Visible = false };
            Label lblHeaderTitle = new Label() { Text = "کنترل دستی دسترسی اینترنت", Font = new Font("Tahoma", 16, FontStyle.Bold), Left = 30, Top = 25, AutoSize = true };
            Label lblHeaderSub = new Label() { Text = "با استفاده از این بخش می‌توانید مستقل از سهمیه مصرف، اینترنت را قطع یا وصل کنید.", Font = new Font("Tahoma", 10), ForeColor = Color.Gray, Left = 30, Top = 60, AutoSize = true };
            pnlAccessControl.Controls.Add(lblHeaderTitle); pnlAccessControl.Controls.Add(lblHeaderSub);

            Panel cardControl = new Panel() { Left = 30, Top = 110, Width = 820, Height = 200, BackColor = Color.White };
            
            Button btnBlock = new Button() { Text = "⛔ قطع فوری اینترنت (مسدودسازی کامل)", Left = 50, Top = 70, Width = 320, Height = 55, BackColor = Color.FromArgb(220, 53, 69), ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Font = new Font("Tahoma", 11, FontStyle.Bold) };
            btnBlock.Click += (s, e) => {
                if (MessageBox.Show("آیا مطمئن هستید که می‌خواهید دسترسی اینترنت را فوراً قطع کنید؟", "تایید قطع", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes) {
                    CutInternetAccess(true);
                }
            };

            Button btnAllow = new Button() { Text = "✅ وصل فوری اینترنت (آزادسازی کامل)", Left = 430, Top = 70, Width = 320, Height = 55, BackColor = Color.FromArgb(40, 167, 69), ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Font = new Font("Tahoma", 11, FontStyle.Bold) };
            btnAllow.Click += (s, e) => {
                if (MessageBox.Show("آیا مطمئن هستید که می‌خواهید دسترسی اینترنت را فعال کنید؟", "تایید اتصال", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes) {
                    EnableInternetAccess(true);
                }
            };

            cardControl.Controls.Add(btnBlock);
            cardControl.Controls.Add(btnAllow);
            pnlAccessControl.Controls.Add(cardControl);
        }

        private void CreateSettingsPanel()
        {
            pnlSettings = new Panel() { Dock = DockStyle.Fill, BackColor = Color.FromArgb(245, 247, 250), Visible = false };
            Label lblHeaderTitle = new Label() { Text = "تنظیمات سیستم", Font = new Font("Tahoma", 16, FontStyle.Bold), Left = 30, Top = 25, AutoSize = true };
            pnlSettings.Controls.Add(lblHeaderTitle);

            Panel cardSecurity = new Panel() { Left = 30, Top = 90, Width = 400, Height = 280, BackColor = Color.White };
            cardSecurity.Controls.Add(new Label() { Text = "🔒 امنیت و رمز عبور", Font = new Font("Tahoma", 12, FontStyle.Bold), Left = 20, Top = 20, AutoSize = true });
            cardSecurity.Controls.Add(new Label() { Text = "رمز عبور فعلی:", Font = new Font("Tahoma", 10), Left = 20, Top = 70, AutoSize = true });
            TextBox txtOldPass = new TextBox() { Left = 150, Top = 68, Width = 220, Font = new Font("Tahoma", 10), PasswordChar = '●' };
            cardSecurity.Controls.Add(txtOldPass);
            cardSecurity.Controls.Add(new Label() { Text = "رمز عبور جدید:", Font = new Font("Tahoma", 10), Left = 20, Top = 120, AutoSize = true });
            TextBox txtNewPass = new TextBox() { Left = 150, Top = 118, Width = 220, Font = new Font("Tahoma", 10), PasswordChar = '●' };
            cardSecurity.Controls.Add(txtNewPass);
            Button btnChangePass = new Button() { Text = "تغییر رمز عبور", Left = 150, Top = 180, Width = 220, Height = 40, BackColor = Color.FromArgb(13, 110, 253), ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Font = new Font("Tahoma", 10, FontStyle.Bold) };
            btnChangePass.Click += (s, e) => {
                if (txtOldPass.Text == adminPass) {
                    if (txtNewPass.Text.Length >= 4) {
                        adminPass = txtNewPass.Text; SaveData(); txtOldPass.Text = ""; txtNewPass.Text = "";
                        LogAlert("رمز عبور مدیر تغییر یافت.");
                        MessageBox.Show("رمز عبور با موفقیت تغییر یافت.", "موفقیت", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    } else MessageBox.Show("رمز عبور جدید باید حداقل 4 کاراکتر باشد.", "خطا", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                } else MessageBox.Show("رمز عبور فعلی اشتباه است.", "خطا", MessageBoxButtons.OK, MessageBoxIcon.Error);
            };
            cardSecurity.Controls.Add(btnChangePass); pnlSettings.Controls.Add(cardSecurity);

            Panel cardAlerts = new Panel() { Left = 450, Top = 90, Width = 400, Height = 280, BackColor = Color.White };
            cardAlerts.Controls.Add(new Label() { Text = "🔔 تنظیمات مصرف و هشدارها", Font = new Font("Tahoma", 12, FontStyle.Bold), Left = 20, Top = 20, AutoSize = true });
            cardAlerts.Controls.Add(new Label() { Text = "درصد آستانه هشدار پایان حجم:", Font = new Font("Tahoma", 10), Left = 20, Top = 70, AutoSize = true });
            NumericUpDown numWarn = new NumericUpDown() { Left = 250, Top = 68, Width = 80, Font = new Font("Tahoma", 10), Minimum = 10, Maximum = 99, Value = warningThreshold };
            cardAlerts.Controls.Add(numWarn);
            Button btnSaveAlerts = new Button() { Text = "ذخیره تنظیمات هشدار", Left = 110, Top = 120, Width = 220, Height = 35, BackColor = Color.FromArgb(40, 167, 69), ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Font = new Font("Tahoma", 10, FontStyle.Bold) };
            btnSaveAlerts.Click += (s, e) => {
                warningThreshold = (int)numWarn.Value; SaveData();
                LogAlert($"آستانه هشدار مصرف روی {warningThreshold}٪ تنظیم شد.");
                MessageBox.Show($"هشدار مصرف روی {warningThreshold}٪ تنظیم شد.", "موفقیت", MessageBoxButtons.OK, MessageBoxIcon.Information);
            };
            cardAlerts.Controls.Add(btnSaveAlerts);

            cardAlerts.Controls.Add(new Label() { Text = "عملیات ویژه:", Font = new Font("Tahoma", 10, FontStyle.Bold), ForeColor = Color.Red, Left = 20, Top = 180, AutoSize = true });
            Button btnResetUsage = new Button() { Text = "صفر کردن مصرف فعلی (بدون تغییر سقف)", Left = 50, Top = 210, Width = 280, Height = 35, BackColor = Color.FromArgb(220, 53, 69), ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Font = new Font("Tahoma", 9, FontStyle.Bold) };
            btnResetUsage.Click += (s, e) => {
                if (MessageBox.Show("آیا مطمئن هستید که می‌خواهید مصرف فعلی را صفر کنید؟", "تایید", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes) {
                    currentUsageBytes = 0; hasShownWarning = false; SaveData(); InitializeNetworkBaseline();
                    LogAlert("حجم مصرفی کاربر به صورت دستی صفر شد.");
                    MessageBox.Show("حجم مصرفی کاربر صفر شد.", "انجام شد", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            };
            cardAlerts.Controls.Add(btnResetUsage); pnlSettings.Controls.Add(cardAlerts);
        }

        private void RefreshDashboardUI()
        {
            if (valUsage == null) return;
            double limitMB = (double)monthlyLimitBytes / (1024 * 1024);
            double currentMB = (double)currentUsageBytes / (1024 * 1024);
            double remMB = limitMB - currentMB;
            if (remMB < 0) remMB = 0;
            double pct = limitMB > 0 ? (currentMB / limitMB) * 100 : 0;

            valUsage.Text = $"{currentMB:F2} مگابایت از {limitMB:F2} مگابایت مصرف شده\n\n( {pct:F1}٪ استفاده شده )";
            valUsage.ForeColor = pct >= warningThreshold ? Color.Red : Color.FromArgb(40,40,50);

            valRemaining.Text = $"{remMB:F2} مگابایت\n\n( {100 - pct:F1}٪ باقیمانده )";
            valStatus.Text = isInternetCut ? "🔴 مسدود شده" : "🟢 متصل و در حال پایش";
        }

        private void SaveData()
        {
            try { File.WriteAllText(DataFilePath, $"{monthlyLimitBytes},{currentUsageBytes},{adminPass},{warningThreshold}"); } catch { }
        }

        private void LoadData()
        {
            try
            {
                if (File.Exists(DataFilePath))
                {
                    string[] parts = File.ReadAllText(DataFilePath).Split(',');
                    if (parts.Length >= 2) { long.TryParse(parts[0], out monthlyLimitBytes); long.TryParse(parts[1], out currentUsageBytes); }
                    if (parts.Length >= 3 && !string.IsNullOrEmpty(parts[2])) adminPass = parts[2];
                    if (parts.Length >= 4) int.TryParse(parts[3], out warningThreshold); 
                }
            }
            catch { }
        }
    }
}
