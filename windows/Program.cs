using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using System.Collections.Generic;
using System.Net.Sockets;
using Microsoft.Win32;

static class Program
{
    const string MutexName = @"Local\PhoneMicReceiver";
    const string OpenEvent = @"Local\PhoneMicReceiver.Open";
    const string StopEvent = @"Local\PhoneMicReceiver.Stop";
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    static void Signal(string name)
    {
        EventWaitHandle handle;
        if (EventWaitHandle.TryOpenExisting(name, out handle))
            using (handle) handle.Set();
    }

    internal static bool StartWithWindows
    {
        get {
            using (var key = Registry.CurrentUser.OpenSubKey(RunKey))
                return key != null && key.GetValue("PhoneMic") != null;
        }
        set {
            using (var key = Registry.CurrentUser.CreateSubKey(RunKey)) {
                if (value) key.SetValue("PhoneMic", "\"" + Application.ExecutablePath + "\" --tray");
                else key.DeleteValue("PhoneMic", false);
            }
        }
    }

    [STAThread]
    static int Main(string[] args)
    {
        if (args.Contains("--check-cable")) return Playback.CableAvailable() ? 0 : 1;
        bool stop = args.Contains("--shutdown");
        bool background = args.Contains("--tray");
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        bool first;
        using (var mutex = new Mutex(true, MutexName, out first)) {
            if (!first) {
                if (stop) {
                    Signal(StopEvent);
                    try { return mutex.WaitOne(5000) ? 0 : 1; }
                    catch (AbandonedMutexException) { return 0; }
                }
                if (!background) Signal(OpenEvent);
                return 0;
            }
            if (stop) return 0;
            using (var open = new EventWaitHandle(false, EventResetMode.AutoReset, OpenEvent))
            using (var shutdown = new EventWaitHandle(false, EventResetMode.AutoReset, StopEvent))
            using (var window = new ReceiverWindow())
            using (var context = new ApplicationContext()) {
                var handle = window.Handle;
                window.FormClosed += (sender, e) => context.ExitThread();
                var show = ThreadPool.RegisterWaitForSingleObject(open, (s, t) =>
                    window.BeginInvoke((Action)window.OpenWindow), null, Timeout.Infinite, false);
                var close = ThreadPool.RegisterWaitForSingleObject(shutdown, (s, t) =>
                    window.BeginInvoke((Action)window.ExitReceiver), null, Timeout.Infinite, true);
                try {
                    window.Initialize(background);
                    Application.Run(context);
                } finally {
                    show.Unregister(null);
                    close.Unregister(null);
                }
            }
            return 0;
        }
    }
}
sealed class ReceiverWindow : Form
{
    readonly TextBox name = new TextBox { MaxLength = 64, Dock = DockStyle.Fill };
    readonly ComboBox output = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
    readonly Label status = new Label { AutoSize = true, Dock = DockStyle.Fill, Padding = new Padding(0, 8, 0, 0) };
    readonly Button toggle = new Button { AutoSize = true, Text = "Turn on" };
    readonly NotifyIcon tray;
    readonly ToolStripMenuItem trayToggle;
    readonly string settings = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                                            "PhoneMic", "receiver.txt");
    readonly string identity = Guid.NewGuid().ToString("N");
    readonly int selectedOutput;
    Receiver receiver;
    bool quitting;

    public ReceiverWindow()
    {
        Text = "PhoneMic Receiver";
        using (var icon = typeof(ReceiverWindow).Assembly.GetManifestResourceStream("PhoneMic.ico"))
            Icon = new Icon(icon);
        ClientSize = new Size(365, 220);
        AutoScaleMode = AutoScaleMode.Font;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(18),
                                            ColumnCount = 1, RowCount = 6 };
        layout.Controls.Add(new Label { Text = "Receiver name", AutoSize = true });
        layout.Controls.Add(name);
        layout.Controls.Add(new Label { Text = "Windows audio output", AutoSize = true, Padding = new Padding(0, 8, 0, 0) });
        layout.Controls.Add(output);
        layout.Controls.Add(toggle);
        layout.Controls.Add(status);
        Controls.Add(layout);

        name.Text = Environment.MachineName;
        string savedOutput = null;
        try {
            if (File.Exists(settings)) {
                var saved = File.ReadAllLines(settings);
                if (saved.Length > 0 && !String.IsNullOrWhiteSpace(saved[0])) name.Text = saved[0];
                if (saved.Length > 1) savedOutput = saved[1];
            }
        } catch (IOException) { }
          catch (UnauthorizedAccessException) { }
        var devices = Playback.Devices().ToList();
        output.DataSource = devices;
        output.DisplayMember = "Value";
        output.ValueMember = "Key";
        var selected = devices.FindIndex(x => x.Value == savedOutput);
        if (selected < 0) selected = devices.FindIndex(x => x.Value.StartsWith("CABLE Input", StringComparison.OrdinalIgnoreCase));
        output.SelectedIndex = selected;
        selectedOutput = selected;

        var menu = new ContextMenuStrip();
        menu.Items.Add("Open PhoneMic", null, (sender, args) => OpenWindow());
        trayToggle = new ToolStripMenuItem("Turn on", null, (sender, args) => Toggle());
        menu.Items.Add(trayToggle);
        var startup = new ToolStripMenuItem("Start PhoneMic with Windows") { CheckOnClick = false };
        menu.Items.Add(startup);
        menu.Opening += (sender, args) => startup.Checked = Program.StartWithWindows;
        startup.Click += (sender, args) => {
            try { Program.StartWithWindows = !startup.Checked; }
            catch (Exception error) {
                if (!(error is UnauthorizedAccessException) && !(error is IOException)) throw;
                MessageBox.Show("Could not update Windows startup settings.", "PhoneMic");
            }
        };
        menu.Items.Add("Exit", null, (sender, args) => ExitReceiver());
        tray = new NotifyIcon { Icon = Icon, Text = "PhoneMic · Off", ContextMenuStrip = menu, Visible = true };
        tray.DoubleClick += (sender, args) => OpenWindow();
        toggle.Click += (sender, args) => Toggle();

        Resize += (sender, args) => { if (WindowState == FormWindowState.Minimized) Hide(); };
        FormClosing += (sender, args) => {
            if (!quitting && args.CloseReason == CloseReason.UserClosing) {
                args.Cancel = true;
                Hide();
            } else {
                TurnOff();
                tray.Visible = false;
            }
        };
        FormClosed += (sender, args) => { tray.ContextMenuStrip.Dispose(); tray.Dispose(); };
    }

    internal void Initialize(bool background)
    {
        if (!background) OpenWindow();
        if (selectedOutput >= 0) TurnOn(!background);
        else status.Text = "Choose an audio output and turn on.";
    }

    internal void ExitReceiver()
    {
        quitting = true;
        Close();
    }

    internal void OpenWindow()
    {
        Show();
        WindowState = FormWindowState.Normal;
        Activate();
    }

    void Toggle() { if (receiver == null) TurnOn(); else TurnOff(); }

    void TurnOn(bool showErrors = true)
    {
        var configuredName = name.Text.Trim();
        if (configuredName.Length == 0) {
            OpenWindow();
            status.Text = "Enter a receiver name.";
            return;
        }
        if (!(output.SelectedItem is KeyValuePair<int, string>)) {
            OpenWindow();
            status.Text = "Choose a Windows audio output.";
            return;
        }
        var selected = (KeyValuePair<int, string>)output.SelectedItem;
        try {
            receiver = new Receiver(configuredName, identity, selected.Key, error => {
                if (!IsDisposed && IsHandleCreated) {
                    try { BeginInvoke((Action)(() => { if (receiver != null) status.Text = error; })); }
                    catch (InvalidOperationException) { }
                }
            });
            UpdateState();
        } catch (SocketException error) {
            if (showErrors) OpenWindow();
            status.Text = "Could not turn on. " + error.Message;
            return;
        } catch (InvalidOperationException error) {
            if (showErrors) OpenWindow();
            status.Text = "Could not turn on. " + error.Message;
            return;
        }
        try {
            Directory.CreateDirectory(Path.GetDirectoryName(settings));
            File.WriteAllLines(settings, new[] { configuredName, selected.Value });
        } catch (IOException) { status.Text = "On · Windows could not save your settings."; }
          catch (UnauthorizedAccessException) { status.Text = "On · Windows could not save your settings."; }
    }

    void TurnOff()
    {
        if (receiver != null) {
            receiver.Dispose();
            receiver = null;
        }
        UpdateState();
    }

    void UpdateState()
    {
        bool on = receiver != null;
        name.Enabled = output.Enabled = !on;
        toggle.Text = trayToggle.Text = on ? "Turn off" : "Turn on";
        tray.Text = on ? "PhoneMic · On" : "PhoneMic · Off";
        status.Text = on ? "On · Available on your local network" : "Off";
    }
}
