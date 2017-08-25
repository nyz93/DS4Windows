﻿using System;
using System.Windows.Forms;
using System.Runtime.InteropServices;
using System.IO;
using System.Reflection;
using System.Collections.Generic;
using System.Net;
using System.Management;
using System.Drawing;
using Microsoft.Win32;
using System.Diagnostics;
using System.Xml;
using System.Text;
using System.Globalization;
using Microsoft.Win32.TaskScheduler;
using System.Security.Principal;
using static DS4Windows.Global;

namespace DS4Windows
{
    public partial class DS4Form : Form
    {
        public string[] cmdArguments;
        delegate void LogDebugDelegate(DateTime Time, String Data, bool warning);
        delegate void NotificationDelegate(object sender, DebugEventArgs args);
        delegate void DeviceStatusChangedDelegate(object sender, DeviceStatusChangeEventArgs args);
        delegate void DeviceSerialChangedDelegate(object sender, SerialChangeArgs args);
        protected Label[] Pads, Batteries;
        protected ComboBox[] cbs;
        protected RadioButton[] trayBatts;
        protected int[] batteryValues;
        protected Button[] ebns;
        protected Button[] lights;
        protected PictureBox[] statPB;
        protected ToolStripMenuItem[] shortcuts;
        WebClient wc = new WebClient();
        System.Timers.Timer hotkeysTimer = new System.Timers.Timer();
        System.Timers.Timer autoProfilesTimer = new System.Timers.Timer();
        string exepath = Directory.GetParent(Assembly.GetExecutingAssembly().Location).FullName;
        string appDataPpath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData) + "\\DS4Windows";
        string oldappdatapath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData) + "\\DS4Tool";
        string tempProfileProgram = string.Empty;
        float dpix, dpiy;
        List<string> profilenames = new List<string>();
        List<string> programpaths = new List<string>();
        List<string>[] proprofiles;
        List<bool> turnOffTempProfiles;
        private static int WM_QUERYENDSESSION = 0x11;
        private static bool systemShutdown = false;
        private bool wasrunning = false;
        delegate void HotKeysDelegate(object sender, EventArgs e);
        Options opt;
        public Size oldsize;
        public bool mAllowVisible;
        bool contextclose;
        string logFile = appdatapath + @"\DS4Service.log";
        bool turnOffTemp;
        bool runningBat;
        Dictionary<Control, string> hoverTextDict = new Dictionary<Control, string>();
        // 0 index is used for application version text. 1 - 4 indices are used for controller status
        string[] notifyText = new string[5] { "DS4Windows v" + FileVersionInfo.GetVersionInfo(Assembly.GetExecutingAssembly().Location).FileVersion,
            string.Empty, string.Empty, string.Empty, string.Empty };

        internal const int BCM_FIRST = 0x1600; // Normal button
        internal const int BCM_SETSHIELD = (BCM_FIRST + 0x000C); // Elevated button

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("kernel32.dll")]
        private static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, uint dwProcessId);

        [DllImport("kernel32.dll")]
        private static extern bool CloseHandle(IntPtr handle);

        [DllImport("psapi.dll")]
        private static extern uint GetModuleBaseName(IntPtr hWnd, IntPtr hModule, StringBuilder lpFileName, int nSize);

        [DllImport("psapi.dll")]
        private static extern uint GetModuleFileNameEx(IntPtr hWnd, IntPtr hModule, StringBuilder lpFileName, int nSize);

        public DS4Form(string[] args)
        {
            InitializeComponent();
            ThemeUtil.SetTheme(lvDebug);

            bnEditC1.Tag = 0;
            bnEditC2.Tag = 1;
            bnEditC3.Tag = 2;
            bnEditC4.Tag = 3;

            this.StartWindowsCheckBox.CheckedChanged -= this.StartWindowsCheckBox_CheckedChanged;

            saveProfiles.Filter = Properties.Resources.XMLFiles + "|*.xml";
            openProfiles.Filter = Properties.Resources.XMLFiles + "|*.xml";
            cmdArguments = args;

            Pads = new Label[4] { lbPad1, lbPad2, lbPad3, lbPad4 };
            Batteries = new Label[4] { lbBatt1, lbBatt2, lbBatt3, lbBatt4 };
            trayBatts = new RadioButton[4] { rbtnTrayBatt1, rbtnTrayBatt2, rbtnTrayBatt3, rbtnTrayBatt4 };
            batteryValues = new int[4] { 0, 0, 0, 0 };
            cbs = new ComboBox[4] { cBController1, cBController2, cBController3, cBController4 };
            ebns = new Button[4] { bnEditC1, bnEditC2, bnEditC3, bnEditC4 };
            lights = new Button[4] { bnLight1, bnLight2, bnLight3, bnLight4 };
            statPB = new PictureBox[4] { pBStatus1, pBStatus2, pBStatus3, pBStatus4 };
            shortcuts = new ToolStripMenuItem[4] { (ToolStripMenuItem)notifyIcon1.ContextMenuStrip.Items[0],
                (ToolStripMenuItem)notifyIcon1.ContextMenuStrip.Items[1],
                (ToolStripMenuItem)notifyIcon1.ContextMenuStrip.Items[2],
                (ToolStripMenuItem)notifyIcon1.ContextMenuStrip.Items[3] };

            SystemEvents.PowerModeChanged += OnPowerChange;
            tSOptions.Visible = false;
            bool firstrun = false;

            if (File.Exists(exepath + "\\Auto Profiles.xml")
                && File.Exists(appDataPpath + "\\Auto Profiles.xml"))
            {
                firstrun = true;
                new SaveWhere(true).ShowDialog();
            }
            else if (File.Exists(exepath + "\\Auto Profiles.xml"))
                SaveWhere(exepath);
            else if (File.Exists(appDataPpath + "\\Auto Profiles.xml"))
                SaveWhere(appDataPpath);
            else if (File.Exists(oldappdatapath + "\\Auto Profiles.xml"))
            {
                try
                {
                    if (Directory.Exists(appDataPpath))
                        Directory.Move(appDataPpath, Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData) + "\\DS4Windows Old");
                    Directory.Move(oldappdatapath, appDataPpath);
                    SaveWhere(appDataPpath);
                }
                catch
                {
                    MessageBox.Show(Properties.Resources.CannotMoveFiles, "DS4Windows");
                    Process.Start("explorer.exe", @"/select, " + appDataPpath);
                    Close();
                    return;
                }
            }
            else if (!File.Exists(exepath + "\\Auto Profiles.xml")
                && !File.Exists(appDataPpath + "\\Auto Profiles.xml"))
            {
                firstrun = true;
                new SaveWhere(false).ShowDialog();
            }

            System.Threading.Thread AppCollectionThread = new System.Threading.Thread(() => CheckDrivers());
            AppCollectionThread.IsBackground = true;
            AppCollectionThread.Start();

            if (string.IsNullOrEmpty(appdatapath))
            {
                Close();
                return;
            }

            Graphics g = this.CreateGraphics();
            try
            {
                dpix = g.DpiX / 100f * 1.041666666667f;
                dpiy = g.DpiY / 100f * 1.041666666667f;
            }
            finally
            {
                g.Dispose();
            }

            blankControllerTab();

            Program.rootHub.Debug += On_Debug;

            Log.GuiLog += On_Debug;
            logFile = appdatapath + "\\DS4Windows.log";
            Log.TrayIconLog += ShowNotification;

            Directory.CreateDirectory(appdatapath);
            Global.Load();
            if (!Save()) //if can't write to file
            {
                if (MessageBox.Show("Cannot write at current location\nCopy Settings to appdata?", "DS4Windows",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
                {
                    try
                    {
                        Directory.CreateDirectory(appDataPpath);
                        File.Copy(exepath + "\\Profiles.xml", appDataPpath + "\\Profiles.xml");
                        File.Copy(exepath + "\\Auto Profiles.xml", appDataPpath + "\\Auto Profiles.xml");
                        Directory.CreateDirectory(appDataPpath + "\\Profiles");
                        foreach (string s in Directory.GetFiles(exepath + "\\Profiles"))
                        {
                            File.Copy(s, appDataPpath + "\\Profiles\\" + Path.GetFileName(s));
                        }
                    }
                    catch { }
                    MessageBox.Show("Copy complete, please relaunch DS4Windows and remove settings from Program Directory", "DS4Windows");
                    appdatapath = null;
                    Close();
                    return;
                }
                else
                {
                    MessageBox.Show("DS4Windows cannot edit settings here, This will now close", "DS4Windows");
                    appdatapath = null;
                    Close();
                    return;
                }
            }

            cBUseWhiteIcon.Checked = UseWhiteIcon;
            Icon = Properties.Resources.DS4W;
            notifyIcon1.Icon = UseWhiteIcon ? Properties.Resources.DS4W___White : Properties.Resources.DS4W;
            foreach (ToolStripMenuItem t in shortcuts)
                t.DropDownItemClicked += Profile_Changed_Menu;

            hideDS4CheckBox.CheckedChanged -= hideDS4CheckBox_CheckedChanged;
            hideDS4CheckBox.Checked = UseExclusiveMode;
            hideDS4CheckBox.CheckedChanged += hideDS4CheckBox_CheckedChanged;
            if (Environment.OSVersion.Version.Major >= 10 && Environment.OSVersion.Version.Build < 10586)
            {
                toolTip1.SetToolTip(hideDS4CheckBox, "For Windows 10, use button on the main tab to connect exclusivly");
                btnConnectDS4Win10.Visible = hideDS4CheckBox.Checked;
                toolTip1.SetToolTip(btnConnectDS4Win10, "This will temporarily kill the taskbar until you connect a controller");
            }
            else
                btnConnectDS4Win10.Visible = false;

            cBDisconnectBT.Checked = DCBTatStop;
            cBQuickCharge.Checked = QuickCharge;
            nUDXIPorts.Value = FirstXinputPort;
            Program.rootHub.x360Bus.FirstController = FirstXinputPort;
            // New settings
            this.Width = FormWidth;
            this.Height = FormHeight;
            startMinimizedCheckBox.CheckedChanged -= startMinimizedCheckBox_CheckedChanged;
            startMinimizedCheckBox.Checked = StartMinimized;
            startMinimizedCheckBox.CheckedChanged += startMinimizedCheckBox_CheckedChanged;
            cBCloseMini.Checked = CloseMini;
            string lang = CultureInfo.CurrentCulture.ToString();
            if (lang.StartsWith("en"))
                cBDownloadLangauge.Visible = false;

            cBDownloadLangauge.Checked = DownloadLang;
            cBFlashWhenLate.Checked = FlashWhenLate;
            nUDLatency.Value = FlashWhenLateAt;

            if (!LoadActions()) //if first no actions have been made yet, create PS+Option to D/C and save it to every profile
            {
                XmlDocument xDoc = new XmlDocument();
                try
                {
                    string[] profiles = Directory.GetFiles(appdatapath + @"\Profiles\");
                    foreach (string s in profiles)
                    {
                        if (Path.GetExtension(s) == ".xml")
                        {
                            xDoc.Load(s);
                            XmlNode el = xDoc.SelectSingleNode("DS4Windows/ProfileActions");
                            if (el != null)
                            {
                                if (string.IsNullOrEmpty(el.InnerText))
                                    el.InnerText = "Disconnect Controller";
                                else
                                    el.InnerText += "/Disconnect Controller";
                            }
                            else
                            {
                                XmlNode Node = xDoc.SelectSingleNode("DS4Windows");
                                el = xDoc.CreateElement("ProfileActions");
                                el.InnerText = "Disconnect Controller";
                                Node.AppendChild(el);
                            }

                            xDoc.Save(s);
                            LoadActions();
                        }
                    }
                }
                catch { }
            }

            bool start = true;
            bool mini = false;
            for (int i = 0, argslen = cmdArguments.Length; i < argslen; i++)
            {
                if (cmdArguments[i] == "-stop")
                    start = false;
                else if (cmdArguments[i] == "-m")
                    mini = true;

                if (mini && start)
                    break;
            }

            if (!(startMinimizedCheckBox.Checked || mini))
            {
                mAllowVisible = true;
                Show();
            }

            Form_Resize(null, null);
            RefreshProfiles();
            opt = new Options(this);
            opt.Icon = this.Icon;
            opt.TopLevel = false;
            opt.Dock = DockStyle.Fill;
            opt.FormBorderStyle = FormBorderStyle.None;
            tabProfiles.Controls.Add(opt);

            for (int i = 0; i < 4; i++)
            {
                LoadProfile(i, false, Program.rootHub, false);
                if (UseCustomLed[i])
                    lights[i].BackColor = CustomColor[i].ToColorA;
                else
                    lights[i].BackColor = MainColor[i].ToColorA;
            }

            autoProfilesTimer.Elapsed += CheckAutoProfiles;
            autoProfilesTimer.Interval = 1000;

            LoadP();

            Global.BatteryStatusChange += BatteryStatusUpdate;
            Global.ControllerRemoved += ControllerRemovedChange;
            Global.DeviceStatusChange += DeviceStatusChanged;
            Global.DeviceSerialChange += DeviceSerialChanged;

            Enable_Controls(0, false);
            Enable_Controls(1, false);
            Enable_Controls(2, false);
            Enable_Controls(3, false);
            btnStartStop.Text = Properties.Resources.StartText;

            hotkeysTimer.Elapsed += Hotkeys;
            if (SwipeProfiles)
            {
                hotkeysTimer.Start();
            }

            if (btnStartStop.Enabled && start)
                btnStartStop_Clicked();

            startToolStripMenuItem.Text = btnStartStop.Text;
            cBoxNotifications.SelectedIndex = Notifications;
            cBSwipeProfiles.Checked = SwipeProfiles;
            int checkwhen = CheckWhen;
            cBUpdate.Checked = checkwhen > 0;
            if (checkwhen > 23)
            {
                cBUpdateTime.SelectedIndex = 1;
                nUDUpdateTime.Value = checkwhen / 24;
            }
            else
            {
                cBUpdateTime.SelectedIndex = 0;
                nUDUpdateTime.Value = checkwhen;
            }

            Uri url = new Uri("http://23.239.26.40/ds4windows/files/builds/newest.txt"); // Sorry other devs, gonna have to find your own server

            if (checkwhen > 0 && DateTime.Now >= LastChecked + TimeSpan.FromHours(checkwhen))
            {
                wc.DownloadFileAsync(url, appdatapath + "\\version.txt");
                wc.DownloadFileCompleted += Check_Version;
                LastChecked = DateTime.Now;
            }

            if (File.Exists(exepath + "\\Updater.exe"))
            {
                System.Threading.Thread.Sleep(2000);
                File.Delete(exepath + "\\Updater.exe");
            }

            if (!Directory.Exists(appdatapath + "\\Virtual Bus Driver"))
                linkUninstall.Visible = false;

            bool isElevated = Global.IsAdministrator();
            if (!isElevated)
            {
                Image tempImg = new Bitmap(uacPictureBox.Width, uacPictureBox.Height);
                AddUACShieldToImage(tempImg);
                uacPictureBox.BackgroundImage = tempImg;
                uacPictureBox.Visible = true;
                new ToolTip().SetToolTip(uacPictureBox, Properties.Resources.UACTask);
                runStartTaskRadio.Enabled = false;
            }
            else
            {
                runStartTaskRadio.Enabled = true;
            }


            if (File.Exists(Environment.GetFolderPath(Environment.SpecialFolder.Startup) + "\\DS4Windows.lnk"))
            {
                StartWindowsCheckBox.Checked = true;
                runStartupPanel.Visible = true;

                string lnkpath = WinProgs.ResolveShortcutAndArgument(Environment.GetFolderPath(Environment.SpecialFolder.Startup) + "\\DS4Windows.lnk");
                string onlylnkpath = WinProgs.ResolveShortcut(Environment.GetFolderPath(Environment.SpecialFolder.Startup) + "\\DS4Windows.lnk");
                if (!lnkpath.EndsWith("-runtask"))
                {
                    runStartProgRadio.Checked = true;
                }
                else
                {
                    runStartTaskRadio.Checked = true;
                }

                if (onlylnkpath != Process.GetCurrentProcess().MainModule.FileName)
                {
                    File.Delete(Environment.GetFolderPath(Environment.SpecialFolder.Startup) + "\\DS4Windows.lnk");
                    appShortcutToStartup();
                    changeStartupRoutine();
                }
            }

            System.Threading.Tasks.Task.Run(() => { UpdateTheUpdater(); });

            this.StartWindowsCheckBox.CheckedChanged += new EventHandler(this.StartWindowsCheckBox_CheckedChanged);
            new ToolTip().SetToolTip(StartWindowsCheckBox, Properties.Resources.RunAtStartup);

            populateHoverTextDict();

            foreach (Control control in fLPSettings.Controls)
            {
                if (control.HasChildren)
                {
                    foreach (Control ctrl in control.Controls)
                        ctrl.MouseHover += Items_MouseHover;
                }

                control.MouseHover += Items_MouseHover;
            }
        }

        private void populateHoverTextDict()
        {
            hoverTextDict.Clear();
            hoverTextDict[linkUninstall] = Properties.Resources.IfRemovingDS4Windows;
            hoverTextDict[cBSwipeProfiles] = Properties.Resources.TwoFingerSwipe;
            hoverTextDict[cBQuickCharge] = Properties.Resources.QuickCharge;
            hoverTextDict[pnlXIPorts] = Properties.Resources.XinputPorts;
            hoverTextDict[lbUseXIPorts] = Properties.Resources.XinputPorts;
            hoverTextDict[nUDXIPorts] = Properties.Resources.XinputPorts;
            hoverTextDict[lbLastXIPort] = Properties.Resources.XinputPorts;
            hoverTextDict[cBCloseMini] = Properties.Resources.CloseMinimize;
            hoverTextDict[uacPictureBox] = Properties.Resources.UACTask;
            hoverTextDict[StartWindowsCheckBox] = Properties.Resources.RunAtStartup;
        }

        private Image AddUACShieldToImage(Image image)
        {
            Bitmap shield = SystemIcons.Shield.ToBitmap();
            shield.MakeTransparent();

            Graphics g = Graphics.FromImage(image);
            g.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceOver;
            double aspectRatio = shield.Width / (double)shield.Height;
            int finalWidth = Convert.ToInt32(image.Height * aspectRatio);
            int finalHeight = Convert.ToInt32(image.Width / aspectRatio);
            g.DrawImage(shield, new Rectangle(0, 0, finalWidth, finalHeight));

            return image;
        }

        private void blankControllerTab()
        {
            bool nocontrollers = true;
            for (Int32 Index = 0, PadsLen = Pads.Length; Index < PadsLen; Index++)
            {
                // Make sure a controller exists
                if (Index < ControlService.DS4_CONTROLLER_COUNT)
                {
                    Pads[Index].Text = "";

                    statPB[Index].Visible = false; toolTip1.SetToolTip(statPB[Index], "");
                    Batteries[Index].Text = Properties.Resources.NA;
                    Pads[Index].Text = Properties.Resources.Disconnected;
                    Enable_Controls(Index, false);
                }
            }

            lbNoControllers.Visible = nocontrollers;
            tLPControllers.Visible = !nocontrollers;
        }

        private void UpdateTheUpdater()
        {
            if (File.Exists(exepath + "\\Update Files\\DS4Updater.exe"))
            {
                Process[] processes = Process.GetProcessesByName("DS4Updater");
                while (processes.Length > 0)
                {
                    System.Threading.Thread.Sleep(500);
                }

                File.Delete(exepath + "\\DS4Updater.exe");
                File.Move(exepath + "\\Update Files\\DS4Updater.exe", exepath + "\\DS4Updater.exe");
                Directory.Delete(exepath + "\\Update Files");
            }
        }

        protected override void SetVisibleCore(bool value)
        {
            if (!mAllowVisible)
            {
                value = false;
                if (!this.IsHandleCreated) CreateHandle();
            }

            base.SetVisibleCore(value);
        }

        public static string GetTopWindowName()
        {
            IntPtr hWnd = GetForegroundWindow();
            uint lpdwProcessId;
            GetWindowThreadProcessId(hWnd, out lpdwProcessId);

            IntPtr hProcess = OpenProcess(0x0410, false, lpdwProcessId);

            StringBuilder text = new StringBuilder(1000);
            GetModuleFileNameEx(hProcess, IntPtr.Zero, text, text.Capacity);

            CloseHandle(hProcess);

            return text.ToString();
        }

        private void OnPowerChange(object s, PowerModeChangedEventArgs e)
        {
            switch (e.Mode)
            {
                case PowerModes.Resume:
                {
                    if (btnStartStop.Text == Properties.Resources.StartText && wasrunning)
                    {
                        DS4LightBar.shuttingdown = false;
                        wasrunning = false;
                        btnStartStop_Clicked();
                    }
                    break;
                }
                case PowerModes.Suspend:
                {
                    if (btnStartStop.Text == Properties.Resources.StopText)
                    {
                        DS4LightBar.shuttingdown = true;
                        btnStartStop_Clicked();
                        wasrunning = true;
                    }
                    break;
                }
                default: break;
            }
        }

        void Hotkeys(object sender, EventArgs e)
        {
            if (SwipeProfiles)
            {
                for (int i = 0; i < 4; i++)
                {
                    string slide = Program.rootHub.TouchpadSlide(i);
                    if (slide == "left")
                    {
                        if (cbs[i].SelectedIndex <= 0)
                            cbs[i].SelectedIndex = cbs[i].Items.Count - 2;
                        else
                            cbs[i].SelectedIndex--;

                    }
                    else if (slide == "right")
                    {
                        if (cbs[i].SelectedIndex == cbs[i].Items.Count - 2)
                            cbs[i].SelectedIndex = 0;
                        else
                            cbs[i].SelectedIndex++;
                    }

                    if (slide.Contains("t"))
                        ShowNotification(this, Properties.Resources.UsingProfile.Replace("*number*", (i + 1).ToString()).Replace("*Profile name*", cbs[i].Text));
                }
            }

            if (bat != null && bat.HasExited && runningBat)
            {
                Process.Start("explorer.exe");
                bat = null;
                runningBat = false;
            }
        }

        private void CheckAutoProfiles(object sender, EventArgs e)
        {
            //Check for process for auto profiles
            if (string.IsNullOrEmpty(tempProfileProgram))
            {
                string windowName = GetTopWindowName().ToLower().Replace('/', '\\');
                for (int i = 0, pathsLen = programpaths.Count; i < pathsLen; i++)
                {
                    string name = programpaths[i].ToLower().Replace('/', '\\');
                    if (name == windowName)
                    {
                        for (int j = 0; j < 4; j++)
                        {
                            if (proprofiles[j][i] != "(none)" && proprofiles[j][i] != Properties.Resources.noneProfile)
                            {
                                LoadTempProfile(j, proprofiles[j][i], true, Program.rootHub); // j is controller index, i is filename
                                //if (LaunchProgram[j] != string.Empty) Process.Start(LaunchProgram[j]);
                            }
                        }

                        if (turnOffTempProfiles[i])
                        {
                            turnOffTemp = true;
                            if (btnStartStop.Text == Properties.Resources.StopText)
                            {
                                btnStartStop_Clicked();
                                hotkeysTimer.Start();
                                autoProfilesTimer.Start();
                                btnStartStop.Text = Properties.Resources.StartText;
                            }
                        }

                        tempProfileProgram = name;
                        break;
                    }
                }
            }
            else
            {
                string windowName = GetTopWindowName().ToLower().Replace('/', '\\');
                if (tempProfileProgram != windowName)
                {
                    tempProfileProgram = string.Empty;
                    for (int j = 0; j < 4; j++)
                        LoadProfile(j, false, Program.rootHub);

                    if (turnOffTemp)
                    {
                        turnOffTemp = false;
                        if (btnStartStop.Text == Properties.Resources.StartText)
                        {
                            btnStartStop_Clicked();
                            btnStartStop.Text = Properties.Resources.StopText;
                        }
                    }
                }
            }

            //GC.Collect();
        }

        public void LoadP()
        {
            XmlDocument doc = new XmlDocument();
            proprofiles = new List<string>[4] { new List<string>(), new List<string>(),
                new List<string>(), new List<string>() };
            turnOffTempProfiles = new List<bool>();
            programpaths.Clear();
            if (!File.Exists(appdatapath + "\\Auto Profiles.xml"))
                return;

            doc.Load(appdatapath + "\\Auto Profiles.xml");
            XmlNodeList programslist = doc.SelectNodes("Programs/Program");
            foreach (XmlNode x in programslist)
                programpaths.Add(x.Attributes["path"].Value);

            foreach (string s in programpaths)
            {
                for (int i = 0; i < 4; i++)
                {
                    proprofiles[i].Add(doc.SelectSingleNode("/Programs/Program[@path=\"" + s + "\"]"
                        + "/Controller" + (i + 1)).InnerText);
                }

                XmlNode item = doc.SelectSingleNode("/Programs/Program[@path=\"" + s + "\"]"
                        + "/TurnOff");
                bool turnOff;
                if (item != null && bool.TryParse(item.InnerText, out turnOff))
                    turnOffTempProfiles.Add(turnOff);
                else
                    turnOffTempProfiles.Add(false);
            }

            int pathCount = programpaths.Count;
            bool timerEnabled = autoProfilesTimer.Enabled;
            if (pathCount > 0 && !timerEnabled)
            {
                autoProfilesTimer.Start();
            }
            else if (pathCount == 0 && timerEnabled)
            {
                autoProfilesTimer.Stop();
            }
        }

        string originalsettingstext;
        private void CheckDrivers()
        {
            originalsettingstext = tabSettings.Text;
            bool deriverinstalled = false;
            try
            {
                ManagementObjectSearcher searcher = new ManagementObjectSearcher("SELECT * FROM Win32_PnPSignedDriver");

                foreach (ManagementObject obj in searcher.Get())
                {
                    try
                    {
                        if (obj.GetPropertyValue("DeviceName").ToString() == "Scp Virtual Bus Driver")
                        {
                            deriverinstalled = true;
                            break;
                        }
                    }
                    catch { }
                }

                if (!deriverinstalled)
                {
                    Process p = new Process();
                    p.StartInfo.FileName = Assembly.GetExecutingAssembly().Location;
                    p.StartInfo.Arguments = "driverinstall";
                    p.StartInfo.Verb = "runas";
                    try { p.Start(); }
                    catch { }
                }
            }
            catch
            {
                if (!File.Exists(exepath + "\\Auto Profiles.xml") && !File.Exists(appDataPpath + "\\Auto Profiles.xml"))
                {
                    linkSetup.LinkColor = Color.Green;
                    tabSettings.Text += " (" + Properties.Resources.InstallDriver + ")";
                }
            }
        }

        private void Check_Version(object sender, System.ComponentModel.AsyncCompletedEventArgs e)
        {
            FileVersionInfo fvi = FileVersionInfo.GetVersionInfo(Assembly.GetExecutingAssembly().Location);
            string version = fvi.FileVersion;
            string newversion = File.ReadAllText(appdatapath + "\\version.txt").Trim();
            if (version.Replace(',', '.').CompareTo(newversion) == -1)//CompareVersions();
            {
                if (MessageBox.Show(Properties.Resources.DownloadVersion.Replace("*number*", newversion),
                    Properties.Resources.DS4Update, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                {
                    if (!File.Exists(exepath + "\\DS4Updater.exe") || (File.Exists(exepath + "\\DS4Updater.exe")
                        && (FileVersionInfo.GetVersionInfo(exepath + "\\DS4Updater.exe").FileVersion.CompareTo("1.1.0.0") == -1)))
                    {
                        Uri url2 = new Uri("http://23.239.26.40/ds4windows/files/DS4Updater.exe");
                        WebClient wc2 = new WebClient();
                        if (appdatapath == exepath)
                            wc2.DownloadFile(url2, exepath + "\\DS4Updater.exe");
                        else
                        {
                            MessageBox.Show(Properties.Resources.PleaseDownloadUpdater);
                            Process.Start("http://23.239.26.40/ds4windows/files/DS4Updater.exe");
                        }
                    }

                    Process p = new Process();
                    p.StartInfo.FileName = exepath + "\\DS4Updater.exe";
                    if (!cBDownloadLangauge.Checked)
                        p.StartInfo.Arguments = "-skipLang";
                    if (AdminNeeded())
                        p.StartInfo.Verb = "runas";

                    try { p.Start(); Close(); }
                    catch { }
                }
                else
                    File.Delete(appdatapath + "\\version.txt");
            }
            else
                File.Delete(appdatapath + "\\version.txt");
        }

        public void RefreshProfiles()
        {
            try
            {
                profilenames.Clear();
                string[] profiles = Directory.GetFiles(appdatapath + @"\Profiles\");
                foreach (string s in profiles)
                {
                    if (s.EndsWith(".xml"))
                        profilenames.Add(Path.GetFileNameWithoutExtension(s));
                }

                lBProfiles.Items.Clear();
                lBProfiles.Items.AddRange(profilenames.ToArray());
                if (lBProfiles.Items.Count == 0)
                {
                    SaveProfile(0, "Default");
                    ProfilePath[0] = "Default";
                    RefreshProfiles();
                    return;
                }
                for (int i = 0; i < 4; i++)
                {
                    cbs[i].Items.Clear();
                    shortcuts[i].DropDownItems.Clear();
                    cbs[i].Items.AddRange(profilenames.ToArray());
                    foreach (string s in profilenames)
                        shortcuts[i].DropDownItems.Add(s);

                    for (int j = 0, itemCount = cbs[i].Items.Count; j < itemCount; j++)
                    {
                        if (cbs[i].Items[j].ToString() == Path.GetFileNameWithoutExtension(ProfilePath[i]))
                        {
                            cbs[i].SelectedIndex = j;
                            ((ToolStripMenuItem)shortcuts[i].DropDownItems[j]).Checked = true;
                            ProfilePath[i] = cbs[i].Text;
                            shortcuts[i].Text = Properties.Resources.ContextEdit.Replace("*number*", (i + 1).ToString());
                            ebns[i].Text = Properties.Resources.EditProfile;
                            break;
                        }
                        else
                        {
                            cbs[i].Text = "(" + Properties.Resources.NoProfileLoaded + ")";
                            shortcuts[i].Text = Properties.Resources.ContextNew.Replace("*number*", (i + 1).ToString());
                            ebns[i].Text = Properties.Resources.New;
                        }
                    }
                }
            }
            catch (DirectoryNotFoundException)
            {
                Directory.CreateDirectory(appdatapath + @"\Profiles\");
                SaveProfile(0, "Default");
                ProfilePath[0] = "Default";
                RefreshProfiles();
                return;
            }
            finally
            {
                if (!(cbs[0].Items.Count > 0 && cbs[0].Items[cbs[0].Items.Count - 1].ToString() == "+" + Properties.Resources.PlusNewProfile))
                {
                    for (int i = 0; i < 4; i++)
                    {
                        cbs[i].Items.Add("+" + Properties.Resources.PlusNewProfile);
                        shortcuts[i].DropDownItems.Add("-");
                        shortcuts[i].DropDownItems.Add("+" + Properties.Resources.PlusNewProfile);
                    }
                    RefreshAutoProfilesPage();
                }
            }
        }


        public void RefreshAutoProfilesPage()
        {
            tabAutoProfiles.Controls.Clear();
            WinProgs WP = new WinProgs(profilenames.ToArray(), this);
            WP.TopLevel = false;
            WP.FormBorderStyle = FormBorderStyle.None;
            WP.Visible = true;
            WP.Dock = DockStyle.Fill;
            tabAutoProfiles.Controls.Add(WP);
        }

        protected void LogDebug(DateTime Time, String Data, bool warning)
        {
            if (lvDebug.InvokeRequired)
            {
                LogDebugDelegate d = new LogDebugDelegate(LogDebug);
                try
                {
                    // Make sure to invoke method asynchronously instead of waiting for result
                    this.BeginInvoke(d, new object[] { Time, Data, warning });
                    //this.Invoke(d, new object[] { Time, Data, warning });
                }
                catch { }
            }
            else
            {
                String Posted = Time.ToString("G");
                lvDebug.Items.Add(new ListViewItem(new String[] { Posted, Data })).EnsureVisible();
                if (warning) lvDebug.Items[lvDebug.Items.Count - 1].ForeColor = Color.Red;
                // Added alternative
                lbLastMessage.Text = Data;
                lbLastMessage.ForeColor = (warning ? Color.Red : SystemColors.GrayText);
            }
        }

        protected void ShowNotification(object sender, DebugEventArgs args)
        {
            if (this.InvokeRequired)
            {
                NotificationDelegate d = new NotificationDelegate(ShowNotification);

                try
                {
                    // Make sure to invoke method asynchronously instead of waiting for result
                    this.BeginInvoke(d, new object[] { sender, args });
                }
                catch { }
            }
            else
            {
                if (Form.ActiveForm != this && (Notifications == 2 || (Notifications == 1 && args.Warning) || sender != null))
                {
                    this.notifyIcon1.BalloonTipText = args.Data;
                    notifyIcon1.BalloonTipTitle = "DS4Windows";
                    notifyIcon1.ShowBalloonTip(1);
                }
            }
        }

        protected void ShowNotification(object sender, string text)
        {
            if (Form.ActiveForm != this && Notifications == 2)
            {
                this.notifyIcon1.BalloonTipText = text;
                notifyIcon1.BalloonTipTitle = "DS4Windows";
                notifyIcon1.ShowBalloonTip(1);
            }
        }

        protected void Form_Resize(object sender, EventArgs e)
        {
            if (FormWindowState.Minimized == this.WindowState)
            {
                this.Hide();
                this.ShowInTaskbar = false;
                this.FormBorderStyle = FormBorderStyle.None;
            }

            else if (FormWindowState.Normal == this.WindowState)
            {
                //mAllowVisible = true;
                this.Show();
                this.ShowInTaskbar = true;
                this.FormBorderStyle = FormBorderStyle.Sizable;
            }

            chData.AutoResize(ColumnHeaderAutoResizeStyle.HeaderSize);
        }

        protected void btnStartStop_Click(object sender, EventArgs e)
        {
            btnStartStop_Clicked();
        }

        public void btnStartStop_Clicked(bool log = true)
        {
            if (btnStartStop.Text == Properties.Resources.StartText)
            {
                Program.rootHub.Start(log);
                if (SwipeProfiles && !hotkeysTimer.Enabled)
                {
                    hotkeysTimer.Start();
                }

                if (programpaths.Count > 0 && !autoProfilesTimer.Enabled)
                {
                    autoProfilesTimer.Start();
                }

                btnStartStop.Text = Properties.Resources.StopText;
            }
            else if (btnStartStop.Text == Properties.Resources.StopText)
            {
                blankControllerTab();
                Program.rootHub.Stop(log);
                hotkeysTimer.Stop();
                autoProfilesTimer.Stop();
                btnStartStop.Text = Properties.Resources.StartText;
                blankControllerTab();
                populateFullNotifyText();
            }

            startToolStripMenuItem.Text = btnStartStop.Text;
        }

        protected void btnClear_Click(object sender, EventArgs e)
        {
            lvDebug.Items.Clear();
            lbLastMessage.Text = string.Empty;
        }

        private bool inHotPlug = false;
        private int hotplugCounter = 0;
        private object hotplugCounterLock = new object();
        protected override void WndProc(ref Message m)
        {
            try
            {
                if (m.Msg == ScpDevice.WM_DEVICECHANGE)
                {
                    if (runHotPlug)
                    {
                        Int32 Type = m.WParam.ToInt32();
                        lock (hotplugCounterLock)
                        {
                            hotplugCounter++;
                        }

                        if (!inHotPlug)
                        {
                            inHotPlug = true;
                            System.Threading.Tasks.Task.Run(() => { InnerHotplug2(); });
                        }
                    }
                }
            }
            catch { }
            if (m.Msg == WM_QUERYENDSESSION)
                systemShutdown = true;

            // If this is WM_QUERYENDSESSION, the closing event should be
            // raised in the base WndProc.
            try { base.WndProc(ref m); }
            catch { }
        }

        protected void InnerHotplug2()
        {
            inHotPlug = true;
            System.Threading.SynchronizationContext uiContext =
                System.Threading.SynchronizationContext.Current;
            bool loopHotplug = false;
            lock (hotplugCounterLock)
            {
                loopHotplug = hotplugCounter > 0;
            }

            while (loopHotplug == true)
            {
                Program.rootHub.HotPlug(uiContext);
                //System.Threading.Tasks.Task.Run(() => { Program.rootHub.HotPlug(uiContext); });
                lock (hotplugCounterLock)
                {
                    hotplugCounter--;
                    loopHotplug = hotplugCounter > 0;
                }
            }

            inHotPlug = false;
        }

        protected void BatteryStatusUpdate(object sender, BatteryReportArgs args)
        {
            string battery;
            int level = args.getLevel();
            bool charging = args.isCharging();
            int Index = args.getIndex();
            if (charging)
            {
                if (level >= 100)
                    battery = Properties.Resources.Full;
                else
                    battery = level + "%+";
            }
            else
            {
                battery = level + "%";
            }

	    Batteries[args.getIndex()].Text = battery;
	    Batteries[args.getIndex()].Text = battery;
	    batteryValues[args.getIndex()] = level;

	    // Update device battery level display for tray icon
	    generateDeviceNotifyText(args.getIndex());
	    generateTrayIconLevel(args.getIndex());
	    populateNotifyText();
        }

        protected void populateFullNotifyText()
        {
            for (int i = 0; i < ControlService.DS4_CONTROLLER_COUNT; i++)
            {
                string temp = Program.rootHub.getShortDS4ControllerInfo(i);
                if (temp != Properties.Resources.NoneText)
                {
                    notifyText[i + 1] = (i + 1) + ": " + temp; // Carefully stay under the 63 character limit.
                }
                else
                {
                    notifyText[i + 1] = string.Empty;
                }
            }

            populateNotifyText();

            /*string tooltip = "DS4Windows v" + FileVersionInfo.GetVersionInfo(Assembly.GetExecutingAssembly().Location).FileVersion;
            for (int i = 0; i < ControlService.DS4_CONTROLLER_COUNT; i++)
            {
                string temp = Program.rootHub.getShortDS4ControllerInfo(i);
                if (temp != Properties.Resources.NoneText)
                    tooltip += "\n" + (i + 1) + ": " + temp; // Carefully stay under the 63 character limit.
            }

            if (tooltip.Length > 63)
                notifyIcon1.Text = tooltip.Substring(0, 63);
            else
                notifyIcon1.Text = tooltip;
            */
        }

        protected void generateDeviceNotifyText(int index)
        {
            string temp = Program.rootHub.getShortDS4ControllerInfo(index);
            if (temp != Properties.Resources.NoneText)
            {
                notifyText[index + 1] = (index + 1) + ": " + temp; // Carefully stay under the 63 character limit.
            }
            else
            {
                notifyText[index + 1] = string.Empty;
            }
        }
        protected void generateTrayIconLevel(int index)
        {
            if(index == Global.TrayBatteryIndex)
            {
                int level = batteryValues[index];
                using (Bitmap bmp = new Bitmap(32, 32))
                using (Graphics gr = Graphics.FromImage(bmp))
                {
                    int top = (int)(28.0 - level * .28);
                    gr.Clear(Color.Black);
                    gr.FillRectangle(Brushes.Transparent, new Rectangle(2, 2, 28, 28));
                    gr.FillRectangle(Brushes.Green, new Rectangle(2, top+2, 28, 28 - top));

                    notifyIcon1.Icon = Icon.FromHandle(bmp.GetHicon());
                }
            }
        }
        protected void populateNotifyText()
        {
            string tooltip = notifyText[0];
            for (int i = 1; i < 5; i++)
            {
                string temp = notifyText[i];
                if (!string.IsNullOrEmpty(temp))
                {
                    tooltip += "\n" + notifyText[i]; // Carefully stay under the 63 character limit.
                }
            }

            if (tooltip.Length > 63)
                notifyIcon1.Text = tooltip.Substring(0, 63);
            else
                notifyIcon1.Text = tooltip;
        }

        protected void DeviceSerialChanged(object sender, SerialChangeArgs args)
        {
            if (InvokeRequired)
            {
                DeviceSerialChangedDelegate d = new DeviceSerialChangedDelegate(DeviceSerialChanged);
                this.BeginInvoke(d, new object[] { sender, args });
            }
            else
            {
                int devIndex = args.getIndex();
                string serial = args.getSerial();
                if (devIndex >= 0 && devIndex < ControlService.DS4_CONTROLLER_COUNT)
                {
                    Pads[devIndex].Text = serial;
                }
            }
        }

        protected void DeviceStatusChanged(object sender, DeviceStatusChangeEventArgs args)
        {
            if (this.InvokeRequired)
            {
                DeviceStatusChangedDelegate d = new DeviceStatusChangedDelegate(DeviceStatusChanged);
                this.BeginInvoke(d, new object[] { sender, args });
            }
            else
            {
                bool nocontrollers = true;
                for (int i = 0, arlen = Program.rootHub.DS4Controllers.Length; nocontrollers && i < arlen; i++)
                {
                    DS4Device dev = Program.rootHub.DS4Controllers[i];
                    if (dev != null)
                    {
                        nocontrollers = false;
                    }
                }

                //string tooltip = "DS4Windows v" + FileVersionInfo.GetVersionInfo(Assembly.GetExecutingAssembly().Location).FileVersion;
                int Index = args.getIndex();
                if (Index >= 0 && Index < ControlService.DS4_CONTROLLER_COUNT)
                {
                    Pads[Index].Text = Program.rootHub.getDS4MacAddress(Index);

                    switch (Program.rootHub.getDS4Status(Index))
                    {
                        case "USB": statPB[Index].Visible = true; statPB[Index].Image = Properties.Resources.USB; toolTip1.SetToolTip(statPB[Index], ""); break;
                        case "BT": statPB[Index].Visible = true; statPB[Index].Image = Properties.Resources.BT; toolTip1.SetToolTip(statPB[Index], "Right click to disconnect"); break;
                        case "SONYWA": statPB[Index].Visible = true; statPB[Index].Image = Properties.Resources.BT; toolTip1.SetToolTip(statPB[Index], "Right click to disconnect"); break;
                        default: statPB[Index].Visible = false; toolTip1.SetToolTip(statPB[Index], ""); break;
                    }

                    Batteries[Index].Text = Program.rootHub.getDS4Battery(Index);
                    batteryValues[Index] = Program.rootHub.getDS4BatteryValue(Index);
                    if (Pads[Index].Text != String.Empty)
                    {
                        if (runningBat)
                        {
                            SendKeys.Send("A");
                            runningBat = false;
                        }

                        Pads[Index].Enabled = true;
                        if (Pads[Index].Text != Properties.Resources.Connecting)
                        {
                            Enable_Controls(Index, true);
                        }
                    }
                    else
                    {
                        Pads[Index].Text = Properties.Resources.Disconnected;
                        Enable_Controls(Index, false);
                    }

                    generateDeviceNotifyText(Index);
                    generateTrayIconLevel(Index);
                    populateNotifyText();
                    //if (Program.rootHub.getShortDS4ControllerInfo(Index) != Properties.Resources.NoneText)
                    //    tooltip += "\n" + (Index + 1) + ": " + Program.rootHub.getShortDS4ControllerInfo(Index); // Carefully stay under the 63 character limit.
                }

                lbNoControllers.Visible = nocontrollers;
                tLPControllers.Visible = !nocontrollers;
                /*if (tooltip.Length > 63)
                    notifyIcon1.Text = tooltip.Substring(0, 63);
                else
                    notifyIcon1.Text = tooltip;
                */
            }
        }

        protected void ControllerRemovedChange(object sender, ControllerRemovedArgs args)
        {
<<<<<<< HEAD
            int devIndex = args.getIndex();
            Pads[devIndex].Text = Properties.Resources.Disconnected;
            Enable_Controls(devIndex, false);
            statPB[devIndex].Visible = false;
            toolTip1.SetToolTip(statPB[devIndex], "");
=======
            if (this.InvokeRequired)
            {
                try
                {
                    ControllerRemovedDelegate d = new ControllerRemovedDelegate(ControllerRemovedChange);
                    this.BeginInvoke(d, new object[] { sender, args });
                }
                catch { }
            }
            else
            {
                int devIndex = args.getIndex();
                Pads[devIndex].Text = Properties.Resources.Disconnected;
                Enable_Controls(devIndex, false);
                statPB[devIndex].Visible = false;
                toolTip1.SetToolTip(statPB[devIndex], "");

                DS4Device[] devices = Program.rootHub.DS4Controllers;
                int controllerLen = devices.Length;
                bool nocontrollers = true;
                for (Int32 i = 0, PadsLen = Pads.Length; nocontrollers && i < PadsLen; i++)
                {
                    DS4Device d = devices[i];
                    if (d != null)
                    {
                        nocontrollers = false;
                    }
                }

                lbNoControllers.Visible = nocontrollers;
                tLPControllers.Visible = !nocontrollers;

                // Update device battery level display for tray icon
                generateDeviceNotifyText(devIndex);
                generateTrayIconLevel(devIndex);
                populateNotifyText();
            }
        }
>>>>>>> charge stuff and wpf

            DS4Device[] devices = Program.rootHub.DS4Controllers;
            int controllerLen = devices.Length;
            bool nocontrollers = true;
            for (Int32 i = 0, PadsLen = Pads.Length; nocontrollers && i < PadsLen; i++)
            {
                DS4Device d = devices[i];
                if (d != null)
                {
                    nocontrollers = false;
                }
            }

            lbNoControllers.Visible = nocontrollers;
            tLPControllers.Visible = !nocontrollers;

            // Update device battery level display for tray icon
            generateDeviceNotifyText(devIndex);
            populateNotifyText();
        }

        private void pBStatus_MouseClick(object sender, MouseEventArgs e)
        {
            int i = Convert.ToInt32(((PictureBox)sender).Tag);
            DS4Device d = Program.rootHub.DS4Controllers[i];
            if (d != null)
            {
                if (e.Button == MouseButtons.Right && Program.rootHub.getDS4Status(i) == "BT" && !d.Charging)
                {
                    d.DisconnectBT();
                }
                else if (e.Button == MouseButtons.Right &&
                    Program.rootHub.getDS4Status(i) == "SONYWA" && !d.Charging)
                {
                    d.DisconnectDongle();
                }
            }
        }

        private void Enable_Controls(int device, bool on)
        {
            Pads[device].Visible = on;
            ebns[device].Visible = on;
            lights[device].Visible = on;
            cbs[device].Visible = on;
            shortcuts[device].Visible = on;
            Batteries[device].Visible = on;
            trayBatts[device].Visible = on;
        }

        protected void On_Debug(object sender, DebugEventArgs e)
        {
            LogDebug(e.Time, e.Data, e.Warning);
        }


        private void lBProfiles_MouseDoubleClick(object sender, MouseEventArgs e)
        {
            if (lBProfiles.SelectedIndex >= 0)
                ShowOptions(4, lBProfiles.SelectedItem.ToString());
        }


        private void lBProfiles_KeyDown(object sender, KeyEventArgs e)
        {
            if (lBProfiles.SelectedIndex >= 0 && !opt.Visible)
            {
                if (e.KeyValue == 13)
                    ShowOptions(4, lBProfiles.SelectedItem.ToString());
                else if (e.KeyValue == 46)
                    tsBDeleteProfle_Click(this, e);
                else if (e.KeyValue == 68 && e.Modifiers == Keys.Control)
                    tSBDupProfile_Click(this, e);
            }
        }

        private void assignToController1ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            cbs[0].SelectedIndex = lBProfiles.SelectedIndex;
        }

        private void assignToController2ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            cbs[1].SelectedIndex = lBProfiles.SelectedIndex;
        }

        private void assignToController3ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            cbs[2].SelectedIndex = lBProfiles.SelectedIndex;
        }

        private void assignToController4ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            cbs[3].SelectedIndex = lBProfiles.SelectedIndex;
        }

        private void tsBNewProfile_Click(object sender, EventArgs e) //Also used for context menu
        {
            ShowOptions(4, "");
        }

        private void tsBNEditProfile_Click(object sender, EventArgs e)
        {
            if (lBProfiles.SelectedIndex >= 0)
                ShowOptions(4, lBProfiles.SelectedItem.ToString());
        }

        private void tsBDeleteProfle_Click(object sender, EventArgs e)
        {
            if (lBProfiles.SelectedIndex >= 0)
            {
                string filename = lBProfiles.SelectedItem.ToString();
                if (MessageBox.Show(Properties.Resources.ProfileCannotRestore.Replace("*Profile name*", "\"" + filename + "\""),
                    Properties.Resources.DeleteProfile,
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                {
                    File.Delete(appdatapath + @"\Profiles\" + filename + ".xml");
                    RefreshProfiles();
                }
            }
        }

        private void tSBDupProfile_Click(object sender, EventArgs e)
        {
            string filename = "";
            if (lBProfiles.SelectedIndex >= 0)
            {
                filename = lBProfiles.SelectedItem.ToString();
                DupBox MTB = new DupBox(filename, this);
                MTB.TopLevel = false;
                MTB.Dock = DockStyle.Top;
                MTB.Visible = true;
                MTB.FormBorderStyle = FormBorderStyle.None;
                tabProfiles.Controls.Add(MTB);
                lBProfiles.SendToBack();
                toolStrip1.SendToBack();
                toolStrip1.Enabled = false;
                lBProfiles.Enabled = false;
                MTB.FormClosed += delegate { toolStrip1.Enabled = true; lBProfiles.Enabled = true; };
            }
        }

        private void tSBImportProfile_Click(object sender, EventArgs e)
        {
            if (appdatapath == Directory.GetParent(Assembly.GetExecutingAssembly().Location).FullName)
                openProfiles.InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData) + "\\DS4Tool" + @"\Profiles\";
            else
                openProfiles.InitialDirectory = Directory.GetParent(Assembly.GetExecutingAssembly().Location).FullName + @"\Profiles\";

            if (openProfiles.ShowDialog() == DialogResult.OK)
            {
                string[] files = openProfiles.FileNames;
                for (int i = 0, arlen = files.Length; i < arlen; i++)
                    File.Copy(openProfiles.FileNames[i], appdatapath + "\\Profiles\\" + Path.GetFileName(files[i]), true);

                RefreshProfiles();
            }
        }

        private void tSBExportProfile_Click(object sender, EventArgs e)
        {
            if (lBProfiles.SelectedIndex >= 0)
            {
                Stream stream;
                Stream profile = new StreamReader(appdatapath + "\\Profiles\\" + lBProfiles.SelectedItem.ToString() + ".xml").BaseStream;                
                if (saveProfiles.ShowDialog() == DialogResult.OK)
                {
                    if ((stream = saveProfiles.OpenFile()) != null)
                    {
                        profile.CopyTo(stream);
                        profile.Close();
                        stream.Close();
                    }
                }
            }
        }

        private void ShowOptions(int devID, string profile)
        {
            Show();

            lBProfiles.Visible = false;

            WindowState = FormWindowState.Normal;
            toolStrip1.Enabled = false;
            tSOptions.Visible = true;
            toolStrip1.Visible = false;
            if (profile != "")
                tSTBProfile.Text = profile;
            else
                tSTBProfile.Text = "<" + Properties.Resources.TypeProfileName + ">";

            lBProfiles.SendToBack();
            toolStrip1.SendToBack();
            tSOptions.SendToBack();
            opt.BringToFront();
            oldsize = Size;
            {
                if (Size.Height < (int)(90 * dpiy) + Options.mSize.Height)
                    Size = new Size(Size.Width, (int)(90 * dpiy) + Options.mSize.Height);

                if (Size.Width < (int)(20 * dpix) + Options.mSize.Width)
                    Size = new Size((int)(20 * dpix) + Options.mSize.Width, Size.Height);
            }

            opt.Reload(devID, profile);
            opt.inputtimer.Start();
            opt.Visible = true;
            tabMain.SelectedIndex = 1;
        }

        public void OptionsClosed()
        {
            RefreshProfiles();

            if (!lbNoControllers.Visible)
                tabMain.SelectedIndex = 0;

            Size = oldsize;
            oldsize = new Size(0, 0);
            tSBKeepSize.Text = Properties.Resources.KeepThisSize;
            tSBKeepSize.Image = Properties.Resources.size;
            tSBKeepSize.Enabled = true;
            tSOptions.Visible = false;
            toolStrip1.Visible = true;
            toolStrip1.Enabled = true;
            lbLastMessage.ForeColor = SystemColors.GrayText;
            int lvDebugItemCount = lvDebug.Items.Count;
            if (lvDebugItemCount > 0)
            {
                lbLastMessage.Text = lvDebug.Items[lvDebugItemCount - 1].SubItems[1].Text;
            }

            opt.inputtimer.Stop();
            opt.sixaxisTimer.Stop();

            lBProfiles.Visible = true;
        }

        private void editButtons_Click(object sender, EventArgs e)
        {
            Button bn = (Button)sender;
            //int i = Int32.Parse(bn.Tag.ToString());
            int i = Convert.ToInt32(bn.Tag);
            string profileText = cbs[i].Text;
            if (profileText != "(" + Properties.Resources.NoProfileLoaded + ")")
                ShowOptions(i, profileText);
            else
                ShowOptions(i, "");
        }

        private void editMenu_Click(object sender, EventArgs e)
        {
            mAllowVisible = true;
            this.Show();
            WindowState = FormWindowState.Normal;
            ToolStripMenuItem em = (ToolStripMenuItem)sender;
            int i = Convert.ToInt32(em.Tag);
            if (em.Text == Properties.Resources.ContextNew.Replace("*number*", (i + 1).ToString()))
                ShowOptions(i, "");
            else
            {
                for (int t = 0, itemCount = em.DropDownItems.Count - 2; t < itemCount; t++)
                {
                    if (((ToolStripMenuItem)em.DropDownItems[t]).Checked)
                        ShowOptions(i, ((ToolStripMenuItem)em.DropDownItems[t]).Text);
                }
            }
        }

        private void lnkControllers_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            Process.Start("control", "joy.cpl");
        }

        private void hideDS4CheckBox_CheckedChanged(object sender, EventArgs e)
        {
            // Prevent the Game Controllers window from throwing an error when controllers are un/hidden
            Process[] rundll64 = Process.GetProcessesByName("rundll64");
            foreach (Process rundll64Instance in rundll64)
            {
                foreach (ProcessModule module in rundll64Instance.Modules)
                {
                    if (module.FileName.Contains("joy.cpl"))
                        module.Dispose();
                }
            }

            bool exclusiveMode = hideDS4CheckBox.Checked;
            UseExclusiveMode = exclusiveMode;
            if (Environment.OSVersion.Version.Major >= 10 && Environment.OSVersion.Version.Build < 10586)
                btnConnectDS4Win10.Visible = exclusiveMode;

            btnStartStop_Clicked(false);
            btnStartStop_Clicked(false);
            Save();
        }

        private void startMinimizedCheckBox_CheckedChanged(object sender, EventArgs e)
        {
            StartMinimized = startMinimizedCheckBox.Checked;
            Save();
        }

        private void lvDebug_ItemActivate(object sender, EventArgs e)
        {
            MessageBox.Show(((ListView)sender).FocusedItem.SubItems[1].Text, "Log");
        }

        private void Profile_Changed(object sender, EventArgs e) //cbs[i] changed
        {
            ComboBox cb = (ComboBox)sender;
            int tdevice = Convert.ToInt32(cb.Tag);
            if (cb.Items[cb.Items.Count - 1].ToString() == "+" + Properties.Resources.PlusNewProfile)
            {
                if (cb.SelectedIndex < cb.Items.Count - 1)
                {
                    for (int i = 0, arlen = shortcuts[tdevice].DropDownItems.Count; i < arlen; i++)
                    {
                        if (!(shortcuts[tdevice].DropDownItems[i] is ToolStripSeparator))
                            ((ToolStripMenuItem)shortcuts[tdevice].DropDownItems[i]).Checked = false;
                    }

                    ((ToolStripMenuItem)shortcuts[tdevice].DropDownItems[cb.SelectedIndex]).Checked = true;
                    LogDebug(DateTime.Now, Properties.Resources.UsingProfile.Replace("*number*", (tdevice + 1).ToString()).Replace("*Profile name*", cb.Text), false);
                    shortcuts[tdevice].Text = Properties.Resources.ContextEdit.Replace("*number*", (tdevice + 1).ToString());
                    ProfilePath[tdevice] = cb.Items[cb.SelectedIndex].ToString();
                    Save();
                    LoadProfile(tdevice, true, Program.rootHub);
                    if (UseCustomLed[tdevice])
                        lights[tdevice].BackColor = CustomColor[tdevice].ToColorA;
                    else
                        lights[tdevice].BackColor = MainColor[tdevice].ToColorA;
                    trayBatts[tdevice].Checked = (TrayBatteryIndex == tdevice);
                }
                else if (cb.SelectedIndex == cb.Items.Count - 1 && cb.Items.Count > 1) //if +New Profile selected
                    ShowOptions(tdevice, "");

                if (cb.Text == "(" + Properties.Resources.NoProfileLoaded + ")")
                    ebns[tdevice].Text = Properties.Resources.New;
                else
                    ebns[tdevice].Text = Properties.Resources.EditProfile;
            }

            OnDeviceStatusChanged(this, tdevice); //to update profile name in notify icon
        }

        private void Profile_Changed_Menu(object sender, ToolStripItemClickedEventArgs e)
        {
            ToolStripMenuItem tS = (ToolStripMenuItem)sender;
            int tdevice = Convert.ToInt32(tS.Tag);
            if (!(e.ClickedItem is ToolStripSeparator))
            {
                if (e.ClickedItem != tS.DropDownItems[tS.DropDownItems.Count - 1]) //if +New Profile not selected 
                    cbs[tdevice].SelectedIndex = tS.DropDownItems.IndexOf(e.ClickedItem);
                else //if +New Profile selected
                    ShowOptions(tdevice, "");
            }
        }

        private void exitToolStripMenuItem_Click(object sender, EventArgs e)
        {
            contextclose = true;
            this.Close();
        }

        private void openToolStripMenuItem_Click(object sender, EventArgs e)
        {
            mAllowVisible = true;
            this.Show();
            Focus();
            WindowState = FormWindowState.Normal;
        }

        private void startToolStripMenuItem_Click(object sender, EventArgs e)
        {
            btnStartStop_Clicked();
        }

        private void notifyIcon1_MouseClick(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Middle)
            {
                contextclose = true;
                this.Close();
            }
        }

        private void notifyIcon1_BalloonTipClicked(object sender, EventArgs e)
        {
            this.Show();
            WindowState = FormWindowState.Normal;
        }

        private void llbHelp_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            Hotkeys hotkeysForm = new Hotkeys();
            hotkeysForm.Icon = this.Icon;
            hotkeysForm.Text = llbHelp.Text;
            hotkeysForm.ShowDialog();
        }

        private void StartWindowsCheckBox_CheckedChanged(object sender, EventArgs e)
        {
            bool isChecked = StartWindowsCheckBox.Checked;
            RegistryKey KeyLoc = Registry.CurrentUser.OpenSubKey("SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run", true);
            if (isChecked && !File.Exists(Environment.GetFolderPath(Environment.SpecialFolder.Startup) + "\\DS4Windows.lnk"))
            {
                appShortcutToStartup();
            }
            else if (!isChecked)
            {
                File.Delete(Environment.GetFolderPath(Environment.SpecialFolder.Startup) + "\\DS4Windows.lnk");
            }

            KeyLoc.DeleteValue("DS4Tool", false);

            if (isChecked)
            {
                runStartupPanel.Visible = true;
            }
            else
            {
                runStartupPanel.Visible = false;
                runStartTaskRadio.Checked = false;
                runStartProgRadio.Checked = true;
            }

            changeStartupRoutine();
        }

        private void appShortcutToStartup()
        {
            Type t = Type.GetTypeFromCLSID(new Guid("72C24DD5-D70A-438B-8A42-98424B88AFB8")); // Windows Script Host Shell Object
            dynamic shell = Activator.CreateInstance(t);
            try
            {
                var lnk = shell.CreateShortcut(Environment.GetFolderPath(Environment.SpecialFolder.Startup) + "\\DS4Windows.lnk");
                try
                {
                    string app = Assembly.GetExecutingAssembly().Location;
                    lnk.TargetPath = Assembly.GetExecutingAssembly().Location;

                    if (runStartProgRadio.Checked)
                    {
                        lnk.Arguments = "-m";
                    }
                    else if (runStartTaskRadio.Checked)
                    {
                        lnk.Arguments = "-runtask";
                    }

                    //lnk.TargetPath = Assembly.GetExecutingAssembly().Location;
                    //lnk.Arguments = "-m";
                    lnk.IconLocation = app.Replace('\\', '/');
                    lnk.Save();
                }
                finally
                {
                    Marshal.FinalReleaseComObject(lnk);
                }
            }
            finally
            {
                Marshal.FinalReleaseComObject(shell);
            }
        }

        private void tabMain_SelectedIndexChanged(object sender, EventArgs e)
        {
            TabPage currentTab = tabMain.SelectedTab;
            lbLastMessage.Visible = currentTab != tabLog;
            if (currentTab == tabLog)
                chData.AutoResize(ColumnHeaderAutoResizeStyle.HeaderSize);

            if (currentTab == tabSettings)
            {
                lbLastMessage.ForeColor = SystemColors.GrayText;
                lbLastMessage.Text = Properties.Resources.HoverOverItems;
            }
            else if (lvDebug.Items.Count > 0)
                lbLastMessage.Text = lvDebug.Items[lvDebug.Items.Count - 1].SubItems[1].Text;
            else
                lbLastMessage.Text = "";
        }

        private void Items_MouseHover(object sender, EventArgs e)
        {
            string hoverText = Properties.Resources.HoverOverItems;
            string temp = "";
            if (hoverTextDict.TryGetValue((Control)sender, out temp))
            {
                hoverText = temp;
            }

            lbLastMessage.Text = hoverText;
            if (hoverText != Properties.Resources.HoverOverItems)
                lbLastMessage.ForeColor = Color.Black;
            else
                lbLastMessage.ForeColor = SystemColors.GrayText;
        }

        private void lBProfiles_MouseDown(object sender, MouseEventArgs e)
        {
            lBProfiles.SelectedIndex = lBProfiles.IndexFromPoint(e.X, e.Y);
            if (e.Button == MouseButtons.Right)
            {
                if (lBProfiles.SelectedIndex < 0)
                {
                    cMProfile.ShowImageMargin = false;
                    assignToController1ToolStripMenuItem.Visible = false;
                    assignToController2ToolStripMenuItem.Visible = false;
                    assignToController3ToolStripMenuItem.Visible = false;
                    assignToController4ToolStripMenuItem.Visible = false;
                    deleteToolStripMenuItem.Visible = false;
                    editToolStripMenuItem.Visible = false;
                    duplicateToolStripMenuItem.Visible = false;
                    exportToolStripMenuItem.Visible = false;
                }
                else
                {
                    cMProfile.ShowImageMargin = true;
                    assignToController1ToolStripMenuItem.Visible = true;
                    assignToController2ToolStripMenuItem.Visible = true;
                    assignToController3ToolStripMenuItem.Visible = true;
                    assignToController4ToolStripMenuItem.Visible = true;
                    ToolStripMenuItem[] assigns = { assignToController1ToolStripMenuItem, 
                                                      assignToController2ToolStripMenuItem,
                                                      assignToController3ToolStripMenuItem, 
                                                      assignToController4ToolStripMenuItem };

                    for (int i = 0; i < 4; i++)
                    {
                        if (lBProfiles.SelectedIndex == cbs[i].SelectedIndex)
                            assigns[i].Checked = true;
                        else
                            assigns[i].Checked = false;
                    }

                    deleteToolStripMenuItem.Visible = true;
                    editToolStripMenuItem.Visible = true;
                    duplicateToolStripMenuItem.Visible = true;
                    exportToolStripMenuItem.Visible = true;
                }
            }
        }

        private void ScpForm_DragDrop(object sender, DragEventArgs e)
        {
            bool therewasanxml = false;
            string[] files = (string[])e.Data.GetData(DataFormats.FileDrop, false);
            for (int i = 0; i < files.Length; i++)
            {
                if (files[i].EndsWith(".xml"))
                {
                    File.Copy(files[i], appdatapath + "\\Profiles\\" + Path.GetFileName(files[i]), true);
                    therewasanxml = true;
                }
            }

            if (therewasanxml)
                RefreshProfiles();
        }

        private void ScpForm_DragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
                e.Effect = DragDropEffects.Copy; // Okay
            else
                e.Effect = DragDropEffects.None; // Unknown data, ignore it
        }

        private void tBProfile_TextChanged(object sender, EventArgs e)
        {
            if (tSTBProfile.Text != null && tSTBProfile.Text != "" &&
                !tSTBProfile.Text.Contains("\\") && !tSTBProfile.Text.Contains("/") &&
                !tSTBProfile.Text.Contains(":") && !tSTBProfile.Text.Contains("*") &&
                !tSTBProfile.Text.Contains("?") && !tSTBProfile.Text.Contains("\"") &&
                !tSTBProfile.Text.Contains("<") && !tSTBProfile.Text.Contains(">") &&
                !tSTBProfile.Text.Contains("|"))
                tSTBProfile.ForeColor = SystemColors.WindowText;
            else
                tSTBProfile.ForeColor = SystemColors.GrayText;
        }

        private void tBProfile_Enter(object sender, EventArgs e)
        {
            if (tSTBProfile.Text == "<" + Properties.Resources.TypeProfileName + ">")
                tSTBProfile.Text = "";
        }

        private void tBProfile_Leave(object sender, EventArgs e)
        {
            if (tSTBProfile.Text == "")
                tSTBProfile.Text = "<" + Properties.Resources.TypeProfileName + ">";
        }

        private void tSBCancel_Click(object sender, EventArgs e)
        {
            if (opt.Visible)
                opt.Close();
        }

        private void tSBSaveProfile_Click(object sender, EventArgs e)
        {
            if (opt.Visible)
            {
                opt.saving = true;
                opt.Set();

                if (tSTBProfile.Text != null && tSTBProfile.Text != "" &&
                    !tSTBProfile.Text.Contains("\\") && !tSTBProfile.Text.Contains("/") &&
                    !tSTBProfile.Text.Contains(":") && !tSTBProfile.Text.Contains("*") &&
                    !tSTBProfile.Text.Contains("?") && !tSTBProfile.Text.Contains("\"") &&
                    !tSTBProfile.Text.Contains("<") && !tSTBProfile.Text.Contains(">") &&
                    !tSTBProfile.Text.Contains("|"))
                {
                    File.Delete(appdatapath + @"\Profiles\" + opt.filename + ".xml");
                    ProfilePath[opt.device] = tSTBProfile.Text;
                    SaveProfile(opt.device, tSTBProfile.Text);
                    cacheProfileCustomsFlags(opt.device);
                    Save();
                    opt.Close();
                }
                else
                    MessageBox.Show(Properties.Resources.ValidName, Properties.Resources.NotValid, MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
            }
        }

        private void tSBKeepSize_Click(object sender, EventArgs e)
        {
            oldsize = Size;
            tSBKeepSize.Text = Properties.Resources.WillKeep;
            tSBKeepSize.Image = Properties.Resources._checked;
            tSBKeepSize.Enabled = false;
        }

        private void cBUpdate_CheckedChanged(object sender, EventArgs e)
        {
            if (!cBUpdate.Checked)
            {
                nUDUpdateTime.Value = 0;
                pNUpdate.Enabled = false;
            }
            else
            {
                nUDUpdateTime.Value = 1;
                cBUpdateTime.SelectedIndex = 0;
                pNUpdate.Enabled = true;
            }
        }

        private void nUDUpdateTime_ValueChanged(object sender, EventArgs e)
        {
            int currentIndex = cBUpdateTime.SelectedIndex;
            if (currentIndex == 0)
                CheckWhen = (int)nUDUpdateTime.Value;
            else if (currentIndex == 1)
                CheckWhen = (int)nUDUpdateTime.Value * 24;

            if (nUDUpdateTime.Value < 1)
                cBUpdate.Checked = false;

            if (nUDUpdateTime.Value == 1)
            {
                int index = currentIndex;
                cBUpdateTime.Items.Clear();
                cBUpdateTime.Items.Add(Properties.Resources.Hour);
                cBUpdateTime.Items.Add(Properties.Resources.Day);
                cBUpdateTime.SelectedIndex = index;
            }
            else if (cBUpdateTime.Items[0].ToString() == Properties.Resources.Hour)
            {
                int index = currentIndex;
                cBUpdateTime.Items.Clear();
                cBUpdateTime.Items.Add(Properties.Resources.Hours);
                cBUpdateTime.Items.Add(Properties.Resources.Days);
                cBUpdateTime.SelectedIndex = index;
            }
        }

        private void cBUpdateTime_SelectedIndexChanged(object sender, EventArgs e)
        {
            int index = cBUpdateTime.SelectedIndex;
            if (index == 0)
                CheckWhen = (int)nUDUpdateTime.Value;
            else if (index == 1)
                CheckWhen = (int)nUDUpdateTime.Value * 24;
        }

        private void lLBUpdate_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            // Sorry other devs, gonna have to find your own server
            Uri url = new Uri("http://23.239.26.40/ds4windows/files/builds/newest.txt");
            WebClient wct = new WebClient();
            wct.DownloadFileAsync(url, appdatapath + "\\version.txt");
            wct.DownloadFileCompleted += wct_DownloadFileCompleted;
        }

        private void cBDisconnectBT_CheckedChanged(object sender, EventArgs e)
        {
            DCBTatStop = cBDisconnectBT.Checked;
        }

        void wct_DownloadFileCompleted(object sender, System.ComponentModel.AsyncCompletedEventArgs e)
        {
            LastChecked = DateTime.Now;
            FileVersionInfo fvi = FileVersionInfo.GetVersionInfo(Assembly.GetExecutingAssembly().Location);
            string version2 = fvi.FileVersion;
            string newversion2 = File.ReadAllText(appdatapath + "\\version.txt").Trim();
            if (version2.Replace(',', '.').CompareTo(newversion2) == -1)//CompareVersions();
            {
                if (MessageBox.Show(Properties.Resources.DownloadVersion.Replace("*number*", newversion2),
                    Properties.Resources.DS4Update, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                {
                    if (!File.Exists(exepath + "\\DS4Updater.exe") || (File.Exists(exepath + "\\DS4Updater.exe")
                         && (FileVersionInfo.GetVersionInfo(exepath + "\\DS4Updater.exe").FileVersion.CompareTo("1.1.0.0") == -1)))
                    {
                        Uri url2 = new Uri("http://ds4windows.com/Files/DS4Updater.exe");
                        WebClient wc2 = new WebClient();
                        if (appdatapath == exepath)
                            wc2.DownloadFile(url2, exepath + "\\DS4Updater.exe");
                        else
                        {
                            MessageBox.Show(Properties.Resources.PleaseDownloadUpdater);
                            Process.Start("http://ds4windows.com/Files/DS4Updater.exe");
                        }
                    }

                    Process p = new Process();
                    p.StartInfo.FileName = exepath + "\\DS4Updater.exe";
                    if (!cBDownloadLangauge.Checked)
                        p.StartInfo.Arguments = "-skipLang";

                    if (AdminNeeded())
                        p.StartInfo.Verb = "runas";

                    try { p.Start(); Close(); }
                    catch { }
                }
                else
                    File.Delete(appdatapath + "\\version.txt");
            }
            else
            {
                File.Delete(appdatapath + "\\version.txt");
                MessageBox.Show(Properties.Resources.UpToDate, "DS4Windows Updater");
            }
        }

        private void linkProfiles_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            Process.Start(appdatapath + "\\Profiles");
        }

        private void linkUninstall_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            if (File.Exists(appdatapath + "\\Virtual Bus Driver\\ScpDriver.exe"))
            {
                try { Process.Start(appdatapath + "\\Virtual Bus Driver\\ScpDriver.exe"); }
                catch { Process.Start(appdatapath + "\\Virtual Bus Driver"); }
            }
        }

        private void cBoxNotifications_SelectedIndexChanged(object sender, EventArgs e)
        {
            Notifications = cBoxNotifications.SelectedIndex;
        }

        private void lLSetup_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            Process p = new Process();
            p.StartInfo.FileName = Assembly.GetExecutingAssembly().Location;
            p.StartInfo.Arguments = "-driverinstall";
            p.StartInfo.Verb = "runas";
            try { p.Start(); }
            catch { }
            //WelcomeDialog wd = new WelcomeDialog();
            //wd.ShowDialog();
            tabSettings.Text = originalsettingstext;
            linkSetup.LinkColor = Color.Blue;
        }

        bool tempBool = false;
        protected void ScpForm_Closing(object sender, FormClosingEventArgs e)
        {
            if (opt.Visible)
            {
                opt.Close();
                e.Cancel = true;
                return;
            }

            bool closeMini = tempBool = cBCloseMini.Checked;
            bool userClosing = e.CloseReason == CloseReason.UserClosing;
            DS4Device d = null;
            bool nocontrollers = tempBool = true;
            //in case user accidentally clicks on the close button whilst "Close Minimizes" checkbox is unchecked
            if (userClosing && !closeMini && !contextclose)
            {
                for (int i = 0, PadsLen = Pads.Length; tempBool && i < PadsLen; i++)
                {
                    d = Program.rootHub.DS4Controllers[i];
                    tempBool = (d != null) ? false : tempBool;
                }

                nocontrollers = tempBool;
                if (!nocontrollers)
                {
                    if (MessageBox.Show(Properties.Resources.CloseConfirm, Properties.Resources.Confirm,
                        MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.No)
                    {
                        e.Cancel = true;
                        return;
                    }
                }
            }
            else if (userClosing && closeMini && !contextclose)
            {
                this.WindowState = FormWindowState.Minimized;
                e.Cancel = true;
                return;
            }

            if (systemShutdown)
            // Reset the variable because the user might cancel the 
            // shutdown.
            {
                systemShutdown = false;
                DS4LightBar.shuttingdown = true;
            }

            if (oldsize == new Size(0, 0))
            {
                FormWidth = this.Width;
                FormHeight = this.Height;
            }
            else
            {
                FormWidth = oldsize.Width;
                FormHeight = oldsize.Height;
            }

            if (!string.IsNullOrEmpty(appdatapath))
            {
                Save();
                blankControllerTab();
                Program.rootHub.Stop();
            }

            // Make sure to stop event generation routines. Should fix odd crashes on shutdown
            Application.Exit();
        }

        private void cBSwipeProfiles_CheckedChanged(object sender, EventArgs e)
        {
            bool swipe = false;
            SwipeProfiles = swipe = cBSwipeProfiles.Checked;
            bool timerEnabled = hotkeysTimer.Enabled;
            if (swipe && !timerEnabled)
            {
                hotkeysTimer.Start();
            }
            else if (!swipe && timerEnabled)
            {
                hotkeysTimer.Stop();
            }
        }

        private void cBQuickCharge_CheckedChanged(object sender, EventArgs e)
        {
            QuickCharge = cBQuickCharge.Checked;
        }

        private void lbLastMessage_MouseHover(object sender, EventArgs e)
        {
            toolTip1.Show(lbLastMessage.Text, lbLastMessage, -3, -3);
        }

        private void pnlButton_MouseLeave(object sender, EventArgs e)
        {
            toolTip1.Hide(lbLastMessage);
        }

        private void pnlXIPorts_MouseEnter(object sender, EventArgs e)
        {
            //oldxiport = (int)Math.Round(nUDXIPorts.Value,0);
        }

        int oldxiport;
        private void pnlXIPorts_MouseLeave(object sender, EventArgs e)
        {

        }

        private void nUDXIPorts_ValueChanged(object sender, EventArgs e)
        {
            lbLastXIPort.Text = "- " + ((int)Math.Round(nUDXIPorts.Value, 0) + 3);
        }

        private void nUDXIPorts_Leave(object sender, EventArgs e)
        {
            if (oldxiport != (int)Math.Round(nUDXIPorts.Value, 0))
            {
                oldxiport = (int)Math.Round(nUDXIPorts.Value, 0);
                FirstXinputPort = oldxiport;
                Program.rootHub.x360Bus.FirstController = oldxiport;
                btnStartStop_Click(sender, e);
                btnStartStop_Click(sender, e);
            }
        }

        private void nUDXIPorts_Enter(object sender, EventArgs e)
        {
            oldxiport = (int)Math.Round(nUDXIPorts.Value, 0);
        }

        private void cBCloseMini_CheckedChanged(object sender, EventArgs e)
        {
            CloseMini = cBCloseMini.Checked;
        }

        private void Pads_MouseHover(object sender, EventArgs e)
        {
            Label lb = (Label)sender;
            int i = Convert.ToInt32(lb.Tag);
            DS4Device d = Program.rootHub.DS4Controllers[i];
            if (d != null && d.ConnectionType == ConnectionType.BT)
            {
                double latency = d.Latency;
                toolTip1.Hide(Pads[i]);
                toolTip1.Show(Properties.Resources.InputDelay.Replace("*number*", latency.ToString()), lb, lb.Size.Width, 0);
            }
        }

        private void Pads_MouseLeave(object sender, EventArgs e)
        {
            toolTip1.Hide((Label)sender);
        }

        Process bat;
        private void btnConnectDS4Win10_Click(object sender, EventArgs e)
        {
            if (!runningBat)
            {
                StreamWriter w = new StreamWriter(exepath + "\\ConnectDS4.bat");
                w.WriteLine("@echo off"); // Turn off echo
                w.WriteLine("taskkill /IM explorer.exe /f");
                w.WriteLine("echo Connect your DS4 controller"); //
                w.WriteLine("pause");
                w.WriteLine("start explorer.exe");
                w.Close();
                runningBat = true;
                bat = Process.Start(exepath + "\\ConnectDS4.bat");
            }
        }

        int currentCustomLed;
        private void EditCustomLed(object sender, EventArgs e)
        {
            Button btn = (Button)sender;
            currentCustomLed = Convert.ToInt32(btn.Tag);
            bool customLedChecked = UseCustomLed[currentCustomLed];
            useCustomColorToolStripMenuItem.Checked = customLedChecked;
            useProfileColorToolStripMenuItem.Checked = !customLedChecked;
            cMCustomLed.Show(btn, new Point(0, btn.Height));
        }

        private void useProfileColorToolStripMenuItem_Click(object sender, EventArgs e)
        {
            UseCustomLed[currentCustomLed] = false;
            lights[currentCustomLed].BackColor = MainColor[currentCustomLed].ToColorA;
            Global.Save();
        }
    
        private void useCustomColorToolStripMenuItem_Click(object sender, EventArgs e)
        {
            advColorDialog.Color = CustomColor[currentCustomLed].ToColor;
            if (advColorDialog.ShowDialog() == DialogResult.OK)
            {
                lights[currentCustomLed].BackColor = new DS4Color(advColorDialog.Color).ToColorA;
                CustomColor[currentCustomLed] = new DS4Color(advColorDialog.Color);
                UseCustomLed[currentCustomLed] = true;
                Global.Save();
            }

            DS4LightBar.forcedFlash[currentCustomLed] = 0;
            DS4LightBar.forcelight[currentCustomLed] = false;
        }

        private void cBUseWhiteIcon_CheckedChanged(object sender, EventArgs e)
        {
            UseWhiteIcon = cBUseWhiteIcon.Checked;
            notifyIcon1.Icon = UseWhiteIcon ? Properties.Resources.DS4W___White : Properties.Resources.DS4W;
        }

        private void advColorDialog_OnUpdateColor(object sender, EventArgs e)
        {
            if (sender is Color)
            {
                Color color = (Color)sender;
                DS4Color dcolor = new DS4Color(color);
                Console.WriteLine(dcolor);
                DS4LightBar.forcedColor[currentCustomLed] = dcolor;
                DS4LightBar.forcedFlash[currentCustomLed] = 0;
                DS4LightBar.forcelight[currentCustomLed] = true;
            }
        }

        private void lBProfiles_SelectedIndexChanged(object sender, EventArgs e)
        {
            int index = lBProfiles.SelectedIndex;
            if (index >= 0)
            {
                tsBNewProfle.Enabled = true;
                tsBEditProfile.Enabled = true;
                tsBDeleteProfile.Enabled = true;
                tSBDupProfile.Enabled = true;
                tSBImportProfile.Enabled = true;
                tSBExportProfile.Enabled = true;
            }
            else
            {
                tsBNewProfle.Enabled = true;
                tsBEditProfile.Enabled = false;
                tsBDeleteProfile.Enabled = false;
                tSBDupProfile.Enabled = false;
                tSBImportProfile.Enabled = true;
                tSBExportProfile.Enabled = false;
            }
        }

        private void runStartProgRadio_Click(object sender, EventArgs e)
        {
            appShortcutToStartup();
            changeStartupRoutine();
        }

        private void runStartTaskRadio_Click(object sender, EventArgs e)
        {
            appShortcutToStartup();
            changeStartupRoutine();
        }

        private void changeStartupRoutine()
        {
            if (runStartTaskRadio.Checked)
            {
                WindowsIdentity identity = WindowsIdentity.GetCurrent();
                WindowsPrincipal principal = new WindowsPrincipal(identity);
                if (principal.IsInRole(WindowsBuiltInRole.Administrator))
                {
                    TaskService ts = new TaskService();
                    Task tasker = ts.FindTask("RunDS4Windows");
                    if (tasker != null)
                    {
                        ts.RootFolder.DeleteTask("RunDS4Windows");
                    }

                    TaskDefinition td = ts.NewTask();
                    td.Actions.Add(new ExecAction(@"%windir%\System32\cmd.exe",
                        "/c start \"RunDS4Windows\" \"" + Process.GetCurrentProcess().MainModule.FileName + "\" -m",
                        new FileInfo(Process.GetCurrentProcess().MainModule.FileName).DirectoryName));

                    td.Principal.RunLevel = TaskRunLevel.Highest;
                    ts.RootFolder.RegisterTaskDefinition("RunDS4Windows", td);
                }
            }
            else
            {
                WindowsIdentity identity = WindowsIdentity.GetCurrent();
                WindowsPrincipal principal = new WindowsPrincipal(identity);
                if (principal.IsInRole(WindowsBuiltInRole.Administrator))
                {
                    TaskService ts = new TaskService();
                    Task tasker = ts.FindTask("RunDS4Windows");
                    if (tasker != null)
                    {
                        ts.RootFolder.DeleteTask("RunDS4Windows");
                    }
                }
            }
        }

        private void cBDownloadLangauge_CheckedChanged(object sender, EventArgs e)
        {
            DownloadLang = cBDownloadLangauge.Checked;
        }

        private void rbtnTrayBatt_Click(object sender, EventArgs e)
        {
            var self = sender as RadioButton;
            for(var i = 0; i < trayBatts.Length; i++)
            {
                if(trayBatts[i] != self) { trayBatts[i].Checked = false; }
                else
                {
                    Global.TrayBatteryIndex = i;
                    Global.Save();
                }
            }
        }

        private void cBFlashWhenLate_CheckedChanged(object sender, EventArgs e)
        {
            FlashWhenLate = cBFlashWhenLate.Checked;
            nUDLatency.Enabled = cBFlashWhenLate.Checked;
            lbMsLatency.Enabled = cBFlashWhenLate.Checked;
        }

        private void nUDLatency_ValueChanged(object sender, EventArgs e)
        {
            FlashWhenLateAt = (int)Math.Round(nUDLatency.Value);
        }
    }

    public class ThemeUtil
    {
        [DllImport("UxTheme", CharSet = CharSet.Unicode, ExactSpelling = true)]
        private static extern int SetWindowTheme(IntPtr hWnd, String appName, String partList);

        public static void SetTheme(ListView lv)
        {
            try
            {
                SetWindowTheme(lv.Handle, "Explorer", null);
            }
            catch { }
        }

        public static void SetTheme(TreeView tv)
        {
            try
            {
                SetWindowTheme(tv.Handle, "Explorer", null);
            }
            catch { }
        }
    }
}
