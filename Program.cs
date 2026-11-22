using Microsoft.Win32;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace NebulaOptimizer;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        int maintenanceIndex = Array.IndexOf(args, "--maintenance");
        if (maintenanceIndex >= 0)
        {
            int profileIndex = Array.IndexOf(args, "--profile");
            string profile = profileIndex >= 0 && profileIndex + 1 < args.Length ? args[profileIndex + 1] : "mi-equipo";
            Environment.ExitCode = MaintenanceRunner.Run(profile);
            return;
        }
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }
}

internal static class Theme
{
    public static readonly Color Ink = Color.FromArgb(28, 37, 54);
    public static readonly Color Muted = Color.FromArgb(110, 123, 143);
    public static readonly Color Canvas = Color.FromArgb(246, 248, 251);
    public static readonly Color Teal = Color.FromArgb(27, 157, 145);
    public static readonly Color TealDark = Color.FromArgb(20, 126, 119);
    public static readonly Color Mint = Color.FromArgb(226, 246, 241);
    public static readonly Color Amber = Color.FromArgb(174, 117, 31);
    public static readonly Color AmberPale = Color.FromArgb(255, 246, 225);
    public static readonly Color Line = Color.FromArgb(231, 235, 241);
    public static readonly Color Sidebar = Color.FromArgb(24, 32, 48);
}

internal sealed record DriveInfoRow(string Name, long Total, long Free)
{
    public string TotalText => Format(Total);
    public string FreeText => Format(Free);
    public int FreePercent => Total <= 0 ? 0 : (int)Math.Clamp(Free * 100d / Total, 0, 100);
    private static string Format(long n) => $"{n / 1073741824d:0.#} GB";
}

internal sealed record MaintenanceOptions(
    bool Enabled = false,
    int IntervalDays = 7,
    string RunAt = "03:00",
    bool BalancedEnergy = false,
    string OriginalPowerScheme = "",
    string LastRun = "",
    string LastResult = "",
    int LastFiles = 0,
    long LastBytes = 0);

internal sealed class DeviceInfo
{
    public string Name { get; init; } = Environment.MachineName;
    public string Os { get; init; } = RuntimeInformation.OSDescription.Trim();
    public string Architecture { get; init; } = RuntimeInformation.OSArchitecture.ToString();
    public string Cpu { get; init; } = "No disponible";
    public int Threads { get; init; } = Environment.ProcessorCount;
    public long Memory { get; init; }
    public DateTimeOffset Captured { get; init; } = DateTimeOffset.Now;
    public List<DriveInfoRow> Drives { get; init; } = [];

    public static DeviceInfo Capture()
    {
        string cpu = "No disponible";
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0", false);
            cpu = key?.GetValue("ProcessorNameString")?.ToString()?.Trim() ?? cpu;
        }
        catch { }
        var memory = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
        long totalMemory = GlobalMemoryStatusEx(ref memory) ? (long)memory.TotalPhysical : 0;
        var drives = new List<DriveInfoRow>();
        foreach (var drive in DriveInfo.GetDrives())
        {
            try
            {
                if (drive.DriveType == DriveType.Fixed && drive.IsReady)
                    drives.Add(new DriveInfoRow(drive.Name.TrimEnd('\\'), drive.TotalSize, drive.AvailableFreeSpace));
            }
            catch { }
        }
        return new DeviceInfo
        {
            Cpu = cpu,
            Memory = totalMemory,
            Drives = drives.OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase).ToList()
        };
    }

    public string MemoryText => Memory <= 0 ? "No disponible" : $"{Memory / 1073741824d:0.#} GB";

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatus
    {
        public uint Length, Load;
        public ulong TotalPhysical, AvailablePhysical, TotalPageFile, AvailablePageFile;
        public ulong TotalVirtual, AvailableVirtual, AvailableExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true, EntryPoint = "GlobalMemoryStatusEx")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatus buffer);
}

internal static class PrivateFile
{
    private const uint Dacl = 4;
    private const uint ProtectedDacl = 0x80000000;

    public static bool RestrictToCurrentUser(string path)
    {
        IntPtr descriptor = IntPtr.Zero;
        try
        {
            string? sid = WindowsIdentity.GetCurrent().User?.Value;
            if (string.IsNullOrWhiteSpace(sid) ||
                !ConvertStringSecurityDescriptorToSecurityDescriptor($"D:P(A;;FA;;;{sid})", 1, out descriptor, out _))
                return false;
            return SetFileSecurity(path, Dacl | ProtectedDacl, descriptor);
        }
        catch { return false; }
        finally { if (descriptor != IntPtr.Zero) LocalFree(descriptor); }
    }

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "ConvertStringSecurityDescriptorToSecurityDescriptorW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ConvertStringSecurityDescriptorToSecurityDescriptor(
        string sddl, uint revision, out IntPtr descriptor, out uint size);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "SetFileSecurityW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetFileSecurity(string path, uint information, IntPtr descriptor);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr LocalFree(IntPtr memory);
}

internal sealed class EnvProfile
{
    private readonly string _folder;
    private readonly string _stem;

    public string PathName => Path.Combine(_folder, _stem + ".env");
    public string Folder => _folder;
    public string Name => _stem;

    public EnvProfile(string name)
    {
        _folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NebulaOptimizer");
        _stem = Normalize(name);
    }

    public static string Normalize(string name)
    {
        string result = Regex.Replace(name.Trim(), @"[^a-zA-Z0-9_-]+", "-").Trim('-', '_');
        if (result.Length > 40) result = result[..40];
        return string.IsNullOrWhiteSpace(result) ? "mi-equipo" : result;
    }

    public static string? FindSavedName()
    {
        try
        {
            string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NebulaOptimizer");
            if (!Directory.Exists(folder)) return null;
            foreach (string file in Directory.EnumerateFiles(folder, "*.env").OrderByDescending(File.GetLastWriteTimeUtc))
            {
                if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0) continue;
                string? line = File.ReadLines(file).FirstOrDefault(x => x.StartsWith("PROFILE_NAME=", StringComparison.Ordinal));
                if (line is not null) return Normalize(line["PROFILE_NAME=".Length..].Trim().Trim('"'));
            }
        }
        catch { }
        return null;
    }

    public MaintenanceOptions LoadMaintenance()
    {
        try
        {
            if (!File.Exists(PathName) || (File.GetAttributes(PathName) & FileAttributes.ReparsePoint) != 0)
                return new MaintenanceOptions();
            var values = File.ReadLines(PathName)
                .Where(line => !line.StartsWith('#') && line.Contains('='))
                .Select(line => line.Split('=', 2))
                .ToDictionary(parts => parts[0], parts => Unquote(parts[1]), StringComparer.OrdinalIgnoreCase);
            string Get(string key, string fallback) => values.TryGetValue(key, out string? value) ? value : fallback;
            bool.TryParse(Get("MNT_ENABLED", "false"), out bool enabled);
            bool.TryParse(Get("MNT_BALANCED_ENERGY", "false"), out bool balanced);
            int.TryParse(Get("MNT_INTERVAL_DAYS", "7"), out int days);
            int.TryParse(Get("MNT_LAST_FILES", "0"), out int lastFiles);
            long.TryParse(Get("MNT_LAST_BYTES", "0"), out long lastBytes);
            return new MaintenanceOptions(enabled, days is 1 or 3 or 7 or 14 ? days : 7,
                Get("MNT_TIME", "03:00"), balanced, Get("MNT_ORIGINAL_SCHEME", ""),
                Get("MNT_LAST_RUN", ""), Get("MNT_LAST_RESULT", ""), lastFiles, lastBytes);
        }
        catch { return new MaintenanceOptions(); }
    }

    public bool Save(DeviceInfo info, MaintenanceOptions maintenance, out string note)
    {
        Directory.CreateDirectory(_folder);
        if ((File.GetAttributes(_folder) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("La carpeta del perfil es un enlace y no se usará.");
        if (File.Exists(PathName) && (File.GetAttributes(PathName) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("El archivo del perfil es un enlace y no se usará.");
        bool folderLocked = PrivateFile.RestrictToCurrentUser(_folder);
        var lines = new List<string>
        {
            "# Perfil local de Nebula Optimizer. Revisalo antes de compartir.",
            "# No contiene contrasenas, claves de producto, MAC ni numeros de serie.",
            $"PROFILE_NAME={Quote(_stem)}",
            $"DEVICE_NAME={Quote(info.Name)}",
            $"OS_DESCRIPTION={Quote(info.Os)}",
            $"OS_ARCHITECTURE={Quote(info.Architecture)}",
            $"CPU_MODEL={Quote(info.Cpu)}",
            $"CPU_LOGICAL_CORES={info.Threads.ToString(CultureInfo.InvariantCulture)}",
            $"MEMORY_TOTAL_BYTES={info.Memory.ToString(CultureInfo.InvariantCulture)}",
            $"CAPTURED_AT={Quote(info.Captured.ToString("O", CultureInfo.InvariantCulture))}",
            $"MNT_ENABLED={maintenance.Enabled.ToString().ToLowerInvariant()}",
            $"MNT_INTERVAL_DAYS={maintenance.IntervalDays.ToString(CultureInfo.InvariantCulture)}",
            $"MNT_TIME={Quote(maintenance.RunAt)}",
            $"MNT_BALANCED_ENERGY={maintenance.BalancedEnergy.ToString().ToLowerInvariant()}",
            $"MNT_ORIGINAL_SCHEME={Quote(maintenance.OriginalPowerScheme)}",
            $"MNT_LAST_RUN={Quote(maintenance.LastRun)}",
            $"MNT_LAST_RESULT={Quote(maintenance.LastResult)}",
            $"MNT_LAST_FILES={maintenance.LastFiles.ToString(CultureInfo.InvariantCulture)}",
            $"MNT_LAST_BYTES={maintenance.LastBytes.ToString(CultureInfo.InvariantCulture)}"
        };
        foreach (var drive in info.Drives)
        {
            string key = Regex.Replace(drive.Name.ToUpperInvariant(), @"[^A-Z0-9]", "_");
            lines.Add($"DRIVE_{key}_TOTAL_BYTES={drive.Total.ToString(CultureInfo.InvariantCulture)}");
            lines.Add($"DRIVE_{key}_FREE_BYTES={drive.Free.ToString(CultureInfo.InvariantCulture)}");
        }
        File.WriteAllText(PathName, string.Join(Environment.NewLine, lines) + Environment.NewLine, new UTF8Encoding(false));
        bool fileLocked = PrivateFile.RestrictToCurrentUser(PathName);
        note = folderLocked && fileLocked
            ? "Carpeta y archivo limitados a tu usuario de Windows."
            : "No se confirmaron todos los permisos privados; revisa AppData.";
        return folderLocked && fileLocked;
    }

    private static string Quote(string value)
    {
        value = value.Replace("\\", "\\\\").Replace("\r", "").Replace("\n", "").Replace("\"", "\\\"");
        return $"\"{value}\"";
    }

    private static string Unquote(string value)
    {
        value = value.Trim();
        if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
            value = value[1..^1].Replace("\\\"", "\"").Replace("\\\\", "\\");
        return value;
    }
}

internal sealed record TempFile(string Path, long Bytes);

internal static class SafeCleanup
{
    private static readonly TimeSpan AgeLimit = TimeSpan.FromHours(48);

    public static string CheckedRoot()
    {
        string root = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar);
        string profile = Path.GetFullPath(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)).TrimEnd(Path.DirectorySeparatorChar);
        if (!root.StartsWith(profile + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("La carpeta temporal queda fuera de tu perfil; la limpieza se desactivó.");
        if (!Directory.Exists(root) || (File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidOperationException("No se pudo validar la carpeta temporal local.");
        return root;
    }

    public static List<TempFile> Preview()
    {
        string root = CheckedRoot();
        DateTime cutoff = DateTime.UtcNow - AgeLimit;
        var result = new List<TempFile>();
        foreach (string path in Directory.EnumerateFiles(root, "*", SearchOption.TopDirectoryOnly))
        {
            try
            {
                var file = new FileInfo(path);
                if (file.LastWriteTimeUtc <= cutoff && (file.Attributes & FileAttributes.ReparsePoint) == 0)
                    result.Add(new TempFile(file.FullName, file.Length));
            }
            catch { }
        }
        return result;
    }

    public static (int Removed, long Bytes, int Skipped) Run(IEnumerable<TempFile> files)
    {
        string root = CheckedRoot();
        DateTime cutoff = DateTime.UtcNow - AgeLimit;
        int removed = 0, skipped = 0;
        long bytes = 0;
        foreach (var item in files)
        {
            try
            {
                string full = Path.GetFullPath(item.Path);
                if (!string.Equals(Path.GetDirectoryName(full)?.TrimEnd(Path.DirectorySeparatorChar), root, StringComparison.OrdinalIgnoreCase))
                { skipped++; continue; }
                var file = new FileInfo(full);
                if (!file.Exists || file.LastWriteTimeUtc > cutoff || (file.Attributes & FileAttributes.ReparsePoint) != 0)
                { skipped++; continue; }
                long size = file.Length;
                File.Delete(full);
                removed++;
                bytes += size;
            }
            catch { skipped++; }
        }
        return (removed, bytes, skipped);
    }
}

internal static class PowerPlanService
{
    public const string BalancedGuid = "381b4222-f694-41f0-9685-ff5bb260df2e";
    private static readonly string[] HighPerformancePlans = ["8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c", "e9a42b02-d5df-448d-aa00-03f14749eb61"];
    private static readonly Regex GuidPattern = new(@"[0-9a-fA-F]{8}-(?:[0-9a-fA-F]{4}-){3}[0-9a-fA-F]{12}", RegexOptions.Compiled);

    public static string CurrentGuid()
    {
        string output = RunPowerCfg("/getactivescheme");
        Match match = GuidPattern.Match(output);
        if (!match.Success) throw new InvalidOperationException("Windows no devolvió el plan de energía activo.");
        return match.Value;
    }

    public static bool TryApplyBalanced(out string original, out string note)
    {
        original = CurrentGuid();
        if (string.Equals(original, BalancedGuid, StringComparison.OrdinalIgnoreCase))
        {
            note = "Windows ya usa el plan Equilibrado";
            return false;
        }
        if (!HighPerformancePlans.Contains(original, StringComparer.OrdinalIgnoreCase))
        {
            note = "Se conservó el plan de ahorro o personalizado de Windows";
            original = "";
            return false;
        }
        SetActive(BalancedGuid);
        note = "Windows cambió de Alto rendimiento a Equilibrado";
        return true;
    }

    public static void RestoreIfBalanced(string original)
    {
        if (!GuidPattern.IsMatch(original)) return;
        if (string.Equals(CurrentGuid(), BalancedGuid, StringComparison.OrdinalIgnoreCase))
            SetActive(original);
    }

    private static void SetActive(string guid) => _ = RunPowerCfg("/setactive", guid);

    private static string RunPowerCfg(params string[] args)
    {
        var start = new ProcessStartInfo("powercfg.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string arg in args) start.ArgumentList.Add(arg);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("No se pudo iniciar PowerCfg.");
        string output = process.StandardOutput.ReadToEnd();
        string error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0) throw new InvalidOperationException(string.IsNullOrWhiteSpace(error) ? output.Trim() : error.Trim());
        return output;
    }
}

internal static class MaintenanceScheduler
{
    private const string TaskName = "NebulaOptimizer-6C395D83-6E81-4B8D-B451-8C20511BEA67-Maintenance";

    public static void Configure(EnvProfile profile, MaintenanceOptions options)
    {
        if (!options.Enabled) { Remove(); return; }
        string appPath = Environment.ProcessPath ?? throw new InvalidOperationException("No se encontró la ruta del optimizador.");
        if (!Path.GetFileName(appPath).Equals("NebulaOptimizer.exe", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Abre la aplicación incluida en el ZIP para crear el horario automático.");
        if (!TimeSpan.TryParseExact(options.RunAt, @"hh\:mm", CultureInfo.InvariantCulture, out _))
            throw new InvalidOperationException("La hora del mantenimiento no es válida.");
        string taskCommand = $"\"{appPath.Replace("\"", "\\\"")}\" --maintenance --profile \"{profile.Name}\"";
        RunSchtasks([
            "/Create", "/SC", "DAILY", "/MO", options.IntervalDays.ToString(CultureInfo.InvariantCulture),
            "/ST", options.RunAt, "/TN", TaskName, "/TR", taskCommand, "/F", "/RL", "LIMITED", "/IT"
        ], expectMissing: false);
    }

    private static void Remove()
    {
        if (RunSchtasks(["/Query", "/TN", TaskName], expectMissing: true) is null) return;
        RunSchtasks(["/Delete", "/TN", TaskName, "/F"], expectMissing: false);
    }

    private static string? RunSchtasks(IEnumerable<string> args, bool expectMissing)
    {
        var start = new ProcessStartInfo("schtasks.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string arg in args) start.ArgumentList.Add(arg);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("No se pudo iniciar el Programador de tareas de Windows.");
        string output = process.StandardOutput.ReadToEnd();
        string error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (expectMissing && process.ExitCode != 0) return null;
        if (process.ExitCode != 0) throw new InvalidOperationException(string.IsNullOrWhiteSpace(error) ? output.Trim() : error.Trim());
        return output;
    }
}

internal static class MaintenanceRunner
{
    public static int Run(string profileName)
    {
        var profile = new EnvProfile(EnvProfile.Normalize(profileName));
        MaintenanceOptions options = profile.LoadMaintenance();
        if (!options.Enabled) return 0;

        DeviceInfo device = DeviceInfo.Capture();
        int removed = 0;
        long bytes = 0;
        var notes = new List<string>();
        try
        {
            var candidates = SafeCleanup.Preview();
            var cleaned = SafeCleanup.Run(candidates);
            removed = cleaned.Removed;
            bytes = cleaned.Bytes;
            notes.Add($"temporales eliminados: {removed}; liberado: {FormatBytes(bytes)}");
        }
        catch (Exception ex) { notes.Add("limpieza omitida: " + ex.Message); }

        if (options.BalancedEnergy)
        {
            try
            {
                bool changed = PowerPlanService.TryApplyBalanced(out string original, out string note);
                if (changed || string.IsNullOrWhiteSpace(options.OriginalPowerScheme))
                    options = options with { OriginalPowerScheme = original };
                notes.Add(note);
            }
            catch (Exception ex) { notes.Add("plan de energía sin cambios: " + ex.Message); }
        }
        else if (!string.IsNullOrWhiteSpace(options.OriginalPowerScheme))
        {
            try { if (!string.IsNullOrWhiteSpace(options.OriginalPowerScheme)) PowerPlanService.RestoreIfBalanced(options.OriginalPowerScheme); }
            catch { }
            options = options with { OriginalPowerScheme = "" };
        }

        options = options with
        {
            LastRun = DateTimeOffset.Now.ToString("O", CultureInfo.InvariantCulture),
            LastFiles = removed,
            LastBytes = bytes,
            LastResult = string.Join("; ", notes)
        };
        try { _ = profile.Save(device, options, out _); return 0; }
        catch { return 1; }
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        string[] units = ["KB", "MB", "GB", "TB"];
        double value = bytes;
        int i = -1;
        do { value /= 1024; i++; } while (value >= 1024 && i < units.Length - 1);
        return $"{value:0.#} {units[i]}";
    }
}

internal sealed record CheckRow(string Title, string Detail, bool Good);

internal sealed class SmoothPanel : Panel
{
    public SmoothPanel() { DoubleBuffered = true; ResizeRedraw = true; }
}

internal sealed class NebulaLogo : Control
{
    public NebulaLogo() { SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true); }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        float size = Math.Min(ClientSize.Width, ClientSize.Height) - 2;
        var bounds = new RectangleF((ClientSize.Width - size) / 2, (ClientSize.Height - size) / 2, size, size);
        using var background = new LinearGradientBrush(bounds, Color.FromArgb(40, 194, 177), Color.FromArgb(63, 99, 195), 42f);
        e.Graphics.FillEllipse(background, bounds);
        using var orbit = new Pen(Color.FromArgb(215, 255, 255, 255), Math.Max(1.2f, size * 0.055f));
        var state = e.Graphics.Save();
        e.Graphics.TranslateTransform(ClientSize.Width / 2f, ClientSize.Height / 2f);
        e.Graphics.RotateTransform(-30f);
        e.Graphics.DrawEllipse(orbit, -size * 0.36f, -size * 0.15f, size * 0.72f, size * 0.30f);
        e.Graphics.Restore(state);
        using var core = new SolidBrush(Color.White);
        float center = size * 0.11f;
        e.Graphics.FillEllipse(core, ClientSize.Width / 2f - center, ClientSize.Height / 2f - center, center * 2, center * 2);
        e.Graphics.FillEllipse(core, ClientSize.Width * 0.69f, ClientSize.Height * 0.29f, Math.Max(2f, size * 0.07f), Math.Max(2f, size * 0.07f));
    }
}

internal sealed class MainForm : Form
{
    private readonly SmoothPanel _pageHost = new();
    private readonly Label _status = new();
    private readonly List<Button> _nav = [];
    private readonly Panel _navIndicator = new() { Width = 3, Height = 28, BackColor = Theme.Teal };
    private System.Windows.Forms.Timer? _navTimer;
    private System.Windows.Forms.Timer? _pageTimer;
    private System.Windows.Forms.Timer? _introTimer;
    private DeviceInfo _device = DeviceInfo.Capture();
    private EnvProfile _profile;
    private MaintenanceOptions _maintenance;
    private bool _profileSecure;
    private string _profileNote = "";
    private List<TempFile> _tempFiles = [];
    private static readonly string[] Pages = ["Inicio", "Optimizar", "Automático", "Seguridad", "Equipo", "Ajustes"];
    private static readonly string[] Icons = ["⌂", "✦", "↻", "✓", "▦", "⚙"];

    public MainForm()
    {
        _profile = new EnvProfile(EnvProfile.FindSavedName() ?? "mi-equipo");
        _maintenance = _profile.LoadMaintenance();
        SaveProfile();
        BuildWindow();
        Shown += (_, _) => AnimateIntro();
        ShowPage("Inicio");
    }

    private void BuildWindow()
    {
        Text = "Nebula Optimizer";
        try { if (Environment.ProcessPath is string executable) Icon = System.Drawing.Icon.ExtractAssociatedIcon(executable); } catch { }
        Opacity = 0;
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(1000, 680);
        Size = new Size(1240, 820);
        BackColor = Theme.Canvas;
        Font = new Font("Segoe UI", 10);
        AutoScaleMode = AutoScaleMode.Dpi;
        DoubleBuffered = true;

        var shell = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, BackColor = Theme.Canvas };
        shell.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 236));
        shell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        Controls.Add(shell);

        var side = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Sidebar, Padding = new Padding(18, 20, 16, 14) };
        shell.Controls.Add(side, 0, 0);
        var mark = new NebulaLogo { Location = new Point(0, 2), Size = new Size(42, 42) };
        side.Controls.Add(mark);
        side.Controls.Add(new Label { Text = "NÉBULA", Location = new Point(54, 2), AutoSize = true, ForeColor = Color.White, Font = new Font("Segoe UI", 12, FontStyle.Bold) });
        side.Controls.Add(new Label { Text = "OPTIMIZER", Location = new Point(55, 26), AutoSize = true, ForeColor = Color.FromArgb(150, 164, 188), Font = new Font("Segoe UI", 8, FontStyle.Bold) });
        var menu = new FlowLayoutPanel { Location = new Point(0, 92), Size = new Size(198, 370), FlowDirection = FlowDirection.TopDown, WrapContents = false, BackColor = Theme.Sidebar };
        side.Controls.Add(menu);
        for (int i = 0; i < Pages.Length; i++)
        {
            var button = new Button
            {
                Text = $"  {Icons[i]}    {Pages[i]}", Tag = Pages[i], Width = 198, Height = 46,
                TextAlign = ContentAlignment.MiddleLeft, FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 10), ForeColor = Color.FromArgb(195, 205, 221),
                BackColor = Theme.Sidebar, Cursor = Cursors.Hand, Margin = new Padding(0, 2, 0, 7)
            };
            button.FlatAppearance.BorderSize = 0;
            button.FlatAppearance.MouseOverBackColor = Color.FromArgb(38, 50, 71);
            button.Click += (_, _) => ShowPage((string)button.Tag!);
            menu.Controls.Add(button);
            _nav.Add(button);
        }
        _navIndicator.Location = new Point(0, menu.Top + 10);
        _navIndicator.BringToFront();
        side.Controls.Add(_navIndicator);
        var sideFoot = new Label
        {
            Text = "Hecho para cuidar tu equipo.\\nSin telemetría · Sin anuncios",
            Location = new Point(0, Math.Max(420, Height - 170)), Size = new Size(202, 50), Anchor = AnchorStyles.Left | AnchorStyles.Bottom,
            ForeColor = Color.FromArgb(145, 162, 188), Font = new Font("Segoe UI", 8.5F)
        };
        side.Controls.Add(sideFoot);

        var main = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Canvas };
        shell.Controls.Add(main, 1, 0);
        var header = new Panel { Dock = DockStyle.Top, Height = 68, BackColor = Color.White, Padding = new Padding(26, 0, 24, 0) };
        main.Controls.Add(header);
        header.Controls.Add(new Label { Text = "OPTIMIZACIÓN LOCAL", Location = new Point(26, 24), AutoSize = true, ForeColor = Theme.Muted, Font = new Font("Segoe UI", 8.5F, FontStyle.Bold) });
        _status.Text = _profileSecure ? "●  Perfil local listo" : "●  Revisa permisos del perfil";
        _status.AutoSize = true;
        _status.ForeColor = _profileSecure ? Theme.TealDark : Theme.Amber;
        _status.Font = new Font("Segoe UI", 9, FontStyle.Bold);
        _status.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        header.Controls.Add(_status);
        header.Resize += (_, _) => _status.Location = new Point(header.ClientSize.Width - 192, 24);
        _pageHost.Dock = DockStyle.Fill;
        _pageHost.BackColor = Theme.Canvas;
        main.Controls.Add(_pageHost);
    }

    private void ShowPage(string page)
    {
        foreach (var button in _nav)
        {
            bool active = string.Equals(button.Tag?.ToString(), page, StringComparison.Ordinal);
            button.BackColor = active ? Color.FromArgb(42, 57, 80) : Theme.Sidebar;
            button.ForeColor = active ? Color.White : Color.FromArgb(195, 205, 221);
            button.Font = new Font("Segoe UI", 10, active ? FontStyle.Bold : FontStyle.Regular);
        }
        _pageTimer?.Stop();
        _pageTimer?.Dispose();
        _pageHost.Controls.Clear();
        var surface = new SmoothPanel
        {
            Location = new Point(18, 0), Size = _pageHost.ClientSize,
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
            BackColor = Theme.Canvas
        };
        _pageHost.Controls.Add(surface);
        var flow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, AutoScroll = true, WrapContents = false, FlowDirection = FlowDirection.TopDown,
            BackColor = Theme.Canvas, Padding = new Padding(28, 24, 24, 24)
        };
        surface.Controls.Add(flow);
        flow.Resize += (_, _) => SetContentWidths(flow);
        switch (page)
        {
            case "Inicio": BuildHome(flow); break;
            case "Optimizar": BuildOptimizer(flow); break;
            case "Automático": BuildAutomatic(flow); break;
            case "Seguridad": BuildSecurity(flow); break;
            case "Equipo": BuildEquipment(flow); break;
            case "Ajustes": BuildSettings(flow); break;
        }
        SetContentWidths(flow);
        AnimatePage(surface);
        AnimateNavigation(page);
    }

    private void AnimateIntro()
    {
        _introTimer?.Stop();
        _introTimer?.Dispose();
        var clock = System.Diagnostics.Stopwatch.StartNew();
        _introTimer = new System.Windows.Forms.Timer { Interval = 15 };
        _introTimer.Tick += (_, _) =>
        {
            double t = Math.Clamp(clock.Elapsed.TotalMilliseconds / 220d, 0, 1);
            Opacity = 1 - Math.Pow(1 - t, 3);
            if (t >= 1) { Opacity = 1; _introTimer.Stop(); _introTimer.Dispose(); _introTimer = null; }
        };
        _introTimer.Start();
    }

    private void AnimatePage(Control surface)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        _pageTimer = new System.Windows.Forms.Timer { Interval = 15 };
        _pageTimer.Tick += (_, _) =>
        {
            double t = Math.Clamp(clock.Elapsed.TotalMilliseconds / 190d, 0, 1);
            double eased = 1 - Math.Pow(1 - t, 3);
            if (!surface.IsDisposed) surface.Left = (int)Math.Round(18 * (1 - eased));
            if (t >= 1) { _pageTimer.Stop(); _pageTimer.Dispose(); _pageTimer = null; }
        };
        _pageTimer.Start();
    }

    private void AnimateNavigation(string page)
    {
        Button? selected = _nav.FirstOrDefault(button => string.Equals(button.Tag?.ToString(), page, StringComparison.Ordinal));
        if (selected is null) return;
        int start = _navIndicator.Top;
        int target = (selected.Parent?.Top ?? 0) + selected.Top + 9;
        _navTimer?.Stop();
        _navTimer?.Dispose();
        var clock = System.Diagnostics.Stopwatch.StartNew();
        _navTimer = new System.Windows.Forms.Timer { Interval = 15 };
        _navTimer.Tick += (_, _) =>
        {
            double t = Math.Clamp(clock.Elapsed.TotalMilliseconds / 180d, 0, 1);
            double eased = 1 - Math.Pow(1 - t, 3);
            _navIndicator.Top = start + (int)Math.Round((target - start) * eased);
            if (t >= 1) { _navIndicator.Top = target; _navTimer.Stop(); _navTimer.Dispose(); _navTimer = null; }
        };
        _navTimer.Start();
    }

    private static void SetContentWidths(FlowLayoutPanel flow)
    {
        int width = Math.Max(540, flow.ClientSize.Width - flow.Padding.Horizontal - 26);
        foreach (Control control in flow.Controls)
            if (control.Tag?.ToString() == "wide") control.Width = width;
    }

    private void BuildHome(FlowLayoutPanel flow)
    {
        Title(flow, "Tu equipo, en equilibrio.", "Herramientas claras para mantener Windows sin cambios agresivos.");
        var hero = Card(194);
        hero.BackColor = Color.FromArgb(237, 248, 246);
        hero.Controls.Add(TextLabel("UN ESPACIO MÁS LIGERO", 22, 20, Theme.TealDark, 8.5F, true));
        hero.Controls.Add(TextLabel("Optimiza con calma.", 22, 48, Theme.Ink, 22, true));
        hero.Controls.Add(BoxLabel("Revisa temporales antiguos, consulta tu equipo y conserva el control de cada acción.", 22, 91, 570, 42));
        var go = Primary("Revisar optimizaciones", 184);
        go.Location = new Point(22, 142);
        go.Click += (_, _) => ShowPage("Optimizar");
        hero.Controls.Add(go);
        flow.Controls.Add(hero);

        var metrics = Card(132);
        metrics.Controls.Add(TextLabel("RESUMEN DEL EQUIPO", 20, 14, Theme.Muted, 8, true));
        Metric(metrics, "Memoria", _device.MemoryText, 22, 47);
        Metric(metrics, "Procesador", _device.Threads + " hilos", 255, 47);
        Metric(metrics, "Espacio libre", MainDriveFree(), 488, 47);
        flow.Controls.Add(metrics);

        var note = Card(99);
        note.Controls.Add(TextLabel("Una experiencia transparente", 20, 17, Theme.Ink, 11, true));
        note.Controls.Add(BoxLabel("La limpieza se limita a archivos temporales de tu usuario con más de 48 horas. No requiere permisos de administrador.", 20, 47, 830, 38));
        flow.Controls.Add(note);
        var routine = Card(112);
        routine.Controls.Add(TextLabel("Mantenimiento automático", 20, 17, Theme.Ink, 11, true));
        string routineText = _maintenance.Enabled
            ? $"Activo cada {_maintenance.IntervalDays} día(s) a las {_maintenance.RunAt}."
            : "Aún no programado. Elige frecuencia y hora para automatizar la rutina.";
        routine.Controls.Add(BoxLabel(routineText, 20, 48, 620, 30));
        var configure = Secondary("Configurar rutina", 164);
        configure.Location = new Point(20, 74);
        configure.Click += (_, _) => ShowPage("Automático");
        routine.Controls.Add(configure);
        flow.Controls.Add(routine);
        Footnote(flow, "Los indicadores describen capacidad; no ejecutamos pruebas de rendimiento.");
    }

    private void BuildOptimizer(FlowLayoutPanel flow)
    {
        Title(flow, "Optimización segura", "Cada acción está acotada y requiere que tú la confirmes.");
        var card = Card(326);
        card.Name = "cleanupCard";
        card.Controls.Add(TextLabel("Limpieza de temporales antiguos", 22, 18, Theme.Ink, 15, true));
        card.Controls.Add(BoxLabel("Solo archivos sueltos de tu carpeta temporal que llevan más de 48 horas sin cambios.", 22, 52, 850, 26));
        var pill = new Label { Text = "SEGURA · SIN ADMINISTRADOR", Location = new Point(22, 91), AutoSize = true, Padding = new Padding(10, 6, 10, 6), BackColor = Theme.Mint, ForeColor = Theme.TealDark, Font = new Font("Segoe UI", 8, FontStyle.Bold) };
        card.Controls.Add(pill);
        var details = TextLabel("Analizando alcance permitido…", 24, 137, Theme.Ink, 11, true);
        details.Name = "cleanupDetails";
        details.Size = new Size(820, 34);
        card.Controls.Add(details);
        var analyze = Secondary("Analizar de nuevo", 155);
        analyze.Location = new Point(22, 193);
        analyze.Click += (_, _) => UpdatePreview(card);
        card.Controls.Add(analyze);
        var consent = new CheckBox
        {
            Name = "cleanupConsent", Text = "Entiendo que se eliminarán solo los temporales antiguos listados arriba.",
            Location = new Point(22, 242), AutoSize = true, ForeColor = Theme.Muted, Font = new Font("Segoe UI", 9)
        };
        card.Controls.Add(consent);
        var clean = Primary("Limpiar ahora", 142);
        clean.Location = new Point(22, 276);
        clean.Enabled = false;
        consent.CheckedChanged += (_, _) => clean.Enabled = consent.Checked;
        clean.Click += async (_, _) => await RunCleanup(card, consent, clean);
        card.Controls.Add(clean);
        flow.Controls.Add(card);
        UpdatePreview(card);

        var limits = Card(120);
        limits.Controls.Add(TextLabel("Límites de esta acción", 20, 17, Theme.Ink, 11, true));
        limits.Controls.Add(BoxLabel("No entra en subcarpetas, no toca Descargas ni documentos, omite enlaces y archivos recientes o bloqueados. El espacio recuperado depende de lo que encuentre.", 20, 49, 850, 48));
        flow.Controls.Add(limits);
    }

    private void UpdatePreview(Panel card)
    {
        var label = card.Controls.Find("cleanupDetails", false).FirstOrDefault() as Label;
        if (label is null) return;
        try
        {
            _tempFiles = SafeCleanup.Preview();
            label.Text = $"{_tempFiles.Count:N0} archivos aptos · {FormatBytes(_tempFiles.Sum(x => x.Bytes))} · antigüedad mínima: 48 horas";
            label.ForeColor = Theme.Ink;
        }
        catch (Exception ex)
        {
            _tempFiles = [];
            label.Text = ex.Message;
            label.ForeColor = Theme.Amber;
        }
    }

    private async Task RunCleanup(Panel card, CheckBox consent, Button button)
    {
        if (_tempFiles.Count == 0)
        {
            MessageBox.Show(this, "No hay temporales antiguos aptos para limpiar.", "Nebula Optimizer", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        var answer = MessageBox.Show(this,
            $"Se intentará eliminar {_tempFiles.Count:N0} archivos temporales de más de 48 horas ({FormatBytes(_tempFiles.Sum(x => x.Bytes))}).\\n\\n¿Continuar?",
            "Confirmar limpieza", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
        if (answer != DialogResult.Yes) return;
        button.Enabled = false;
        consent.Enabled = false;
        try
        {
            var result = await Task.Run(() => SafeCleanup.Run(_tempFiles));
            MessageBox.Show(this, $"Limpieza finalizada.\\n\\nEliminados: {result.Removed:N0}\\nEspacio recuperado: {FormatBytes(result.Bytes)}\\nOmitidos o bloqueados: {result.Skipped:N0}",
                "Resultado", MessageBoxButtons.OK, MessageBoxIcon.Information);
            consent.Checked = false;
            UpdatePreview(card);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "No se pudo limpiar", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally { consent.Enabled = true; button.Enabled = consent.Checked; }
    }

    private void BuildAutomatic(FlowLayoutPanel flow)
    {
        Title(flow, "Mantenimiento automático", "Programa la limpieza y una rutina de energía reversible.");
        var card = Card(390);
        card.Controls.Add(TextLabel("Tu rutina, en el horario que elijas", 22, 18, Theme.Ink, 14, true));
        var enabled = new CheckBox
        {
            Text = "Activar mantenimiento automático", Checked = _maintenance.Enabled,
            Location = new Point(22, 56), AutoSize = true, Font = new Font("Segoe UI Semibold", 10, FontStyle.Bold), ForeColor = Theme.Ink
        };
        card.Controls.Add(enabled);
        card.Controls.Add(TextLabel("Frecuencia", 22, 97, Theme.Muted, 9, true));
        var frequency = new ComboBox
        {
            Location = new Point(22, 120), Width = 190, DropDownStyle = ComboBoxStyle.DropDownList,
            Font = new Font("Segoe UI", 10)
        };
        frequency.Items.AddRange(["Cada día", "Cada 3 días", "Cada semana", "Cada 14 días"]);
        frequency.SelectedIndex = _maintenance.IntervalDays switch { 1 => 0, 3 => 1, 14 => 3, _ => 2 };
        card.Controls.Add(frequency);
        card.Controls.Add(TextLabel("Hora local", 250, 97, Theme.Muted, 9, true));
        var time = new DateTimePicker
        {
            Location = new Point(250, 120), Width = 112, Format = DateTimePickerFormat.Custom,
            CustomFormat = "HH:mm", ShowUpDown = true,
            Value = DateTime.Today.Add(TimeSpan.TryParse(_maintenance.RunAt, out var parsedTime) ? parsedTime : TimeSpan.FromHours(3))
        };
        card.Controls.Add(time);
        var balanced = new CheckBox
        {
            Text = "Cambiar de Alto rendimiento a Equilibrado",
            Checked = _maintenance.BalancedEnergy, Location = new Point(22, 169), AutoSize = true,
            ForeColor = Theme.Ink, Font = new Font("Segoe UI", 9.5F)
        };
        card.Controls.Add(balanced);
        card.Controls.Add(BoxLabel("Al activar esta opción, Windows cambia ahora y en cada ejecución solo si detecta Alto rendimiento. Puede reducir consumo en hardware compatible y bajar rendimiento; la GPU dedicada depende de su fabricante. Conserva planes de ahorro o personalizados. Si Windows rechaza el cambio, la limpieza seguirá programada.", 22, 198, 850, 62));
        card.Controls.Add(BoxLabel("La rutina elimina solo archivos sueltos de %TEMP% con más de 48 horas. Se ejecuta con tu usuario, cuando la sesión está iniciada y el equipo está despierto.", 22, 269, 850, 44));
        var save = Primary("Guardar y programar", 190);
        save.Location = new Point(22, 330);
        save.Click += (_, _) => SaveMaintenance(enabled.Checked, frequency.SelectedIndex, time.Value.ToString("HH:mm", CultureInfo.InvariantCulture), balanced.Checked);
        card.Controls.Add(save);
        void UpdateAvailability()
        {
            frequency.Enabled = enabled.Checked;
            time.Enabled = enabled.Checked;
            balanced.Enabled = enabled.Checked;
        }
        enabled.CheckedChanged += (_, _) => UpdateAvailability();
        UpdateAvailability();
        flow.Controls.Add(card);

        var report = Card(128);
        report.Controls.Add(TextLabel("Último mantenimiento", 20, 16, Theme.Ink, 12, true));
        string when = DateTimeOffset.TryParse(_maintenance.LastRun, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var last)
            ? last.ToLocalTime().ToString("dd/MM/yyyy · HH:mm") : "Todavía no se ha ejecutado";
        report.Controls.Add(BoxLabel(when, 20, 48, 820, 22));
        report.Controls.Add(BoxLabel(string.IsNullOrWhiteSpace(_maintenance.LastResult) ? "La tarea automática registra el resultado en tu archivo .env." : _maintenance.LastResult, 20, 76, 850, 36));
        flow.Controls.Add(report);
    }

    private void SaveMaintenance(bool enabled, int frequencyIndex, string runAt, bool balanced)
    {
        int[] intervals = [1, 3, 7, 14];
        int interval = frequencyIndex >= 0 && frequencyIndex < intervals.Length ? intervals[frequencyIndex] : 7;
        MaintenanceOptions previous = _maintenance;
        string originalScheme = previous.OriginalPowerScheme;
        string planFeedback = "";
        bool appliedPlan = false;
        bool restoredPlan = false;
        try
        {
            if (enabled && balanced && !previous.BalancedEnergy)
            {
                try { appliedPlan = PowerPlanService.TryApplyBalanced(out originalScheme, out planFeedback); }
                catch (Exception ex) { originalScheme = previous.OriginalPowerScheme; planFeedback = "No se pudo cambiar el plan: " + ex.Message; }
            }
            else if (enabled && balanced)
            {
                try
                {
                    bool changed = PowerPlanService.TryApplyBalanced(out string detectedOriginal, out planFeedback);
                    appliedPlan = changed;
                    if (changed || string.IsNullOrWhiteSpace(originalScheme)) originalScheme = detectedOriginal;
                }
                catch (Exception ex) { planFeedback = "No se pudo cambiar el plan: " + ex.Message; }
            }
            else if (previous.BalancedEnergy)
            {
                try
                {
                    if (!string.IsNullOrWhiteSpace(previous.OriginalPowerScheme))
                        PowerPlanService.RestoreIfBalanced(previous.OriginalPowerScheme);
                    originalScheme = "";
                    restoredPlan = true;
                }
                catch (Exception ex) { planFeedback = "La rutina se desactivó, pero Windows no restauró el plan: " + ex.Message; }
            }
            else if (!balanced && !string.IsNullOrWhiteSpace(originalScheme))
            {
                try { PowerPlanService.RestoreIfBalanced(originalScheme); originalScheme = ""; }
                catch (Exception ex) { planFeedback = "Windows aún no restauró el plan anterior: " + ex.Message; }
            }

            var next = previous with
            {
                Enabled = enabled,
                IntervalDays = interval,
                RunAt = runAt,
                BalancedEnergy = enabled && balanced,
                OriginalPowerScheme = (enabled && balanced) || !string.IsNullOrWhiteSpace(planFeedback) ? originalScheme : "",
                LastResult = string.IsNullOrWhiteSpace(planFeedback) ? previous.LastResult : planFeedback
            };
            MaintenanceScheduler.Configure(_profile, next);
            _maintenance = next;
            if (!SaveProfile()) throw new IOException(_profileNote);
            ShowPage("Automático");
            string status = enabled
                ? $"Rutina programada cada {interval} día(s), a las {runAt}. El equipo debe estar despierto y tu sesión iniciada."
                : "La rutina se desactivó. Si seguía activo, se restauró el plan de energía anterior.";
            if (enabled && balanced && string.IsNullOrWhiteSpace(originalScheme))
                status += " Se conservó el plan actual de ahorro o personalizado.";
            if (!string.IsNullOrWhiteSpace(planFeedback)) status += " " + planFeedback;
            MessageBox.Show(this, status, "Nebula Optimizer", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            try
            {
                if (appliedPlan) PowerPlanService.RestoreIfBalanced(originalScheme);
                else if (restoredPlan && previous.BalancedEnergy) _ = PowerPlanService.TryApplyBalanced(out _, out _);
                MaintenanceScheduler.Configure(_profile, previous);
            }
            catch { }
            _maintenance = previous;
            _ = SaveProfile();
            MessageBox.Show(this, ex.Message, "No se pudo guardar la rutina", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void BuildSecurity(FlowLayoutPanel flow)
    {
        Title(flow, "Privacidad y seguridad", "La aplicación comprueba sus propios límites y mantiene el trabajo en tu equipo.");
        var checks = CheckRows();
        var card = Card(72 + checks.Count * 66);
        card.Controls.Add(TextLabel("Comprobaciones del optimizador", 20, 17, Theme.Ink, 14, true));
        int y = 56;
        foreach (var item in checks)
        {
            var badge = new Label
            {
                Text = item.Good ? "✓" : "!", Location = new Point(22, y + 2), Size = new Size(30, 30),
                TextAlign = ContentAlignment.MiddleCenter, ForeColor = item.Good ? Theme.TealDark : Theme.Amber,
                BackColor = item.Good ? Theme.Mint : Theme.AmberPale, Font = new Font("Segoe UI", 12, FontStyle.Bold)
            };
            card.Controls.Add(badge);
            card.Controls.Add(TextLabel(item.Title, 66, y, Theme.Ink, 10, true));
            var detail = BoxLabel(item.Detail, 66, y + 22, 800, 36);
            detail.Font = new Font("Segoe UI", 8.5F);
            card.Controls.Add(detail);
            y += 66;
        }
        flow.Controls.Add(card);
        var info = Card(127);
        info.Controls.Add(TextLabel("Datos locales, sin envío", 20, 17, Theme.Ink, 12, true));
        info.Controls.Add(BoxLabel($"El perfil se guarda en {ShortPath(_profile.PathName)}. Esta versión no inicia conexiones de red ni envía telemetría.", 20, 48, 860, 48));
        flow.Controls.Add(info);
    }

    private List<CheckRow> CheckRows()
    {
        bool admin = false;
        try { admin = new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator); } catch { }
        bool local = IsLocal(_profile.Folder);
        bool tempOk;
        string tempInfo;
        try { tempInfo = SafeCleanup.CheckedRoot(); tempOk = true; }
        catch (Exception ex) { tempInfo = ex.Message; tempOk = false; }
        return
        [
            new("Perfil privado", _profileNote, _profileSecure),
            new("Ruta local", local ? "El perfil está bajo AppData del usuario en un disco fijo." : "La ruta del perfil no parece local.", local),
            new("Permisos mínimos", admin ? "La aplicación está elevada. No requiere permisos de administrador; ábrela como usuario normal." : "Se ejecuta sin privilegios de administrador.", !admin),
            new("Alcance de limpieza", tempOk ? $"Carpeta temporal validada: {tempInfo}" : tempInfo, tempOk),
            new("Mantenimiento automático", _maintenance.Enabled ? $"Programado cada {_maintenance.IntervalDays} día(s) a las {_maintenance.RunAt}; requiere sesión iniciada." : "Desactivado. Puedes configurarlo desde Automático.", true),
            new("Sin envío de datos", "No hay funciones de red ni telemetría en esta versión.", true)
        ];
    }

    private void BuildEquipment(FlowLayoutPanel flow)
    {
        Title(flow, "Tu equipo", "Ficha técnica actual y archivo .env personal.");
        var card = Card(323);
        card.Controls.Add(TextLabel("Identificación y sistema", 20, 16, Theme.Ink, 13, true));
        Pair(card, "Nombre del equipo", _device.Name, 22, 57, 170);
        Pair(card, "Sistema operativo", _device.Os, 22, 100, 170);
        Pair(card, "Arquitectura", _device.Architecture, 22, 143, 170);
        Pair(card, "Procesador", _device.Cpu, 22, 186, 170);
        Pair(card, "Procesadores lógicos", _device.Threads.ToString(CultureInfo.InvariantCulture), 22, 229, 170);
        Pair(card, "Memoria física", _device.MemoryText, 500, 57, 150);
        Pair(card, "Capturado", _device.Captured.ToString("yyyy-MM-dd HH:mm"), 500, 100, 150);
        flow.Controls.Add(card);

        int diskHeight = Math.Max(110, 62 + _device.Drives.Count * 40);
        var disks = Card(diskHeight);
        disks.Controls.Add(TextLabel("Discos locales fijos", 20, 16, Theme.Ink, 12, true));
        int y = 52;
        foreach (var drive in _device.Drives)
        {
            Pair(disks, drive.Name, $"{drive.FreeText} libres de {drive.TotalText} ({drive.FreePercent}% libre)", 22, y, 140);
            y += 38;
        }
        if (_device.Drives.Count == 0) disks.Controls.Add(BoxLabel("No se detectaron discos fijos disponibles.", 22, 54, 600, 24));
        flow.Controls.Add(disks);

        var save = Primary("Actualizar ficha y archivo .env", 250);
        save.Click += (_, _) =>
        {
            _device = DeviceInfo.Capture();
            SaveProfile();
            ShowPage("Equipo");
            MessageBox.Show(this, _profileNote, "Ficha actualizada", MessageBoxButtons.OK, MessageBoxIcon.Information);
        };
        flow.Controls.Add(save);
        Footnote(flow, "Se omiten el usuario de Windows, números de serie, direcciones MAC y claves de producto.");
    }

    private void BuildSettings(FlowLayoutPanel flow)
    {
        Title(flow, "Ajustes del perfil", "Elige un nombre corto para tu archivo local.");
        var card = Card(245);
        card.Controls.Add(TextLabel("Nombre del perfil", 20, 18, Theme.Ink, 12, true));
        card.Controls.Add(BoxLabel("Se convierte en un archivo .env dentro de AppData. Letras, números, guion y guion bajo.", 20, 49, 850, 25));
        var input = new TextBox { Text = _profile.Name, Location = new Point(22, 89), Width = 350, Font = new Font("Segoe UI", 11) };
        card.Controls.Add(input);
        var save = Primary("Guardar perfil", 148);
        save.Location = new Point(390, 86);
        save.Click += (_, _) =>
        {
            var previous = _profile;
            var next = new EnvProfile(EnvProfile.Normalize(input.Text));
            try
            {
                MaintenanceScheduler.Configure(next, _maintenance);
                _profile = next;
                if (!SaveProfile()) throw new IOException(_profileNote);
            }
            catch (Exception ex)
            {
                _profile = previous;
                try { MaintenanceScheduler.Configure(_profile, _maintenance); } catch { }
                MessageBox.Show(this, ex.Message, "No se pudo actualizar el perfil", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            MessageBox.Show(this, $"Ficha guardada en:\\n{_profile.PathName}\\n\\nSi cambiaste el nombre, el archivo anterior se conserva.",
                "Perfil guardado", MessageBoxButtons.OK, MessageBoxIcon.Information);
            ShowPage("Ajustes");
        };
        card.Controls.Add(save);
        card.Controls.Add(BoxLabel("El contenido es texto legible. La carpeta se restringe a tu usuario de Windows cuando el sistema lo permite; revisa el archivo antes de compartirlo.", 22, 142, 850, 54));
        flow.Controls.Add(card);

        var data = Card(140);
        data.Controls.Add(TextLabel("Qué se guarda", 20, 17, Theme.Ink, 12, true));
        data.Controls.Add(BoxLabel("Nombre del equipo, Windows, arquitectura, modelo del procesador, memoria física, procesadores lógicos, capacidad y espacio libre de discos fijos y fecha de captura.", 20, 48, 850, 42));
        var safe = TextLabel("Sin contraseñas, claves de producto, números de serie ni MAC.", 20, 98, Theme.TealDark, 9, true);
        data.Controls.Add(safe);
        flow.Controls.Add(data);
        Footnote(flow, $"Archivo actual: {_profile.PathName}");
    }

    private bool SaveProfile()
    {
        try
        {
            _profileSecure = _profile.Save(_device, _maintenance, out _profileNote);
            _status.Text = _profileSecure ? "●  Perfil local listo" : "●  Revisa permisos del perfil";
            _status.ForeColor = _profileSecure ? Theme.TealDark : Theme.Amber;
            return true;
        }
        catch (Exception ex)
        {
            _profileSecure = false;
            _profileNote = ex.Message;
            _status.Text = "●  Error al guardar perfil";
            _status.ForeColor = Theme.Amber;
            return false;
        }
    }

    private string MainDriveFree()
    {
        string system = Path.GetPathRoot(Environment.SystemDirectory)?.TrimEnd('\\') ?? "";
        return _device.Drives.FirstOrDefault(d => string.Equals(d.Name, system, StringComparison.OrdinalIgnoreCase))?.FreeText ?? "No disponible";
    }

    private static bool IsLocal(string path)
    {
        try
        {
            string root = Path.GetPathRoot(Path.GetFullPath(path)) ?? "";
            return root.Length > 1 && root[1] == ':' && new DriveInfo(root).DriveType == DriveType.Fixed;
        }
        catch { return false; }
    }

    private static string ShortPath(string path)
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return path.StartsWith(home, StringComparison.OrdinalIgnoreCase) ? "%USERPROFILE%" + path[home.Length..] : path;
    }

    private static void Title(FlowLayoutPanel flow, string title, string subtitle)
    {
        var panel = new Panel { Height = 72, Width = 900, Tag = "wide", Margin = new Padding(0, 0, 0, 11) };
        panel.Controls.Add(TextLabel(title, 0, 0, Theme.Ink, 21, true));
        panel.Controls.Add(new Label { Text = subtitle, Location = new Point(2, 41), AutoSize = true, ForeColor = Theme.Muted, Font = new Font("Segoe UI", 9.5F) });
        flow.Controls.Add(panel);
    }

    private static Panel Card(int height) => new()
    {
        Width = 900, Height = height, Tag = "wide", BackColor = Color.White,
        BorderStyle = BorderStyle.FixedSingle, Margin = new Padding(0, 0, 0, 13)
    };

    private static Label TextLabel(string text, int x, int y, Color color, float size, bool bold) =>
        new() { Text = text, Location = new Point(x, y), AutoSize = true, ForeColor = color, Font = new Font(bold ? "Segoe UI Semibold" : "Segoe UI", size, bold ? FontStyle.Bold : FontStyle.Regular) };

    private static Label BoxLabel(string text, int x, int y, int width, int height) =>
        new() { Text = text, Location = new Point(x, y), Size = new Size(width, height), ForeColor = Theme.Muted, Font = new Font("Segoe UI", 9.5F), AutoEllipsis = true };

    private static void Metric(Panel parent, string title, string value, int x, int y)
    {
        parent.Controls.Add(TextLabel(title.ToUpperInvariant(), x, y, Theme.Muted, 8, true));
        parent.Controls.Add(TextLabel(value, x, y + 26, Theme.Ink, 15, true));
    }

    private static void Pair(Panel parent, string title, string value, int x, int y, int offset)
    {
        parent.Controls.Add(TextLabel(title, x, y, Theme.Muted, 9, false));
        parent.Controls.Add(new Label { Text = value, Location = new Point(x + offset, y - 1), Size = new Size(310, 27), ForeColor = Theme.Ink, Font = new Font("Segoe UI Semibold", 9.5F, FontStyle.Bold), AutoEllipsis = true });
    }

    private static Button Primary(string text, int width)
    {
        var button = new Button { Text = text, Width = width, Height = 38, FlatStyle = FlatStyle.Flat, BackColor = Theme.Teal, ForeColor = Color.White, Font = new Font("Segoe UI", 9, FontStyle.Bold), Cursor = Cursors.Hand, UseVisualStyleBackColor = false };
        button.FlatAppearance.BorderSize = 0;
        button.FlatAppearance.MouseOverBackColor = Theme.TealDark;
        return button;
    }

    private static Button Secondary(string text, int width)
    {
        var button = new Button { Text = text, Width = width, Height = 36, FlatStyle = FlatStyle.Flat, BackColor = Color.White, ForeColor = Theme.Ink, Font = new Font("Segoe UI", 9, FontStyle.Bold), Cursor = Cursors.Hand, UseVisualStyleBackColor = false };
        button.FlatAppearance.BorderColor = Theme.Line;
        return button;
    }

    private static void Footnote(FlowLayoutPanel flow, string text) =>
        flow.Controls.Add(new Label { Text = text, Width = 900, Height = 28, Tag = "wide", ForeColor = Theme.Muted, Font = new Font("Segoe UI", 8.5F), Margin = new Padding(2, 2, 0, 10) });

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        string[] units = ["KB", "MB", "GB", "TB"];
        double value = bytes;
        int i = -1;
        do { value /= 1024; i++; } while (value >= 1024 && i < units.Length - 1);
        return $"{value:0.#} {units[i]}";
    }
}

