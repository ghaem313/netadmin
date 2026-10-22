using System;
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
        private const string AdminPass = "009855Vh";
        private static readonly string DataFilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NetQuotaData.txt");

        private long monthlyLimitBytes = 2L * 1024 * 1024 * 1024;
        private long currentUsageBytes = 0;
        private long lastRecordedBytes = -1;
        private bool isInternetCut = false;

        private Panel pnlDashboard;
        private Panel pnlOtherTabs;
        private Label lblTabTitle;
        private Label lblTabContent;

        private Label valUsage;
        private Label valRemaining;
        private Label valStatus;

        private NotifyIcon trayIcon;
        private bool isAppVisible = false;
        
        // اصلاح خطای ابهام تایمر در گیت‌هاب
        private System.Windows.Forms.Timer networkTimer;

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

            // استفاده دقیق از مسیر تایمر برای جلوگیری از خطای کامپایل
            networkTimer = new System.Windows.Forms.Timer() { Interval = 10000 };
            networkTimer.Tick += NetworkTimer_Tick;
            networkTimer.Start();
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
            }
            catch { lastRecordedBytes = 0; }
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

            if (currentUsageBytes >= monthlyLimitBytes && monthlyLimitBytes > 0)
            {
                CutInternetAccess();
            }
        }

        private void CutInternetAccess()
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo("netsh", "interface set interface \"Wi-Fi\" admin=disabled") { CreateNoWindow = true, UseShellExecute = false, WindowStyle = ProcessWindowStyle.Hidden };
                Process.Start(psi);
                isInternetCut = true;
                if (isAppVisible) RefreshDashboardUI();
                MessageBox.Show("سقف حجم ماهانه به پایان رسید و اینترنت قطع شد.", "هشدار", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch { }
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
            Label lblLoginHeader = new Label() { Text = "ورود مدیریت", ForeColor = Color.FromArgb(30, 30, 40), Font = new Font("Tahoma", 16, FontStyle.Bold), Left = 50, Top = 100, Width = 400 };
            TextBox txtPassword = new TextBox() { Left = 50, Top = 180, Width = 350, Height = 35, PasswordChar = '●', Font = new Font("Tahoma", 12) };
            Button btnSignIn = new Button() { Text = "تایید و ورود", Left = 50, Top = 230, Width = 350, Height = 45, BackColor = Color.FromArgb(13, 110, 253), ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Font = new Font("Tahoma", 11, FontStyle.Bold) };

            bool success = false;
            btnSignIn.Click += (s, e) => { if (txtPassword.Text == AdminPass) { success = true; loginForm.Close(); } else MessageBox.Show("رمز عبور اشتباه است!"); };
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
            Label lblBrand = new Label() { Text = "🛡️ مدیریت سهمیه شبکه", ForeColor = Color.White, Font = new Font("Tahoma", 11, FontStyle.Bold), Left = 15, Top = 30, Width = 220 };
            sidebar.Controls.Add(lblBrand);

            CreateDashboardPanel();
            CreateOtherTabsPanel();

            string[] menuItems = { "داشبورد", "پایش لحظه‌ای", "هشدارهای مصرف", "کنترل دسترسی اینترنت", "تنظیمات" };
            int topPos = 90;
            foreach (var item in menuItems)
            {
                Button btnMenu = new Button() { Text = "   " + item, Left = 10, Top = topPos, Width = 230, Height = 45, FlatStyle = FlatStyle.Flat, ForeColor = Color.FromArgb(210, 220, 235), BackColor = item == "داشبورد" ? Color.FromArgb(28, 54, 108) : Color.Transparent, TextAlign = ContentAlignment.TopRight, Font = new Font("Tahoma", 10), Tag = item };
                btnMenu.FlatAppearance.BorderSize = 0;
                btnMenu.Click += (s, e) => {
                    foreach (Control ctrl in sidebar.Controls) if (ctrl is Button b) b.BackColor = Color.Transparent;
                    btnMenu.BackColor = Color.FromArgb(28, 54, 108);

                    if (btnMenu.Tag.ToString() == "داشبورد") { pnlDashboard.Visible = true; pnlOtherTabs.Visible = false; RefreshDashboardUI(); }
                    else {
                        pnlDashboard.Visible = false; pnlOtherTabs.Visible = true;
                        lblTabTitle.Text = btnMenu.Tag.ToString();
                        lblTabContent.Text = $"اطلاعات مربوط به '{btnMenu.Tag}' به زودی در نسخه‌های بعدی در این قسمت نمایش داده می‌شود.\nدر حال حاضر موتور پردازشی پس‌زمینه این بخش فعال است.";
                    }
                };
                sidebar.Controls.Add(btnMenu);
                topPos += 52;
            }

            this.Controls.Add(pnlDashboard);
            this.Controls.Add(pnlOtherTabs);
            this.Controls.Add(sidebar);
        }

        private void CreateDashboardPanel()
        {
            pnlDashboard = new Panel() { Dock = DockStyle.Fill, BackColor = Color.FromArgb(245, 247, 250), Visible = true };
            Label lblHeaderTitle = new Label() { Text = "داشبورد مدیریت", Font = new Font("Tahoma", 16, FontStyle.Bold), Left = 30, Top = 25, Width = 300 };
            pnlDashboard.Controls.Add(lblHeaderTitle);

            Panel c1 = new Panel() { Left = 30, Top = 90, Width = 350, Height = 150, BackColor = Color.White };
            c1.Controls.Add(new Label() { Text = "سهمیه ماهانه", Font = new Font("Tahoma", 9, FontStyle.Bold), ForeColor = Color.Gray, Left = 20, Top = 15, Width = 200 });
            valUsage = new Label() { Font = new Font("Tahoma", 10), ForeColor = Color.FromArgb(40,40,50), Left = 20, Top = 50, Width = 320, Height = 80 };
            c1.Controls.Add(valUsage);

            Panel c2 = new Panel() { Left = 400, Top = 90, Width = 200, Height = 150, BackColor = Color.White };
            c2.Controls.Add(new Label() { Text = "باقیمانده", Font = new Font("Tahoma", 9, FontStyle.Bold), ForeColor = Color.Gray, Left = 20, Top = 15, Width = 150 });
            valRemaining = new Label() { Font = new Font("Tahoma", 10), ForeColor = Color.FromArgb(40,40,50), Left = 20, Top = 50, Width = 170, Height = 80 };
            c2.Controls.Add(valRemaining);

            Panel c3 = new Panel() { Left = 620, Top = 90, Width = 220, Height = 150, BackColor = Color.White };
            c3.Controls.Add(new Label() { Text = "وضعیت اتصال", Font = new Font("Tahoma", 9, FontStyle.Bold), ForeColor = Color.Gray, Left = 20, Top = 15, Width = 150 });
            valStatus = new Label() { Font = new Font("Tahoma", 10), ForeColor = Color.FromArgb(40,40,50), Left = 20, Top = 50, Width = 190, Height = 80 };
            c3.Controls.Add(valStatus);

            pnlDashboard.Controls.Add(c1); pnlDashboard.Controls.Add(c2); pnlDashboard.Controls.Add(c3);

            Panel cardAction = new Panel() { Left = 30, Top = 270, Width = 810, Height = 160, BackColor = Color.White };
            cardAction.Controls.Add(new Label() { Text = "تنظیم و آپدیت حجم ماهانه جدید", Font = new Font("Tahoma", 11, FontStyle.Bold), Left = 25, Top = 20, Width = 400 });
            cardAction.Controls.Add(new Label() { Text = "گیگابایت:", Left = 25, Top = 72, Width = 70, Font = new Font("Tahoma", 10) });
            NumericUpDown numGB = new NumericUpDown() { Left = 95, Top = 70, Width = 70, Font = new Font("Tahoma", 11), Maximum = 1000, Value = 2 };
            cardAction.Controls.Add(numGB);
            
            cardAction.Controls.Add(new Label() { Text = "مگابایت:", Left = 180, Top = 72, Width = 70, Font = new Font("Tahoma", 10) });
            NumericUpDown numMB = new NumericUpDown() { Left = 250, Top = 70, Width = 70, Font = new Font("Tahoma", 11), Maximum = 1023 };
            cardAction.Controls.Add(numMB);

            Button btnUpdate = new Button() { Text = "اعمال حجم و وصل اینترنت", Left = 340, Top = 65, Width = 220, Height = 35, BackColor = Color.FromArgb(40, 167, 69), ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Font = new Font("Tahoma", 9, FontStyle.Bold) };
            btnUpdate.Click += (s, e) => {
                monthlyLimitBytes = ((long)numGB.Value * 1024 * 1024 * 1024) + ((long)numMB.Value * 1024 * 1024);
                currentUsageBytes = 0;
                isInternetCut = false;
                SaveData();
                InitializeNetworkBaseline(); 
                
                try { Process.Start(new ProcessStartInfo("netsh", "interface set interface \"Wi-Fi\" admin=enabled") { CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden }); } catch { }
                
                RefreshDashboardUI();
                MessageBox.Show("حجم جدید با موفقیت اعمال شد و اینترنت متصل گردید.", "موفقیت", MessageBoxButtons.OK, MessageBoxIcon.Information);
            };
            cardAction.Controls.Add(btnUpdate);
            pnlDashboard.Controls.Add(cardAction);
        }

        private void CreateOtherTabsPanel()
        {
            pnlOtherTabs = new Panel() { Dock = DockStyle.Fill, BackColor = Color.FromArgb(245, 247, 250), Visible = false };
            lblTabTitle = new Label() { Font = new Font("Tahoma", 16, FontStyle.Bold), Left = 30, Top = 25, Width = 300 };
            lblTabContent = new Label() { Font = new Font("Tahoma", 11), ForeColor = Color.FromArgb(60,60,60), Left = 30, Top = 100, Width = 700, Height = 200 };
            pnlOtherTabs.Controls.Add(lblTabTitle);
            pnlOtherTabs.Controls.Add(lblTabContent);
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
            valUsage.ForeColor = pct > 90 ? Color.Red : Color.FromArgb(40,40,50);

            valRemaining.Text = $"{remMB:F2} مگابایت\n\n( {100 - pct:F1}٪ باقیمانده )";
            valStatus.Text = isInternetCut ? "🔴 قطع شده (حجم تمام شده)" : "🟢 متصل و در حال پایش";
        }

        private void SaveData()
        {
            try { File.WriteAllText(DataFilePath, $"{monthlyLimitBytes},{currentUsageBytes}"); } catch { }
        }

        private void LoadData()
        {
            try
            {
                if (File.Exists(DataFilePath))
                {
                    string[] parts = File.ReadAllText(DataFilePath).Split(',');
                    if (parts.Length >= 2)
                    {
                        long.TryParse(parts[0], out monthlyLimitBytes);
                        long.TryParse(parts[1], out currentUsageBytes);
                    }
                }
            }
            catch { }
        }
    }
}
