using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using Microsoft.Win32;

namespace NetQuotaManager
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
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
        private long currentUsageBytes = 536870912; 

        private Panel pnlDashboard;
        private Panel pnlPlaceholder;
        private Label lblPlaceholderTitle;

        private NotifyIcon trayIcon;
        private bool isAppVisible = false;

        public MainForm()
        {
            LoadData();
            AddToStartup(); 
            
            trayIcon = new NotifyIcon();
            trayIcon.Icon = SystemIcons.Shield;
            trayIcon.Text = "NetQuota Manager - در حال بررسی مصرف";
            trayIcon.Visible = true;

            ContextMenuStrip trayMenu = new ContextMenuStrip();
            trayMenu.Items.Add("باز کردن پنل مدیریت", null, (s, e) => OpenAdminPanel());
            trayMenu.Items.Add("خروج کامل (قطع سرویس)", null, (s, e) => ExitApplication());
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
        }

        protected override void SetVisibleCore(bool value)
        {
            if (!isAppVisible) value = false;
            base.SetVisibleCore(value);
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
            Form loginForm = new Form()
            {
                Width = 950, Height = 650,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                Text = "مدیر سهمیه اینترنت - ورود امن",
                StartPosition = FormStartPosition.CenterScreen,
                MaximizeBox = false, MinimizeBox = false,
                RightToLeft = RightToLeft.Yes,
                RightToLeftLayout = true,
                BackColor = Color.FromArgb(245, 247, 250)
            };

            Panel pnlLeft = new Panel() { Width = 420, Height = 650, Dock = DockStyle.Left, BackColor = Color.FromArgb(15, 32, 67) };
            Label lblAppTitle = new Label() { Text = "مدیریت سهمیه شبکه", ForeColor = Color.White, Font = new Font("Tahoma", 18, FontStyle.Bold), Left = 40, Top = 50, Width = 350, Height = 40 };
            Label lblAppDesc = new Label() { Text = "پایش، مدیریت و کنترل مصرف اینترنت شبکه‌های متصل به همراه سهمیه‌های ماهانه و هشدار در لحظه.", ForeColor = Color.FromArgb(180, 195, 220), Font = new Font("Tahoma", 10), Left = 40, Top = 100, Width = 350, Height = 60 };
            pnlLeft.Controls.Add(lblAppTitle);
            pnlLeft.Controls.Add(lblAppDesc);

            Panel pnlRight = new Panel() { Width = 530, Height = 650, Dock = DockStyle.Fill, BackColor = Color.White };
            Label lblLoginHeader = new Label() { Text = "ورود مدیریت", ForeColor = Color.FromArgb(30, 30, 40), Font = new Font("Tahoma", 16, FontStyle.Bold), Left = 60, Top = 120, Width = 400, Height = 35 };
            Label lblLoginSub = new Label() { Text = "لطفا رمز عبور مدیریت را برای دسترسی به پنل یا خروج وارد کنید.", ForeColor = Color.Gray, Font = new Font("Tahoma", 9), Left = 60, Top = 160, Width = 400, Height = 25 };

            Label lblPassTitle = new Label() { Text = "رمز عبور", ForeColor = Color.FromArgb(50, 50, 60), Font = new Font("Tahoma", 10, FontStyle.Bold), Left = 60, Top = 220, Width = 400, Height = 25 };
            TextBox txtPassword = new TextBox() { Left = 60, Top = 250, Width = 390, Height = 35, PasswordChar = '●', Font = new Font("Tahoma", 12) };
            Button btnSignIn = new Button() { Text = "تایید و ورود", Left = 60, Top = 320, Width = 390, Height = 45, BackColor = Color.FromArgb(13, 110, 253), ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Font = new Font("Tahoma", 11, FontStyle.Bold) };

            bool success = false;
            btnSignIn.Click += (s, e) =>
            {
                if (txtPassword.Text == AdminPass)
                {
                    success = true;
                    loginForm.Close();
                }
                else
                {
                    MessageBox.Show("رمز عبور وارد شده اشتباه است!", "خطای احراز هویت", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            };

            pnlRight.Controls.Add(lblLoginHeader);
            pnlRight.Controls.Add(lblLoginSub);
            pnlRight.Controls.Add(lblPassTitle);
            pnlRight.Controls.Add(txtPassword);
            pnlRight.Controls.Add(btnSignIn);

            loginForm.Controls.Add(pnlRight);
            loginForm.Controls.Add(pnlLeft);
            loginForm.ShowDialog();

            return success;
        }

        private void BuildMainDashboard()
        {
            this.Width = 1100; this.Height = 750;
            this.Text = "مدیریت سهمیه شبکه - NetQuota Manager";
            this.StartPosition = FormStartPosition.CenterScreen;
            this.RightToLeft = RightToLeft.Yes;
            this.RightToLeftLayout = true;
            this.BackColor = Color.FromArgb(245, 247, 250);

            Panel sidebar = new Panel() { Width = 260, Dock = DockStyle.Right, BackColor = Color.FromArgb(15, 32, 67) };
            Label lblBrand = new Label() { Text = "🛡️ مدیریت سهمیه شبکه", ForeColor = Color.White, Font = new Font("Tahoma", 11, FontStyle.Bold), Left = 20, Top = 30, Width = 220, Height = 40 };
            sidebar.Controls.Add(lblBrand);

            CreateContentPanels();

            string[] menuItems = { "داشبورد", "سهمیه ماهانه", "پایش لحظه‌ای", "هشدارهای مصرف", "کنترل دسترسی اینترنت", "تنظیمات" };
            int topPos = 90;
            
            foreach (var item in menuItems)
            {
                Button btnMenu = new Button()
                {
                    Text = "   " + item, Left = 15, Top = topPos, Width = 230, Height = 42,
                    FlatStyle = FlatStyle.Flat, ForeColor = Color.FromArgb(210, 220, 235),
                    BackColor = item == "داشبورد" ? Color.FromArgb(28, 54, 108) : Color.Transparent,
                    TextAlign = ContentAlignment.TopRight, Font = new Font("Tahoma", 10),
                    Tag = item 
                };
                btnMenu.FlatAppearance.BorderSize = 0;
                
                btnMenu.Click += (s, e) => {
                    foreach (Control ctrl in sidebar.Controls)
                        if (ctrl is Button b) b.BackColor = Color.Transparent;
                    btnMenu.BackColor = Color.FromArgb(28, 54, 108);

                    string selectedMenu = btnMenu.Tag.ToString();
                    if (selectedMenu == "داشبورد")
                    {
                        pnlDashboard.Visible = true;
                        pnlPlaceholder.Visible = false;
                    }
                    else
                    {
                        pnlDashboard.Visible = false;
                        pnlPlaceholder.Visible = true;
                        lblPlaceholderTitle.Text = $"بخش {selectedMenu} در حال توسعه است...";
                    }
                };

                sidebar.Controls.Add(btnMenu);
                topPos += 50;
            }

            this.Controls.Add(pnlDashboard);
            this.Controls.Add(pnlPlaceholder);
            this.Controls.Add(sidebar);
        }

        private void CreateContentPanels()
        {
            pnlDashboard = new Panel() { Dock = DockStyle.Fill, BackColor = Color.FromArgb(245, 247, 250), Padding = new Padding(20), Visible = true };
            Label lblHeaderTitle = new Label() { Text = "داشبورد مدیریت", Font = new Font("Tahoma", 16, FontStyle.Bold), ForeColor = Color.FromArgb(30, 35, 45), Left = 30, Top = 25, Width = 300, Height = 35 };
            Label lblHeaderSub = new Label() { Text = "نمای کلی از مصرف شبکه و سهمیه ماهانه شما", Font = new Font("Tahoma", 9), ForeColor = Color.Gray, Left = 30, Top = 60, Width = 400, Height = 25 };
            pnlDashboard.Controls.Add(lblHeaderTitle);
            pnlDashboard.Controls.Add(lblHeaderSub);

            Panel cardUsage = CreateDashboardCard(30, 105, 320, 180, "سهمیه ماهانه", "۱.۲۵ گیگابایت از ۲ گیگابایت مصرف شده\n(۶۲٪ استفاده شده)");
            Panel cardRemaining = CreateDashboardCard(365, 105, 180, 180, "باقیمانده", "۰.۷۵ گیگابایت\n۳۸٪ باقیمانده");
            Panel cardDays = CreateDashboardCard(560, 105, 180, 180, "روزهای باقی‌مانده", "۱۵ روز\nتا ریست بعدی");
            Panel cardStatus = CreateDashboardCard(755, 105, 200, 180, "وضعیت", "متصل\nهمه سیستم‌ها نرمال است");

            pnlDashboard.Controls.Add(cardUsage);
            pnlDashboard.Controls.Add(cardRemaining);
            pnlDashboard.Controls.Add(cardDays);
            pnlDashboard.Controls.Add(cardStatus);

            Panel cardAction = new Panel() { Left = 30, Top = 310, Width = 925, Height = 180, BackColor = Color.White };
            Label lblActionTitle = new Label() { Text = "تنظیم و آپدیت حجم ماهانه جدید", Font = new Font("Tahoma", 11, FontStyle.Bold), Left = 25, Top = 20, Width = 400 };
            Label lblActionDesc = new Label() { Text = "سقف مصرف ماهانه کاربر را بر اساس گیگابایت و مگابایت تعیین کنید:", Font = new Font("Tahoma", 9), ForeColor = Color.Gray, Left = 25, Top = 50, Width = 600 };
            
            // ورودی گیگابایت
            Label lblGB = new Label() { Text = "گیگابایت:", Left = 25, Top = 92, Width = 70, Font = new Font("Tahoma", 10) };
            NumericUpDown numGB = new NumericUpDown() { Left = 95, Top = 90, Width = 80, Font = new Font("Tahoma", 11), Maximum = 10000, Minimum = 0, Value = 2 };

            // ورودی مگابایت
            Label lblMB = new Label() { Text = "مگابایت:", Left = 190, Top = 92, Width = 70, Font = new Font("Tahoma", 10) };
            NumericUpDown numMB = new NumericUpDown() { Left = 260, Top = 90, Width = 80, Font = new Font("Tahoma", 11), Maximum = 1023, Minimum = 0, Value = 0 };

            Button btnUpdateLimit = new Button() { Text = "اعمال حجم جدید", Left = 360, Top = 88, Width = 200, Height = 38, BackColor = Color.FromArgb(40, 167, 69), ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Font = new Font("Tahoma", 9, FontStyle.Bold) };

            btnUpdateLimit.Click += (s, e) =>
            {
                long gbToBytes = (long)numGB.Value * 1024L * 1024L * 1024L;
                long mbToBytes = (long)numMB.Value * 1024L * 1024L;
                
                monthlyLimitBytes = gbToBytes + mbToBytes;
                currentUsageBytes = 0;
                SaveData();
                MessageBox.Show($"حجم ماهانه با موفقیت به {numGB.Value} گیگابایت و {numMB.Value} مگابایت بروزرسانی شد.", "موفقیت", MessageBoxButtons.OK, MessageBoxIcon.Information);
            };

            cardAction.Controls.Add(lblActionTitle);
            cardAction.Controls.Add(lblActionDesc);
            cardAction.Controls.Add(lblGB);
            cardAction.Controls.Add(numGB);
            cardAction.Controls.Add(lblMB);
            cardAction.Controls.Add(numMB);
            cardAction.Controls.Add(btnUpdateLimit);
            pnlDashboard.Controls.Add(cardAction);

            pnlPlaceholder = new Panel() { Dock = DockStyle.Fill, BackColor = Color.FromArgb(245, 247, 250), Padding = new Padding(20), Visible = false };
            lblPlaceholderTitle = new Label() { Text = "", Font = new Font("Tahoma", 16, FontStyle.Bold), ForeColor = Color.Gray, Left = 30, Top = 50, Width = 600, Height = 40 };
            pnlPlaceholder.Controls.Add(lblPlaceholderTitle);
        }

        private Panel CreateDashboardCard(int left, int top, int width, int height, string title, string content)
        {
            Panel pnl = new Panel() { Left = left, Top = top, Width = width, Height = height, BackColor = Color.White };
            Label lblT = new Label() { Text = title, Font = new Font("Tahoma", 9, FontStyle.Bold), ForeColor = Color.Gray, Left = 20, Top = 18, Width = width - 40 };
            Label lblC = new Label() { Text = content, Font = new Font("Tahoma", 10, FontStyle.Regular), ForeColor = Color.FromArgb(40, 40, 50), Left = 20, Top = 55, Width = width - 40, Height = height - 70 };
            pnl.Controls.Add(lblT);
            pnl.Controls.Add(lblC);
            return pnl;
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
