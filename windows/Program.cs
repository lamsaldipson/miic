using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using System.Collections.Generic;
using System.Net.Sockets;

static class Program
{
    [STAThread]
    static void Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        bool first;
        using (var mutex = new Mutex(true, @"Local\PhoneMicReceiver", out first)) {
            if (!first) {
                MessageBox.Show("PhoneMic is already running. Open it from the Windows notification area.", "PhoneMic");
                return;
            }
            using (var window = new ReceiverWindow()) Application.Run(window);
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

        var menu = new ContextMenuStrip();
        menu.Items.Add("Open PhoneMic", null, (sender, args) => OpenWindow());
        trayToggle = new ToolStripMenuItem("Turn on", null, (sender, args) => Toggle());
        menu.Items.Add(trayToggle);
        menu.Items.Add("Exit", null, (sender, args) => { quitting = true; Close(); });
        tray = new NotifyIcon { Icon = Icon, Text = "PhoneMic · Off", ContextMenuStrip = menu, Visible = true };
        tray.DoubleClick += (sender, args) => OpenWindow();
        toggle.Click += (sender, args) => Toggle();
        Shown += (sender, args) => {
            if (selected >= 0) TurnOn();
            else status.Text = "Choose an audio output and turn on.";
        };
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

    void OpenWindow()
    {
        Show();
        WindowState = FormWindowState.Normal;
        Activate();
    }

    void Toggle() { if (receiver == null) TurnOn(); else TurnOff(); }

    void TurnOn()
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
            OpenWindow();
            status.Text = "Could not turn on. " + error.Message;
            return;
        } catch (InvalidOperationException error) {
            OpenWindow();
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
