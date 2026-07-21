using System.Net;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.ServiceProcess;
using System.Text.Json;
using System.Text.Json.Nodes;

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
    private readonly TextBox baselineMsiPath = new() { Dock = DockStyle.Fill, ReadOnly = true };
    private readonly Button selectBaselineMsi = new() { Text = "选择当前 MSI", AutoSize = true };
    private readonly TextBox publicKeyId = new() { Dock = DockStyle.Fill, PlaceholderText = "release-key-2026" };
    private readonly TextBox allowedHosts = new() { Dock = DockStyle.Fill, Multiline = true, Height = 72, PlaceholderText = "github.com\r\nobjects.githubusercontent.com" };
    private readonly NumericUpDown maximumArtifactMegabytes = new() { Minimum = 1, Maximum = 8192, Value = 2048, Dock = DockStyle.Left, Width = 120 };
    private readonly NumericUpDown executionTimeoutSeconds = new() { Minimum = 30, Maximum = 3600, Value = 600, Dock = DockStyle.Left, Width = 120 };
    private readonly Button saveHost = new() { Text = "测试并保存 Host 设置", AutoSize = true };
    private readonly Label hostStatus = new() { AutoSize = true, ForeColor = SystemColors.GrayText };
    private readonly NumericUpDown cpuLimitPercent = new() { Minimum = 0, Maximum = 100, Value = 0, Dock = DockStyle.Left, Width = 120 };
    private readonly NumericUpDown memoryLimitMiB = new() { Minimum = 0, Maximum = 4_194_304, Value = 0, Dock = DockStyle.Left, Width = 120 };
    private readonly NumericUpDown diskWarningPercent = new() { Minimum = 70, Maximum = 95, Value = 85, Dock = DockStyle.Left, Width = 120 };
    private readonly Button saveResources = new() { Text = "保存并立即应用资源策略", AutoSize = true };
    private readonly Label resourceStatus = new() { AutoSize = true, ForeColor = SystemColors.GrayText };
    private readonly Label overviewStatus = new() { AutoSize = true, ForeColor = SystemColors.GrayText, MaximumSize = new Size(680, 0) };
    private readonly System.Windows.Forms.Timer refreshTimer = new() { Interval = 5_000 };

    private static string AgentDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "VisiCore", "EdgeAgent");
    private static string HostDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "VisiCore", "EdgeHostAgent");
    private static string AgentConfigurationPath => Path.Combine(AgentDirectory, "edge-agent.json");
    private static string BootstrapPath => Path.Combine(AgentDirectory, "bootstrap.json");
    private static string HostConfigurationPath => Path.Combine(HostDirectory, "edge-host-agent.json");
    private static string ImportedPublicKeyPath => Path.Combine(HostDirectory, "release-public-key.pem");
    private static string ImportedBaselineMsiPath => Path.Combine(HostDirectory, "known-good", "edge-node.msi");

    public EdgeNodeConfigurationForm()
    {
        Text = "VisiCore 边缘节点配置";
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        ClientSize = new Size(760, 610);

        var tabs = new TabControl { Dock = DockStyle.Fill };
        tabs.TabPages.Add(CreateOverviewPage());
        tabs.TabPages.Add(CreateConnectionPage());
        tabs.TabPages.Add(CreateResourcePage());
        tabs.TabPages.Add(CreateHostPage());
        Controls.Add(tabs);

        testConnection.Click += async (_, _) => await TestConnectionAsync();
        pair.Click += async (_, _) => await PairAsync();
        selectPublicKey.Click += (_, _) => SelectPublicKey();
        selectBaselineMsi.Click += (_, _) => SelectBaselineMsi();
        saveHost.Click += async (_, _) => await SaveHostAsync();
        saveResources.Click += async (_, _) => await SaveResourcesAsync();
        allowExecution.CheckedChanged += (_, _) => enableHostAgent.Checked = enableHostAgent.Checked || allowExecution.Checked;
        refreshTimer.Tick += async (_, _) => await RefreshOverviewAsync();
        LoadExistingConfiguration();
        RefreshServiceStatus();
        refreshTimer.Start();
        Shown += async (_, _) => await RefreshOverviewAsync();
    }

    private TabPage CreateOverviewPage()
    {
        var page = new TabPage("概览");
        var layout = CreateLayout();
        layout.Controls.Add(new Label
        {
            Text = "运行状态",
            AutoSize = true,
            Font = new Font(Font, FontStyle.Bold),
            Margin = new Padding(0, 8, 0, 6)
        }, 0, 0);
        layout.Controls.Add(overviewStatus, 0, 1);
        layout.Controls.Add(new Label
        {
            Text = "状态每 5 秒刷新。中心连接、资源使用、有效配额和 Host Agent 错误均只从本机回环端点读取。",
            AutoSize = true,
            ForeColor = SystemColors.GrayText,
            MaximumSize = new Size(680, 0),
            Margin = new Padding(0, 16, 0, 4)
        }, 0, 2);
        page.Controls.Add(layout);
        return page;
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
        AddField(layout, "当前已知良好 MSI", CreateBaselineMsiPicker());
        AddField(layout, "受信下载域名（每行一个）", allowedHosts);
        AddField(layout, "最大制品大小（MiB）", maximumArtifactMegabytes);
        AddField(layout, "执行超时（秒）", executionTimeoutSeconds);
        layout.Controls.Add(saveHost, 0, 11);
        layout.Controls.Add(new Label
        {
            Text = "PEM 和当前 MSI 都会导入受限目录。当前 MSI 仅用于升级失败回退，不会上传、写入发行清单或展示在后台。默认只启用发行验证，实际升级需单独明确开启。",
            AutoSize = true,
            ForeColor = SystemColors.GrayText,
            MaximumSize = new Size(580, 0),
            Margin = new Padding(0, 10, 0, 4)
        }, 0, 12);
        layout.Controls.Add(hostStatus, 0, 13);
        page.Controls.Add(layout);
        return page;
    }

    private TabPage CreateResourcePage()
    {
        var page = new TabPage("资源策略");
        var layout = CreateLayout();
        AddField(layout, "CPU 上限（宿主总算力 %，0 表示不限制）", cpuLimitPercent);
        AddField(layout, "内存上限（MiB，0 表示不限制）", memoryLimitMiB);
        AddField(layout, "磁盘预警阈值（%）", diskWarningPercent);
        layout.Controls.Add(saveResources, 0, 6);
        layout.Controls.Add(new Label
        {
            Text = "CPU 与内存限制仅作用于 Edge Agent 及其子进程。Host Agent 保持不受限，以确保策略、升级和恢复仍可执行。",
            AutoSize = true,
            ForeColor = SystemColors.GrayText,
            MaximumSize = new Size(680, 0),
            Margin = new Padding(0, 10, 0, 4)
        }, 0, 7);
        layout.Controls.Add(resourceStatus, 0, 8);
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

    private Control CreateBaselineMsiPicker()
    {
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, AutoSize = true };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        panel.Controls.Add(baselineMsiPath, 0, 0);
        panel.Controls.Add(selectBaselineMsi, 1, 0);
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
                    BootstrapFilePath = BootstrapPath,
                    HostUpgradeEnabled = false,
                    ResourcePolicy = BuildResourcePolicy(),
                    ResourcePolicyStatusPath = Path.Combine(AgentDirectory, "resource-policy-status.json")
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
        if (!TryBuildHostConfiguration(out var configuration, out var publicKeyPem, out var baselineMsiSource, out var error))
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
            if (baselineMsiSource is not null)
            {
                await ImportKnownGoodInstallerAsync(baselineMsiSource);
                baselineMsiPath.Text = ImportedBaselineMsiPath;
            }
            WriteJsonAtomically(HostConfigurationPath, new { HostAgent = configuration });
            ProtectPath(HostConfigurationPath, localServiceReadable: false);
            SetHostUpgradeCapability(enableHostAgent.Checked && allowExecution.Checked);

            if (enableHostAgent.Checked)
            {
                RestartService(HostAgentServiceName);
                RestartService(EdgeAgentServiceName);
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

    private bool TryBuildHostConfiguration(
        out object configuration,
        out string? publicKeyPem,
        out string? baselineMsiSource,
        out string error)
    {
        configuration = null!;
        publicKeyPem = null;
        baselineMsiSource = null;
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
        else if (File.Exists(ImportedPublicKeyPath))
        {
            publicKeyPem = File.ReadAllText(ImportedPublicKeyPath);
            using var rsa = RSA.Create();
            rsa.ImportFromPem(publicKeyPem);
        }

        if (allowExecution.Checked &&
            (publicKeyPem is null || string.IsNullOrWhiteSpace(publicKeyId.Text) || hosts.Length == 0))
        {
            error = "启用实际升级前，必须选择 PEM 文件、填写公钥标识并配置受信下载域名。";
            return false;
        }

        if (!string.IsNullOrWhiteSpace(baselineMsiPath.Text))
        {
            baselineMsiSource = baselineMsiPath.Text.Trim();
        }
        if (allowExecution.Checked &&
            (string.IsNullOrWhiteSpace(baselineMsiSource) ||
             !Path.IsPathFullyQualified(baselineMsiSource) ||
             !baselineMsiSource.EndsWith(".msi", StringComparison.OrdinalIgnoreCase) ||
             !File.Exists(baselineMsiSource) ||
             new FileInfo(baselineMsiSource).Length == 0))
        {
            error = "启用实际升级前，必须选择当前正在使用的 MSI，以建立本机回退基线。";
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
            WindowsInstallerPath = OperatingSystem.IsWindows() ? ImportedBaselineMsiPath : null,
            ManagedResourcePolicyStatusPath = Path.Combine(AgentDirectory, "resource-policy-status.json"),
            ResourcePolicy = BuildResourcePolicy(),
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

    private static void SetHostUpgradeCapability(bool enabled)
    {
        var root = File.Exists(AgentConfigurationPath)
            ? JsonNode.Parse(File.ReadAllText(AgentConfigurationPath))?.AsObject() ?? new JsonObject()
            : new JsonObject();
        var edgeAgent = root["EdgeAgent"] as JsonObject ?? new JsonObject();
        edgeAgent["HostUpgradeEnabled"] = enabled;
        root["EdgeAgent"] = edgeAgent;
        WriteJsonAtomically(AgentConfigurationPath, root);
        ProtectPath(AgentConfigurationPath, localServiceReadable: true);
    }

    private static void SetAgentResourcePolicy(object policy)
    {
        var root = File.Exists(AgentConfigurationPath)
            ? JsonNode.Parse(File.ReadAllText(AgentConfigurationPath))?.AsObject() ?? new JsonObject()
            : new JsonObject();
        var edgeAgent = root["EdgeAgent"] as JsonObject ?? new JsonObject();
        edgeAgent["ResourcePolicy"] = JsonSerializer.SerializeToNode(policy);
        edgeAgent["ResourcePolicyStatusPath"] = Path.Combine(AgentDirectory, "resource-policy-status.json");
        root["EdgeAgent"] = edgeAgent;
        WriteJsonAtomically(AgentConfigurationPath, root);
        ProtectPath(AgentConfigurationPath, localServiceReadable: true);
    }

    private object BuildResourcePolicy() => new
    {
        CpuLimitPercent = cpuLimitPercent.Value == 0 ? null : (int?)cpuLimitPercent.Value,
        MemoryLimitMiB = memoryLimitMiB.Value == 0 ? null : (int?)memoryLimitMiB.Value,
        DiskWarningPercent = (int)diskWarningPercent.Value
    };

    private bool TryBuildResourcePolicy(out object policy, out string error)
    {
        policy = BuildResourcePolicy();
        error = string.Empty;
        if (memoryLimitMiB.Value is > 0 and < 256)
        {
            error = "内存上限至少为 256 MiB，或设为 0 表示不限制。";
            return false;
        }
        return true;
    }

    private static JsonObject CreateDefaultHostConfiguration() => new()
    {
        ["Enabled"] = true,
        ["AllowExecution"] = false,
        ["OperationInboxDirectory"] = Path.Combine(HostDirectory, "inbox"),
        ["OperationReceiptDirectory"] = Path.Combine(HostDirectory, "receipts"),
        ["OperationStateDirectory"] = Path.Combine(HostDirectory, "state"),
        ["ReleaseArtifactDirectory"] = Path.Combine(HostDirectory, "releases"),
        ["WindowsInstallerExecutablePath"] = Path.Combine(Environment.SystemDirectory, "msiexec.exe"),
        ["WindowsUpdateRunnerExecutablePath"] = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "VisiCore", "EdgeNode", "VisiCore.EdgeUpdateRunner.exe")
    };

    private void SelectBaselineMsi()
    {
        using var dialog = new OpenFileDialog { Filter = "Windows Installer (*.msi)|*.msi", CheckFileExists = true };
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            baselineMsiPath.Text = dialog.FileName;
        }
    }

    private async Task SaveResourcesAsync()
    {
        if (!TryBuildResourcePolicy(out var policy, out var error))
        {
            resourceStatus.Text = error;
            return;
        }

        SetBusy(saveResources, true);
        try
        {
            Directory.CreateDirectory(HostDirectory);
            var root = File.Exists(HostConfigurationPath)
                ? JsonNode.Parse(File.ReadAllText(HostConfigurationPath))?.AsObject() ?? new JsonObject()
                : new JsonObject();
            var host = root["HostAgent"] as JsonObject ?? CreateDefaultHostConfiguration();
            host["Enabled"] = true;
            host["ResourcePolicy"] = JsonSerializer.SerializeToNode(policy);
            host["ManagedResourcePolicyStatusPath"] = Path.Combine(AgentDirectory, "resource-policy-status.json");
            root["HostAgent"] = host;
            WriteJsonAtomically(HostConfigurationPath, root);
            ProtectPath(HostConfigurationPath, localServiceReadable: false);
            SetAgentResourcePolicy(policy);
            RestartService(HostAgentServiceName);
            resourceStatus.Text = "资源策略已保存，Host Agent 正在应用限制。";
        }
        catch (IOException)
        {
            resourceStatus.Text = "资源策略未生效：受限目录不可写。";
        }
        catch (UnauthorizedAccessException)
        {
            resourceStatus.Text = "资源策略未生效：需要管理员权限。";
        }
        catch (InvalidOperationException)
        {
            resourceStatus.Text = "资源策略已保存，但 Host Agent 服务未能启动。";
        }
        finally
        {
            SetBusy(saveResources, false);
            await RefreshOverviewAsync();
        }
    }

    private static async Task ImportKnownGoodInstallerAsync(string sourcePath)
    {
        var targetDirectory = Path.GetDirectoryName(ImportedBaselineMsiPath)!;
        Directory.CreateDirectory(targetDirectory);
        if (!string.Equals(Path.GetFullPath(sourcePath), ImportedBaselineMsiPath, StringComparison.OrdinalIgnoreCase))
        {
            var temporaryPath = Path.Combine(targetDirectory, $".edge-node.{Guid.NewGuid():N}.tmp");
            try
            {
                File.Copy(sourcePath, temporaryPath, overwrite: false);
                File.Move(temporaryPath, ImportedBaselineMsiPath, overwrite: true);
            }
            finally
            {
                if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
            }
        }
        await using var stream = new FileStream(ImportedBaselineMsiPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        _ = await SHA256.HashDataAsync(stream);
        ProtectPath(ImportedBaselineMsiPath, localServiceReadable: false);
    }

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
                using var response = await client.GetAsync("http://127.0.0.1:18082/api/v1/edge-agent/identity");
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
        TryReadString(HostConfigurationPath, "HostAgent", "WindowsInstallerPath", value =>
        {
            if (File.Exists(value)) baselineMsiPath.Text = value;
        });
        TryReadResourcePolicy(HostConfigurationPath);
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

    private void TryReadResourcePolicy(string path)
    {
        if (!File.Exists(path)) return;
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            if (!document.RootElement.TryGetProperty("HostAgent", out var host) ||
                !host.TryGetProperty("ResourcePolicy", out var policy) ||
                policy.ValueKind != JsonValueKind.Object) return;
            if (policy.TryGetProperty("CpuLimitPercent", out var cpu) && cpu.TryGetInt32(out var cpuValue)) cpuLimitPercent.Value = cpuValue;
            if (policy.TryGetProperty("MemoryLimitMiB", out var memory) && memory.TryGetInt32(out var memoryValue)) memoryLimitMiB.Value = memoryValue;
            if (policy.TryGetProperty("DiskWarningPercent", out var disk) && disk.TryGetInt32(out var diskValue)) diskWarningPercent.Value = diskValue;
        }
        catch (JsonException) { }
    }

    private void RefreshServiceStatus()
    {
        nodeStatus.Text = $"Edge Agent：{GetServiceStatus(EdgeAgentServiceName)}。";
        hostStatus.Text = $"Host Agent：{GetServiceStatus(HostAgentServiceName)}。";
    }

    private async Task RefreshOverviewAsync()
    {
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
            using var response = await client.GetAsync("http://127.0.0.1:18082/api/v1/edge-agent/runtime");
            if (!response.IsSuccessStatusCode)
            {
                overviewStatus.Text = $"Edge Agent 服务：{GetServiceStatus(EdgeAgentServiceName)}。本地运行端点暂不可用。";
                return;
            }

            using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var runtime = payload.RootElement.TryGetProperty("runtime", out var runtimeValue) ? runtimeValue : default;
            var status = runtime.ValueKind == JsonValueKind.Object && runtime.TryGetProperty("status", out var statusValue)
                ? statusValue.GetString() ?? "未上报"
                : "未上报";
            var message = $"节点：{DescribeRuntimeStatus(status)}；Edge Agent：{GetServiceStatus(EdgeAgentServiceName)}；Host Agent：{GetServiceStatus(HostAgentServiceName)}。";
            if (runtime.ValueKind == JsonValueKind.Object && runtime.TryGetProperty("resource", out var resource) && resource.ValueKind == JsonValueKind.Object)
            {
                var cpu = resource.TryGetProperty("processCpuPercent", out var cpuValue) && cpuValue.TryGetDouble(out var cpuPercent)
                    ? $"CPU {cpuPercent:0.0}%"
                    : "CPU 未上报";
                var memory = resource.TryGetProperty("processMemoryBytes", out var memoryValue) && memoryValue.TryGetInt64(out var memoryBytes)
                    ? $"内存 {FormatBytes(memoryBytes)}"
                    : "内存未上报";
                var enforcement = resource.TryGetProperty("enforcementStatus", out var enforcementValue)
                    ? enforcementValue.GetString() ?? "未上报"
                    : "未上报";
                var failure = resource.TryGetProperty("enforcementFailureKind", out var failureValue) && failureValue.ValueKind == JsonValueKind.String
                    ? $"；资源策略失败：{failureValue.GetString()}"
                    : string.Empty;
                message += $"\r\n{cpu}；{memory}；资源策略：{enforcement}{failure}";
            }
            overviewStatus.Text = message;
        }
        catch (HttpRequestException)
        {
            overviewStatus.Text = $"Edge Agent 服务：{GetServiceStatus(EdgeAgentServiceName)}。本地运行端点不可达。";
        }
        catch (TaskCanceledException)
        {
            overviewStatus.Text = "本地运行状态读取超时。";
        }
        catch (JsonException)
        {
            overviewStatus.Text = "本地运行状态响应无效。";
        }
    }

    private static string DescribeRuntimeStatus(string status) => status switch
    {
        "running" => "在线运行",
        "awaiting_enrollment" => "等待配对",
        "starting" => "正在启动",
        "degraded" => "运行异常",
        _ => status
    };

    private static string FormatBytes(long value)
    {
        var units = new[] { "B", "KiB", "MiB", "GiB", "TiB" };
        var amount = (double)Math.Max(value, 0);
        var index = 0;
        while (amount >= 1024 && index < units.Length - 1)
        {
            amount /= 1024;
            index++;
        }
        return index == 0 ? $"{amount:0} {units[index]}" : $"{amount:0.0} {units[index]}";
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
