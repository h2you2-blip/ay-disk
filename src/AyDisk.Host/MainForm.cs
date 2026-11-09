using System.Security.Cryptography;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;

namespace AyDisk.Host;

/// <summary>
/// "This Desk" window: shows ID + password, asks the user to Allow/Deny incoming sessions,
/// streams the screen and applies remote input while a session is active.
/// </summary>
public class MainForm : Form
{
    private readonly HostSettings _settings = HostSettings.Load();
    private readonly ScreenCapturer _capturer = new();

    private readonly TextBox _txtRelay = new() { Width = 320 };
    private readonly Label _lblId = new() { AutoSize = true, Font = new Font("Segoe UI", 22, FontStyle.Bold), Text = "--- --- ---" };
    private readonly Label _lblPwd = new() { AutoSize = true, Font = new Font("Consolas", 16, FontStyle.Bold) };
    private readonly Label _lblStatus = new() { AutoSize = true, ForeColor = Color.DimGray, Text = "Offline" };
    private readonly Button _btnConnect = new() { Text = "Go online", AutoSize = true };
    private readonly Button _btnNewPwd = new() { Text = "New password", AutoSize = true };
    private readonly Button _btnEnd = new() { Text = "End session", AutoSize = true, Enabled = false, BackColor = Color.MistyRose };
    private readonly NotifyIcon _tray = new() { Icon = SystemIcons.Shield, Text = "AY Disk Host", Visible = true };

    private HubConnection? _conn;
    private string _password = NewPassword();
    private string? _activeViewer;
    private CancellationTokenSource? _streamCts;

    public MainForm()
    {
        Text = "AY Disk - This Desk";
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(16);

        _txtRelay.Text = _settings.RelayUrl;
        _lblPwd.Text = _password;

        var layout = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false };
        layout.Controls.Add(new Label { Text = "Relay server URL:", AutoSize = true });
        layout.Controls.Add(_txtRelay);
        layout.Controls.Add(_btnConnect);
        layout.Controls.Add(new Label { Text = "Your ID:", AutoSize = true, Margin = new Padding(3, 12, 3, 0) });
        layout.Controls.Add(_lblId);
        layout.Controls.Add(new Label { Text = "Password:", AutoSize = true });
        var pwdRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        pwdRow.Controls.Add(_lblPwd);
        pwdRow.Controls.Add(_btnNewPwd);
        layout.Controls.Add(pwdRow);
        layout.Controls.Add(_lblStatus);
        layout.Controls.Add(_btnEnd);
        Controls.Add(layout);

        _btnConnect.Click += async (_, _) => await ConnectAsync();
        _btnNewPwd.Click += async (_, _) => await RegeneratePasswordAsync();
        _btnEnd.Click += async (_, _) => await EndSessionAsync(notifyRelay: true);
        _tray.DoubleClick += (_, _) => { Show(); WindowState = FormWindowState.Normal; Activate(); };
        FormClosing += async (_, _) => { _tray.Visible = false; StopStreaming(); if (_conn != null) await _conn.DisposeAsync(); };
    }

    // ------------------------------------------------ connection

    private async Task ConnectAsync()
    {
        try
        {
            _btnConnect.Enabled = false;
            _settings.RelayUrl = _txtRelay.Text.Trim().TrimEnd('/');
            _settings.Save();

            if (_conn != null) await _conn.DisposeAsync();

            _conn = new HubConnectionBuilder()
                .WithUrl(_settings.RelayUrl + "/relay")
                .AddMessagePackProtocol()
                .WithAutomaticReconnect()
                .Build();

            _conn.On<string, string>("SessionRequested", (viewerConn, viewerName) =>
                BeginInvoke(() => _ = OnSessionRequestedAsync(viewerConn, viewerName)));

            _conn.On<string, double, double, int, int, int>("Input", (type, x, y, btn, delta, key) =>
            {
                if (_activeViewer != null) InputInjector.Handle(type, x, y, btn, delta, key);
            });

            _conn.On("SessionEnded", () => BeginInvoke(() => _ = EndSessionAsync(notifyRelay: false)));

            _conn.Reconnecting += _ => { BeginInvoke(() => SetStatus("Reconnecting...")); return Task.CompletedTask; };
            _conn.Reconnected += async _ => await RegisterAsync();
            _conn.Closed += _ => { BeginInvoke(() => { SetStatus("Offline"); _btnConnect.Enabled = true; }); return Task.CompletedTask; };

            SetStatus("Connecting...");
            await _conn.StartAsync();
            await RegisterAsync();
        }
        catch (Exception ex)
        {
            SetStatus("Connection failed: " + ex.Message);
            _btnConnect.Enabled = true;
        }
    }

    private async Task RegisterAsync()
    {
        if (_conn == null) return;
        var id = await _conn.InvokeAsync<string>("RegisterHost", Environment.MachineName, _password, _settings.HostId);
        _settings.HostId = id;
        _settings.Save();
        BeginInvoke(() =>
        {
            _lblId.Text = $"{id[..3]} {id[3..6]} {id[6..]}";
            SetStatus("Online - ready for connections");
        });
    }

    private async Task RegeneratePasswordAsync()
    {
        _password = NewPassword();
        _lblPwd.Text = _password;
        if (_conn?.State == HubConnectionState.Connected) await RegisterAsync();
    }

    // ------------------------------------------------ session

    private async Task OnSessionRequestedAsync(string viewerConn, string viewerName)
    {
        if (_conn == null) return;
        if (_activeViewer != null)
        {
            await _conn.InvokeAsync("RespondToSession", viewerConn, false);
            return;
        }

        Show(); WindowState = FormWindowState.Normal; Activate();
        var answer = MessageBox.Show(this,
            $"\"{viewerName}\" wants to VIEW and CONTROL this computer.\n\nDo you allow this?",
            "AY Disk - Incoming connection", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);

        bool accepted = answer == DialogResult.Yes;
        await _conn.InvokeAsync("RespondToSession", viewerConn, accepted);
        if (!accepted) return;

        _activeViewer = viewerConn;
        _btnEnd.Enabled = true;
        SetStatus($"CONTROLLED by {viewerName}");
        _lblStatus.ForeColor = Color.Red;
        _tray.ShowBalloonTip(3000, "AY Disk", $"{viewerName} is now controlling this PC.", ToolTipIcon.Warning);

        _streamCts = new CancellationTokenSource();
        var token = _streamCts.Token;
        _ = Task.Run(() => StreamLoopAsync(token));
    }

    private async Task EndSessionAsync(bool notifyRelay)
    {
        StopStreaming();
        _activeViewer = null;
        _btnEnd.Enabled = false;
        _lblStatus.ForeColor = Color.DimGray;
        SetStatus("Online - session ended");
        if (notifyRelay && _conn?.State == HubConnectionState.Connected)
            await _conn.InvokeAsync("EndSession");
    }

    private void StopStreaming()
    {
        _streamCts?.Cancel();
        _streamCts = null;
    }

    private async Task StreamLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (_conn?.State == HubConnectionState.Connected)
                {
                    var (data, w, h) = _capturer.Capture();
                    // awaiting the invoke gives natural back-pressure on slow networks
                    await _conn.InvokeAsync("SendFrame", data, w, h, ct);
                }
                await Task.Delay(66, ct); // ~15 fps max
            }
            catch (OperationCanceledException) { break; }
            catch
            {
                // e.g. UAC / lock screen (secure desktop) cannot be captured - retry
                try { await Task.Delay(500, ct); } catch { break; }
            }
        }
    }

    // ------------------------------------------------ helpers

    private void SetStatus(string text) => _lblStatus.Text = text;

    private static string NewPassword()
    {
        const string chars = "abcdefghjkmnpqrstuvwxyz23456789";
        return new string(Enumerable.Range(0, 8).Select(_ => chars[RandomNumberGenerator.GetInt32(chars.Length)]).ToArray());
    }
}
