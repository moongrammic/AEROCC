using System.Diagnostics;
using System.IO;
using System.Management;
using System.Reflection;
using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Win32;

namespace AEROCC.Core.Wmi;

/// <summary>
/// Ensures the Gigabyte ACPI WMI MOF schema (<c>GB_WMIACPI_*</c>) is materialized in ProgramData,
/// compiled into <c>root\WMI</c>, and registered under WMI autorecovery so it survives reboots
/// without requiring Gigabyte Control Center.
/// </summary>
public static class GigabyteWmiMofRegistrar
{
    private const string WmiNamespace = @"\\.\root\WMI";
    private const string EmbeddedResourceSuffix = "Wmi.GB_WMIACPI.mof";
    private const string MutexName = @"Global\AEROCC_WMI_MOF_REG";
    private const string CimomRegistrySubKey = @"SOFTWARE\Microsoft\WBEM\CIMOM";
    private const string AutorecoverValueName = "Autorecover MOFs";
    private const int MofCompTimeoutMs = 30_000;

    private static readonly TimeSpan MutexTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan RetryCooldown = TimeSpan.FromSeconds(60);

    private static readonly string[] RequiredGetMethods =
    [
        "getCpuTemp",
        "getRpm1",
        "getRpm2",
        "GetChargeStop",
        "GetPEG2orSG2",
        "GetPowerOnTime",
        "GetFanIndexValue"
    ];

    private static readonly string[] RequiredSetMethods =
    [
        "SetFanModeNotify",
        "SetFanIndexValue",
        "SetChargeStop",
        "SetPEG2orSG2",
        "SetApuParameter1"
    ];

    private static readonly object SyncLock = new();
    private static volatile bool _verifiedThisProcess;
    private static DateTime _lastForceAttemptUtc = DateTime.MinValue;
    private static bool _lastForceResult;
    private static DateTime _lastFailedAttemptUtc = DateTime.MinValue;

    /// <summary>
    /// Fixed machine-wide MOF path registered with WMI <c>#pragma autorecover</c>.
    /// </summary>
    public static string InstalledMofPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "AEROCC",
        "GB_WMIACPI.mof");

    /// <summary>
    /// Fast-path per-process check/registration intended for lazy WMI connection initialization.
    /// </summary>
    public static bool EnsureRegisteredOnce()
    {
        if (_verifiedThisProcess)
        {
            return true;
        }

        return EnsureRegistered(force: false);
    }

    /// <summary>
    /// Verifies that <c>GB_WMIACPI_*</c> classes, ProgramData MOF file, and WMI autorecovery entry
    /// are present and current, compiling the embedded MOF via <c>mofcomp.exe</c> when elevated.
    /// </summary>
    public static bool EnsureRegistered(bool force = false)
    {
        lock (SyncLock)
        {
            DateTime now = DateTime.UtcNow;
            if (force)
            {
                if (_lastForceAttemptUtc != DateTime.MinValue &&
                    (now - _lastForceAttemptUtc) < RetryCooldown)
                {
                    return _lastForceResult;
                }
            }
            else
            {
                if (_verifiedThisProcess)
                {
                    return true;
                }

                if (_lastFailedAttemptUtc != DateTime.MinValue &&
                    (now - _lastFailedAttemptUtc) < RetryCooldown)
                {
                    return false;
                }
            }

            bool result = ExecuteSynchronizedRegistration(force);
            DateTime completedAt = DateTime.UtcNow;

            if (force)
            {
                _lastForceAttemptUtc = completedAt;
                _lastForceResult = result;
            }

            if (result)
            {
                _lastFailedAttemptUtc = DateTime.MinValue;
            }
            else
            {
                _lastFailedAttemptUtc = completedAt;
            }

            _verifiedThisProcess = result;
            return result;
        }
    }

    private static bool ExecuteSynchronizedRegistration(bool force)
    {
        using Mutex? crossProcessMutex = TryOpenCrossProcessMutex();
        bool mutexAcquired = false;

        try
        {
            if (crossProcessMutex != null)
            {
                try
                {
                    mutexAcquired = crossProcessMutex.WaitOne(MutexTimeout);
                }
                catch (AbandonedMutexException)
                {
                    mutexAcquired = true;
                }
            }

            return EnsureRegisteredCore(force);
        }
        finally
        {
            if (mutexAcquired && crossProcessMutex != null)
            {
                try
                {
                    crossProcessMutex.ReleaseMutex();
                }
                catch (ApplicationException)
                {
                    // Ignore release mismatch if mutex ownership changed unexpectedly.
                }
            }
        }
    }

    private static Mutex? TryOpenCrossProcessMutex()
    {
        try
        {
            return new Mutex(false, MutexName);
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
        catch
        {
            return null;
        }
    }

    private static bool EnsureRegisteredCore(bool force)
    {
        byte[]? embeddedBytes = LoadEmbeddedMofBytes();
        if (embeddedBytes == null)
        {
            GigabyteWmiClient.LogDebug("[WMI MOF] Embedded GB_WMIACPI.mof resource not found.");
            return IsWmiSchemaComplete();
        }

        string mofPath = InstalledMofPath;
        bool isAdmin = GigabyteWmiClient.IsAdministrator();
        bool schemaComplete = !force && IsWmiSchemaComplete();
        bool fileCurrent = IsInstalledMofUpToDate(mofPath, embeddedBytes);
        bool autorecoverRegistered = IsAutorecoverRegistered(mofPath);

        if (schemaComplete && fileCurrent && autorecoverRegistered)
        {
            if (isAdmin)
            {
                // Enforce ACL hardening even when MOF file and schema already exist
                try
                {
                    string? dir = Path.GetDirectoryName(mofPath);
                    if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                    {
                        if (!SecureDirectoryPermissions(new DirectoryInfo(dir)))
                        {
                            GigabyteWmiClient.LogDebug($"[WMI MOF] Existing directory ACL hardening failed for {dir}");
                            return false;
                        }
                    }
                    if (!File.Exists(mofPath) || !SecureFilePermissions(mofPath))
                    {
                        GigabyteWmiClient.LogDebug($"[WMI MOF] Existing MOF ACL hardening failed for {mofPath}");
                        return false;
                    }
                }
                catch (Exception ex)
                {
                    GigabyteWmiClient.LogDebug($"[WMI MOF] ACL verification check failed: {ex.Message}");
                    return false;
                }
            }
            return true;
        }

        if (!isAdmin)
        {
            if (schemaComplete)
            {
                GigabyteWmiClient.LogDebug(
                    "[WMI MOF] root\\WMI schema present (non-admin process; skipping ProgramData/Autorecover update).");
                return true;
            }

            GigabyteWmiClient.LogDebug(
                "[WMI MOF] root\\WMI GB_WMIACPI_* classes missing and process is not elevated; mofcomp skipped.");
            return false;
        }

        if (!TryWriteMofFile(mofPath, embeddedBytes, fileCurrent))
        {
            return false;
        }

        if (!RunMofComp(mofPath))
        {
            return false;
        }

        if (!IsWmiSchemaComplete())
        {
            GigabyteWmiClient.LogDebug("[WMI MOF] mofcomp succeeded, but WMI schema verification failed.");
            return false;
        }

        if (!IsAutorecoverRegistered(mofPath))
        {
            GigabyteWmiClient.LogDebug(
                $"[WMI MOF] Warning: {mofPath} compiled cleanly, but was not listed under HKLM\\{CimomRegistrySubKey}\\{AutorecoverValueName}.");
        }
        else
        {
            GigabyteWmiClient.LogDebug($"[WMI MOF] Registered {mofPath} in root\\WMI and Autorecover MOFs.");
        }

        return true;
    }

    private static byte[]? LoadEmbeddedMofBytes()
    {
        Assembly assembly = typeof(GigabyteWmiMofRegistrar).Assembly;
        string? resourceName = assembly
            .GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith(EmbeddedResourceSuffix, StringComparison.OrdinalIgnoreCase));

        if (resourceName == null)
        {
            return null;
        }

        using Stream? stream = assembly.GetManifestResourceStream(resourceName);
        if (stream == null)
        {
            return null;
        }

        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }

    private static bool IsInstalledMofUpToDate(string mofPath, byte[] expectedBytes)
    {
        try
        {
            if (!File.Exists(mofPath))
            {
                return false;
            }

            byte[] existing = File.ReadAllBytes(mofPath);
            return existing.AsSpan().SequenceEqual(expectedBytes);
        }
        catch
        {
            return false;
        }
    }

    private static bool TryWriteMofFile(string mofPath, byte[] expectedBytes, bool alreadyCurrent)
    {
        try
        {
            string? dir = Path.GetDirectoryName(mofPath);
            if (!string.IsNullOrEmpty(dir))
            {
                var dirInfo = Directory.CreateDirectory(dir);
                if (!SecureDirectoryPermissions(dirInfo))
                {
                    GigabyteWmiClient.LogDebug($"[WMI MOF] Aborting registration: failed to secure directory ACL for {dir}");
                    return false;
                }
            }

            if (!alreadyCurrent)
            {
                File.WriteAllBytes(mofPath, expectedBytes);
            }

            if (!File.Exists(mofPath) || !SecureFilePermissions(mofPath))
            {
                GigabyteWmiClient.LogDebug($"[WMI MOF] Aborting registration: failed to secure file ACL for {mofPath}");
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            GigabyteWmiClient.LogDebug($"[WMI MOF] Failed to write or secure {mofPath}: {ex.Message}");
            return false;
        }
    }

    private static bool SecureDirectoryPermissions(DirectoryInfo dirInfo)
    {
        try
        {
            var security = new DirectorySecurity();
            security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
            security.AddAccessRule(new FileSystemAccessRule(
                new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
                FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None,
                AccessControlType.Allow));
            security.AddAccessRule(new FileSystemAccessRule(
                new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
                FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None,
                AccessControlType.Allow));
            security.AddAccessRule(new FileSystemAccessRule(
                new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null),
                FileSystemRights.ReadAndExecute,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None,
                AccessControlType.Allow));

            dirInfo.SetAccessControl(security);
            return true;
        }
        catch (Exception ex)
        {
            GigabyteWmiClient.LogDebug($"[WMI MOF] Could not set explicit directory ACLs: {ex.Message}");
            return false;
        }
    }

    private static bool SecureFilePermissions(string filePath)
    {
        try
        {
            var fileInfo = new FileInfo(filePath);
            var security = new FileSecurity();
            security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
            security.AddAccessRule(new FileSystemAccessRule(
                new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
                FileSystemRights.FullControl,
                AccessControlType.Allow));
            security.AddAccessRule(new FileSystemAccessRule(
                new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
                FileSystemRights.FullControl,
                AccessControlType.Allow));
            security.AddAccessRule(new FileSystemAccessRule(
                new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null),
                FileSystemRights.ReadAndExecute,
                AccessControlType.Allow));

            fileInfo.SetAccessControl(security);
            return true;
        }
        catch (Exception ex)
        {
            GigabyteWmiClient.LogDebug($"[WMI MOF] Could not set explicit file ACLs: {ex.Message}");
            return false;
        }
    }

    private static bool IsAutorecoverRegistered(string mofPath)
    {
        try
        {
            string expectedFull = Path.GetFullPath(mofPath);
            using var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var cimom = hklm.OpenSubKey(CimomRegistrySubKey, writable: false);
            if (cimom?.GetValue(AutorecoverValueName) is not string[] entries)
            {
                return false;
            }

            foreach (string rawEntry in entries)
            {
                if (string.IsNullOrWhiteSpace(rawEntry))
                {
                    continue;
                }

                string expanded = Environment.ExpandEnvironmentVariables(rawEntry.Trim());
                try
                {
                    if (Path.GetFullPath(expanded).Equals(expectedFull, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
                catch (ArgumentException)
                {
                    // Skip malformed path entries in CIMOM registry list.
                }
            }

            return false;
        }
        catch (Exception ex)
        {
            GigabyteWmiClient.LogDebug($"[WMI MOF] Failed checking Autorecover MOFs: {ex.Message}");
            return false;
        }
    }

    private static bool IsWmiSchemaComplete()
    {
        try
        {
            var scope = new ManagementScope(WmiNamespace, GigabyteWmiClient.CreateOptions());
            scope.Connect();

            if (!HasClass(scope, "GB_WMIACPI_Data"))
            {
                return false;
            }

            if (!HasClassWithProperty(scope, "GB_WMIACPI_Event", "Data"))
            {
                return false;
            }

            if (!HasClassWithMethods(scope, "GB_WMIACPI_Get", RequiredGetMethods))
            {
                return false;
            }

            if (!HasClassWithMethods(scope, "GB_WMIACPI_Set", RequiredSetMethods))
            {
                return false;
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool HasClass(ManagementScope scope, string className)
    {
        using var mgmtClass = new ManagementClass(scope, new ManagementPath(className), null);
        mgmtClass.Get();
        return true;
    }

    private static bool HasClassWithProperty(ManagementScope scope, string className, string propertyName)
    {
        using var mgmtClass = new ManagementClass(scope, new ManagementPath(className), null);
        mgmtClass.Get();

        foreach (PropertyData prop in mgmtClass.Properties)
        {
            if (prop.Name.Equals(propertyName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasClassWithMethods(ManagementScope scope, string className, string[] requiredMethods)
    {
        using var mgmtClass = new ManagementClass(scope, new ManagementPath(className), null);
        mgmtClass.Get();

        var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (MethodData method in mgmtClass.Methods)
        {
            existing.Add(method.Name);
        }

        foreach (string required in requiredMethods)
        {
            if (!existing.Contains(required))
            {
                return false;
            }
        }

        return true;
    }

    private static string ResolveMofCompPath()
    {
        if (Environment.Is64BitOperatingSystem && !Environment.Is64BitProcess)
        {
            string sysnative = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                "Sysnative",
                "wbem",
                "mofcomp.exe");
            if (File.Exists(sysnative))
            {
                return sysnative;
            }
        }

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            "wbem",
            "mofcomp.exe");
    }

    private static bool RunMofComp(string mofPath)
    {
        string mofCompExe = ResolveMofCompPath();
        if (!File.Exists(mofCompExe))
        {
            GigabyteWmiClient.LogDebug($"[WMI MOF] mofcomp.exe not found at {mofCompExe}");
            return false;
        }

        var psi = new ProcessStartInfo
        {
            FileName = mofCompExe,
            Arguments = $"\"{mofPath}\"",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        try
        {
            using Process? process = Process.Start(psi);
            if (process == null)
            {
                GigabyteWmiClient.LogDebug("[WMI MOF] Failed to start mofcomp.exe process.");
                return false;
            }

            Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync();
            Task<string> stderrTask = process.StandardError.ReadToEndAsync();

            if (!process.WaitForExit(MofCompTimeoutMs))
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch
                {
                    // Best-effort cleanup after timeout.
                }

                GigabyteWmiClient.LogDebug($"[WMI MOF] mofcomp.exe timed out after {MofCompTimeoutMs} ms.");
                return false;
            }

            string stdout = stdoutTask.GetAwaiter().GetResult().Trim();
            string stderr = stderrTask.GetAwaiter().GetResult().Trim();

            if (process.ExitCode != 0)
            {
                GigabyteWmiClient.LogDebug(
                    $"[WMI MOF] mofcomp.exe failed (exit={process.ExitCode}). Out: {stdout} Err: {stderr}");
                return false;
            }

            GigabyteWmiClient.LogDebug($"[WMI MOF] mofcomp.exe succeeded (exit=0). {stdout}");
            return true;
        }
        catch (Exception ex)
        {
            GigabyteWmiClient.LogDebug($"[WMI MOF] mofcomp.exe exception: {ex.GetType().Name} - {ex.Message}");
            return false;
        }
    }
}
