using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;

namespace AyDisk.Viewer;

/// <summary>Technician side: enter ID + password, see and control the remote screen.</summary>
public class ViewerForm : Form
{
    private static readonly string RelayFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AY Disk", "viewer-relay.txt");

    private readonly TextBox _txtRelay = new() { Width = 220, PlaceholderText = "http://relay:5000" };
    private readonly TextBox _txtId = new() { Width = 120, PlaceholderText = "Remote ID" };
    private readonly TextBox _txtPwd = new() { Width = 110, PlaceholderText = "Password", UseSystemPasswordChar = true };
    private readonly Button _btnConnect = new() { Text = "Connect", AutoSize = true };
    private readonly Button _btnDisconnect = new() { Text = "Disconnect", AutoSize = true, Enabled = false };
    private readonly Label _lblStatus = new() { AutoSize = true, Padding = new Padding(0, 6, 0, 0), Text = "Ready" };
    private readonly RemoteScreen _screen = new() { Dock = DockStyle.Fill };

    private HubConnection? _conn;
    private bool _inSession;
    private DateTime _lastMove = DateTime.MinValue;

    public ViewerForm()
    {
        Text = "AY Disk - Viewer";
        Width = 1280; Height = 800;
        KeyPreview = false;

        try { _txtRelay.Text = File.ReadAllText(RelayFile).Trim(); } catch { _txtRelay.Text = "http://localhost:5000"; }

        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 38, Padding = new Padding(6), WrapContents = false };
        bar.Controls.AddRange(new Control[] { _txtRelay, _txtId, _txtPwd, _btnConnect, _btnDisconnect, _lblStatus });
        Controls.Add(_screen);
        Controls.Add(bar);

        _btnConnect.Click += async (_, _) => await ConnectAsync();
        _btnDisconnect.Click += async (_, _) => await DisconnectAsync();
        _txtPwd.KeyDown += async (_, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; await ConnectAsync(); } };

        // ---- mouse ----
        _screen.MouseMove += (_, e) =>
        {
            if ((DateTime.UtcNow - _lastMove).TotalMilliseconds < 30) return; // throttle
            _lastMove = DateTime.UtcNow;
            if (_screen.TryMap(e.Location, out var x, out var y)) Send("move", x, y);
        };
        _screen.MouseDown += (_, e) =>
        {
            _screen.Focus();
            if (_screen.TryMap(e.Location, out var x, out var y)) Send("down", x, y, ButtonId(e.Button));
        };
        _screen.MouseUp += (_, e) =>
        {
            _screen.TryMap(e.Location, out var x, out var y);
            Send("up", Math.Clamp(x, 0, 1), Math.Clamp(y, 0, 1), ButtonId(e.Button));
        };
        _screen.MouseWheel += (_, e) => Send("wheel", 0, 0, delta: e.Delta);

        // ---- keyboard ----
        _screen.KeyDown += (_, e) => { e.Handled = e.SuppressKeyPress = true; Send("keydown", 0, 0, key: e.KeyValue); };
        _screen.KeyUp += (_, e) => { e.Handled = true; Send("keyup", 0, 0, key: e.KeyValue); };

        FormClosing += async (_, _) => { if (_conn != null) { await DisconnectAsync(); await _conn.DisposeAsync(); } };
    }

    private async Task ConnectAsync()
    {
        try
        {
            _btnConnect.Enabled = false;
            var relay = _txtRelay.Text.Trim().TrimEnd('/');
            Directory.CreateDirectory(Path.GetDirectoryName(RelayFile)!);
            File.WriteAllText(RelayFile, relay);

            if (_conn != null) await _conn.DisposeAsync();
            _conn = new HubConnectionBuilder()
                .WithUrl(relay + "/relay")
                .AddMessagePackProtocol()
                .Build();

            _conn.On<byte[], int, int>("Frame", (data, w, h) =>
            {
                Bitmap bmp;
                using (var ms = new MemoryStream(data))
                using (var img = Image.FromStream(ms))
                    bmp = new Bitmap(img);
                BeginInvoke(() =>
                {
                    var old = _screen.Image;
                    _screen.Image = bmp;
                    old?.Dispose();
                });
            });

            _conn.On("SessionAccepted", () => BeginInvoke(() =>
            {
                _inSession = true;
                _btnDisconnect.Enabled = true;
                SetStatus("Connected - you are in control");
                _screen.Focus();
            }));
            _conn.On<string>("SessionRejected", reason => BeginInvoke(() => EndUi(reason)));
            _conn.On("SessionEnded", () => BeginInvoke(() => EndUi("Session ended by remote side")));
            _conn.Closed += _ => { BeginInvoke(() => EndUi("Disconnected from relay")); return Task.CompletedTask; };

            SetStatus("Connecting to relay...");
            await _conn.StartAsync();

            var result = await _conn.InvokeAsync<string>("RequestSession",
                _txtId.Text.Replace(" ", ""), _txtPwd.Text, $"{Environment.UserName}@{Environment.MachineName}");

            if (result == "PENDING")
            {
                SetStatus("Waiting for remote user to accept...");
                _btnDisconnect.Enabled = true;
            }
            else
            {
                EndUi(result);
            }
        }
        catch (Exception ex)
        {
            EndUi("Error: " + ex.Message);
        }
    }

    private async Task DisconnectAsync()
    {
        try
        {
            if (_conn?.State == HubConnectionState.Connected)
            {
                await _conn.InvokeAsync("EndSession");
                await _conn.StopAsync();
            }
        }
        catch { /* ignore */ }
        EndUi("Disconnected");
    }

    private void Send(string type, double x, double y, int button = 0, int delta = 0, int key = 0)
    {
        if (!_inSession || _conn?.State != HubConnectionState.Connected) return;
        _ = _conn.SendAsync("SendInput", type, x, y, button, delta, key);
    }

    private void EndUi(string status)
    {
        _inSession = false;
        _btnConnect.Enabled = true;
        _btnDisconnect.Enabled = false;
        SetStatus(status);
    }

    private void SetStatus(string text) => _lblStatus.Text = text;

    private static int ButtonId(MouseButtons b) => b switch
    {
        MouseButtons.Right => 1,
        MouseButtons.Middle => 2,
        _ => 0
    };
}
