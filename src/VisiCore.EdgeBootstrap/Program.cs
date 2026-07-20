using System.Net;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.ServiceProcess;
using System.Text.Json;

namespace VisiCore.EdgeBootstrap;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new EdgeNodeConfigurationForm());
    }
}

internal sealed class EdgeNodeConfigurationForm : Form
{
    private const string EdgeAgentServiceName = "VisiCore Edge Agent";
    private const string HostAgentServiceName = "VisiCore Edge Host Agent";
    private readonly TextBox controlPlane = new() { Dock = DockStyle.Fill, PlaceholderText = "https://visicore.example.com/" };
    private readonly TextBox enrollmentCode = new() { Dock = DockStyle.Fill, UseSystemPasswordChar = true };
    private readonly Button testConnection = new() { Text = "测试连接", AutoSize = true };
    private readonly Button pair = new() { Text = "确认配对并生效", AutoSize = true };
    private readonly Label nodeStatus = new() { AutoSize = true, ForeColor = SystemColors.GrayText };
    private readonly CheckBox enableHostAgent = new() { Text = "启用 Host Agent 验证服务", AutoSize = true };
    private readonly CheckBox allowExecution = new() { Text = "允许实际受控升级", AutoSize = true };
    private readonly TextBox publicKeyPath = new() { Dock = DockStyle.Fill, ReadOnly = true };
    private readonly Button selectPublicKey = new() { Text = "选择 PEM 文件", AutoSize = true };
    private readonly TextBox publicKeyId = new() { Dock = DockStyle.Fill, PlaceholderText = "release-key-2026" };
    private readonly TextBox allowedHosts = new() { Dock = DockStyle.Fill, Multiline = true, Height = 72, PlaceholderText = "github.com\r\nobjects.githubusercontent.com" };
    private readonly NumericUpDown maximumArtifactMegabytes = new() { Minimum = 1, Maximum = 8192, Value = 2048, Dock = DockStyle.Left, Width = 120 };
    private readonly NumericUpDown executionTimeoutSeconds = new() { Minimum = 30, Maximum = 3600, Value = 600, Dock = DockStyle.Left, Width = 120 };
    private readonly Button saveHost = new() { Text = "测试并保存 Host 设置", AutoSize = true };
    private readonly Label hostStatus = new() { AutoSize = true, ForeColor = SystemColors.GrayText };

    private static string AgentDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "VisiCore", "EdgeAgent");
    private static string HostDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "VisiCore", "EdgeHostAgent");
    private static string AgentConfigurationPath => Path.Combine(AgentDirectory, "edge-agent.json");
    private static string BootstrapPath => Path.Combine(AgentDirectory, "bootstrap.json");
    private static string HostConfigurationPath => Path.Combine(HostDirectory, "edge-host-agent.json");
    private static string ImportedPublicKeyPath => Path.Combine(HostDirectory, "release-public-key.pem");

    public EdgeNodeConfigurationForm()
    {
        Text = "VisiCore 边缘节点配置";
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        ClientSize = new Size(650, 460);

        var tabs = new TabControl { Dock = DockStyle.Fill };
        tabs.TabPages.Add(CreateConnectionPage());
        tabs.TabPages.Add(CreateHostPage());
        Controls.Add(tabs);

        testConnection.Click += async (_, _) => await TestConnectionAsync();
        pair.Click += async (_, _) => await PairAsync();
        selectPublicKey.Click += (_, _) => SelectPublicKey();
        saveHost.Click += async (_, _) => await SaveHostAsync();
        allowExecution.CheckedChanged += (_, _) => enableHostAgent.Checked = enableHostAgent.Checked || allowExecution.Checked;
        LoadExistingConfiguration();
        RefreshServiceStatus();
    }

    private TabPage CreateConnectionPage()
    {
        var page = new TabPage("连接与配对");
        var layout = CreateLayout();
        AddField(layout, "中心 HTTPS 地址", controlPlane);
        AddField(layout, "一次性配对凭证", enrollmentCode);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.RightToLeft };
        actions.Controls.Add(pair);
        actions.Controls.Add(testConnection);
        layout.Controls.Add(actions, 0, 4);
        layout.Controls.Add(new Label
        {
            Text = "测试仅验证 HTTPS、证书链和中心健康检查；确认配对后才会写入注册码、启动服务并等待节点登记。",
            AutoSize = true,
            ForeColor = SystemColors.GrayText,
            MaximumSize = new Size(580, 0),
            Margin = new Padding(0, 14, 0, 4)
        }, 0, 5);
        layout.Controls.Add(nodeStatus, 0, 6);
        page.Controls.Add(layout);
        return page;
    }

    private TabPage CreateHostPage()
    {
        var page = new TabPage("Host Agent 高级设置");
        var layout = CreateLayout();
        layout.Controls.Add(enableHostAgent, 0, 0);
        layout.Controls.Add(allowExecution, 0, 1);
        AddField(layout, "发行签名公钥", CreateFilePicker());
        AddField(layout, "公钥标识", publicKeyId);
        AddField(layout, "受信下载域名（每行一个）", allowedHosts);
        AddField(layout, "最大制品大小（MiB）", maximumArtifactMegabytes);
        AddField(layout, "执行超时（秒）", executionTimeoutSeconds);
        layout.Controls.Add(saveHost, 0, 9);
        layout.Controls.Add(new Label
        {
            Text = "PEM 仅从文件导入到受限目录，配置页不会显示其内容。默认只启用发行验证，实际升级需单独明确开启。",
            AutoSize = true,
            ForeColor = SystemColors.GrayText,
            MaximumSize = new Size(580, 0),
            Margin = new Padding(0, 10, 0, 4)
        }, 0, 10);
        layout.Controls.Add(hostStatus, 0, 11);
        page.Controls.Add(layout);
        return page;
    }

    private Control CreateFilePicker()
    {
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, AutoSize = true };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        panel.Controls.Add(publicKeyPath, 0, 0);
        panel.Controls.Add(selectPublicKey, 1, 0);
        return panel;
    }

    private static TableLayoutPanel CreateLayout() => new()
    {
        Dock = DockStyle.Fill,
        Padding = new Padding(18),
        ColumnCount = 1,
        AutoSize = true,
        AutoScroll = true
    };

    private static void AddField(TableLayoutPanel layout, string label, Control control)
    {
        layout.Controls.Add(new Label { Text = label, AutoSize = true, Margin = new Padding(0, 8, 0, 2) });
        layout.Controls.Add(control);
    }

    private async Task TestConnectionAsync()
    {
        if (!TryNormalizeControlPlane(out var uri, out var error))
        {
            nodeStatus.Text = error;
            return;
        }

        SetBusy(testConnection, true);
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
            using var response = await client.GetAsync(new Uri(uri, "healthz"));
            nodeStatus.Text = response.StatusCode == HttpStatusCode.OK
                ? "连接测试通过：中心 HTTPS 健康检查正常。"
                : $"连接测试失败：中心返回 HTTP {(int)response.StatusCode}。";
        }
        catch (HttpRequestException)
        {
            nodeStatus.Text = "连接测试失败：无法建立受信任的 HTTPS 连接。";
        }
        catch (TaskCanceledException)
        {
            nodeStatus.Text = "连接测试失败：中心健康检查超时。";
        }
        finally
        {
            SetBusy(testConnection, false);
        }
    }

    private async Task PairAsync()
    {
        if (!TryNormalizeControlPlane(out var uri, out var error) || string.IsNullOrWhiteSpace(enrollmentCode.Text))
        {
            nodeStatus.Text = string.IsNullOrEmpty(error) ? "请输入一次性配对凭证。" : error;
            return;
        }

        SetBusy(pair, true);
        try
        {
            Directory.CreateDirectory(AgentDirectory);
            WriteJsonAtomically(AgentConfigurationPath, new
            {
                EdgeAgent = new
                {
                    ControlPlaneBaseUri = uri.AbsoluteUri,
                    BootstrapFilePath = BootstrapPath
                }
            });
            WriteJsonAtomically(BootstrapPath, new { enrollmentCode = enrollmentCode.Text.Trim() });
            ProtectPath(AgentConfigurationPath, localServiceReadable: true);
            ProtectPath(BootstrapPath, localServiceReadable: true);

            RestartService(EdgeAgentServiceName);
            enrollmentCode.Text = string.Empty;
            nodeStatus.Text = "已应用配置，正在等待节点登记。";
            var enrolled = await WaitForEnrollmentAsync();
            nodeStatus.Text = enrolled
                ? "配对已生效：节点已登记，注册码已由 Agent 清除。"
                : "配置已保存，但尚未完成登记。请检查注册码和中心状态后重试或取消。";
        }
        catch (IOException)
        {
            nodeStatus.Text = "配置保存失败：状态目录不可写。";
        }
        catch (UnauthorizedAccessException)
        {
            nodeStatus.Text = "配置保存失败：需要管理员权限。";
        }
        catch (InvalidOperationException)
        {
            nodeStatus.Text = "配置已保存，但 Edge Agent 服务无法启动。";
        }
        finally
        {
            SetBusy(pair, false);
            RefreshServiceStatus();
        }
    }

    private async Task SaveHostAsync()
    {
        await Task.Yield();
        if (!TryBuildHostConfiguration(out var configuration, out var publicKeyPem, out var error))
        {
            hostStatus.Text = error;
            return;
        }

        SetBusy(saveHost, true);
        try
        {
            Directory.CreateDirectory(HostDirectory);
            if (publicKeyPem is not null)
            {
                WriteTextAtomically(ImportedPublicKeyPath, publicKeyPem);
                ProtectPath(ImportedPublicKeyPath, localServiceReadable: false);
            }
            WriteJsonAtomically(HostConfigurationPath, new { HostAgent = configuration });
            ProtectPath(HostConfigurationPath, localServiceReadable: false);

            if (enableHostAgent.Checked)
            {
                RestartService(HostAgentServiceName);
                hostStatus.Text = allowExecution.Checked
                    ? "Host Agent 设置已生效，已允许受控升级。"
                    : "Host Agent 设置已生效，实际升级仍处于禁用状态。";
            }
            else
            {
                StopService(HostAgentServiceName);
                hostStatus.Text = "Host Agent 设置已保存，服务保持停止。";
            }
        }
        catch (CryptographicException)
        {
            hostStatus.Text = "Host 设置未生效：PEM 公钥无效。";
        }
        catch (IOException)
        {
            hostStatus.Text = "Host 设置未生效：受限目录不可写。";
        }
        catch (UnauthorizedAccessException)
        {
            hostStatus.Text = "Host 设置未生效：需要管理员权限。";
        }
        catch (InvalidOperationException)
        {
            hostStatus.Text = "Host 设置已保存，但服务启动失败。";
        }
        finally
        {
            SetBusy(saveHost, false);
            RefreshServiceStatus();
        }
    }

    private bool TryBuildHostConfiguration(out object configuration, out string? publicKeyPem, out string error)
    {
        configuration = null!;
        publicKeyPem = null;
        error = string.Empty;
        var hosts = allowedHosts.Lines.Select(host => host.Trim()).Where(host => host.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (hosts.Any(host => host.Length > 253 || host.Contains('/') || host.Contains(':')))
        {
            error = "受信下载域名格式无效。";
            return false;
        }

        if (!string.IsNullOrWhiteSpace(publicKeyPath.Text))
        {
            publicKeyPem = File.ReadAllText(publicKeyPath.Text);
            using var rsa = RSA.Create();
            rsa.ImportFromPem(publicKeyPem);
        }

        if (allowExecution.Checked &&
            (publicKeyPem is null || string.IsNullOrWhiteSpace(publicKeyId.Text) || hosts.Length == 0))
        {
            error = "启用实际升级前，必须选择 PEM 文件、填写公钥标识并配置受信下载域名。";
            return false;
        }

        configuration = new
        {
            Enabled = enableHostAgent.Checked,
            AllowExecution = allowExecution.Checked,
            OperationInboxDirectory = Path.Combine(HostDirectory, "inbox"),
            OperationReceiptDirectory = Path.Combine(HostDirectory, "receipts"),
            OperationStateDirectory = Path.Combine(HostDirectory, "state"),
            ReleaseArtifactDirectory = Path.Combine(HostDirectory, "releases"),
            SigningPublicKeyPath = publicKeyPem is null ? null : ImportedPublicKeyPath,
            SigningPublicKeyId = string.IsNullOrWhiteSpace(publicKeyId.Text) ? null : publicKeyId.Text.Trim(),
            AllowedArtifactHosts = hosts,
            MaximumArtifactBytes = (long)maximumArtifactMegabytes.Value * 1024 * 1024,
            WindowsInstallerExecutablePath = Path.Combine(Environment.SystemDirectory, "msiexec.exe"),
            ExecutionTimeoutSeconds = (int)executionTimeoutSeconds.Value
        };
        return true;
    }

    private void SelectPublicKey()
    {
        using var dialog = new OpenFileDialog { Filter = "PEM 公钥 (*.pem)|*.pem|所有文件 (*.*)|*.*", CheckFileExists = true };
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            publicKeyPath.Text = dialog.FileName;
        }
    }

    private bool TryNormalizeControlPlane(out Uri uri, out string error)
    {
        uri = null!;
        error = string.Empty;
        if (!Uri.TryCreate(controlPlane.Text.Trim(), UriKind.Absolute, out var parsed) ||
            !parsed.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            !string.IsNullOrEmpty(parsed.UserInfo) || !string.IsNullOrEmpty(parsed.Query) || !string.IsNullOrEmpty(parsed.Fragment))
        {
            error = "请输入不含账号、查询参数或片段的中心 HTTPS 地址。";
            return false;
        }
        uri = new Uri(parsed.ToString().TrimEnd('/') + "/", UriKind.Absolute);
        return true;
    }

    private static void WriteJsonAtomically(string path, object value) =>
        WriteTextAtomically(path, JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }));

    private static void WriteTextAtomically(string path, string content)
    {
        var directory = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(temporaryPath, content);
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    private static void ProtectPath(string path, bool localServiceReadable)
    {
        var security = new FileSecurity();
        security.SetAccessRuleProtection(true, false);
        security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null), FileSystemRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), FileSystemRights.FullControl, AccessControlType.Allow));
        if (localServiceReadable)
        {
            security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.LocalServiceSid, null), FileSystemRights.ReadAndExecute, AccessControlType.Allow));
        }
        new FileInfo(path).SetAccessControl(security);
    }

    private static void RestartService(string serviceName)
    {
        using var service = new ServiceController(serviceName);
        if (service.Status != ServiceControllerStatus.Stopped)
        {
            service.Stop();
            service.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(20));
        }
        service.Start();
    }

    private static void StopService(string serviceName)
    {
        using var service = new ServiceController(serviceName);
        if (service.Status is not ServiceControllerStatus.Stopped and not ServiceControllerStatus.StopPending)
        {
            service.Stop();
            service.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(20));
        }
    }

    private static async Task<bool> WaitForEnrollmentAsync()
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
        for (var attempt = 0; attempt < 20; attempt++)
        {
            try
            {
                using var response = await client.GetAsync("http://127.0.0.1:8080/api/v1/edge-agent/identity");
                using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                if (payload.RootElement.TryGetProperty("isEnrolled", out var enrolled) && enrolled.ValueKind == JsonValueKind.True)
                {
                    return true;
                }
            }
            catch (HttpRequestException) { }
            catch (TaskCanceledException) { }
            await Task.Delay(TimeSpan.FromSeconds(3));
        }
        return false;
    }

    private void LoadExistingConfiguration()
    {
        TryReadString(AgentConfigurationPath, "EdgeAgent", "ControlPlaneBaseUri", value => controlPlane.Text = value);
        TryReadString(HostConfigurationPath, "HostAgent", "SigningPublicKeyId", value => publicKeyId.Text = value);
        TryReadStringArray(HostConfigurationPath, "HostAgent", "AllowedArtifactHosts", values => allowedHosts.Text = string.Join(Environment.NewLine, values));
    }

    private static void TryReadString(string path, string section, string name, Action<string> setValue)
    {
        if (!File.Exists(path)) return;
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            if (document.RootElement.TryGetProperty(section, out var root) && root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String)
            {
                setValue(value.GetString() ?? string.Empty);
            }
        }
        catch (JsonException) { }
    }

    private static void TryReadStringArray(string path, string section, string name, Action<IReadOnlyList<string>> setValue)
    {
        if (!File.Exists(path)) return;
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            if (document.RootElement.TryGetProperty(section, out var root) && root.TryGetProperty(name, out var values) && values.ValueKind == JsonValueKind.Array)
            {
                setValue(values.EnumerateArray().Where(value => value.ValueKind == JsonValueKind.String).Select(value => value.GetString() ?? string.Empty).ToArray());
            }
        }
        catch (JsonException) { }
    }

    private void RefreshServiceStatus()
    {
        nodeStatus.Text = $"Edge Agent：{GetServiceStatus(EdgeAgentServiceName)}。";
        hostStatus.Text = $"Host Agent：{GetServiceStatus(HostAgentServiceName)}。";
    }

    private static string GetServiceStatus(string serviceName)
    {
        try
        {
            using var service = new ServiceController(serviceName);
            return service.Status.ToString();
        }
        catch (InvalidOperationException)
        {
            return "未安装";
        }
    }

    private static void SetBusy(Control control, bool busy) => control.Enabled = !busy;
}
