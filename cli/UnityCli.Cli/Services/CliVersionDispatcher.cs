#nullable enable
using System.Collections;
using System.Diagnostics;
using System.Runtime.InteropServices;
using UnityCli.Protocol;

namespace UnityCli.Cli.Services;

/// <summary>Thrown when the hand-off to another installed CLI could not be performed.</summary>
internal sealed class CliDispatchException : Exception
{
    public CliDispatchException(string message)
        : base(message)
    {
    }
}

/// <summary>
/// Everything the PROTOCOL_MISMATCH dispatch path needs from the outside world. Isolated behind an
/// interface so the decision logic in <see cref="CliDispatchPolicy"/> stays testable: the real
/// implementation replaces the current process, which no test can survive.
/// </summary>
internal interface ICliVersionDispatcher
{
    bool IsDispatchGuardSet { get; }

    IReadOnlyList<InstalledCliVersion> ListInstalledVersions();

    /// <summary>
    /// Hands the original argv to another installed CLI. On macOS/Linux this replaces the current
    /// process and never returns; on Windows it runs a child that inherits the standard streams and
    /// returns its exit code.
    /// </summary>
    /// <exception cref="CliDispatchException">The hand-off could not be performed.</exception>
    int Exec(string executablePath, string[] args);
}

internal sealed class ProcessCliVersionDispatcher : ICliVersionDispatcher
{
    public bool IsDispatchGuardSet => CliInstallLayout.IsDispatchGuardSet();

    public IReadOnlyList<InstalledCliVersion> ListInstalledVersions() => CliInstallLayout.ListInstalled();

    public int Exec(string executablePath, string[] args)
    {
        Console.Out.Flush();
        Console.Error.Flush();

        return OperatingSystem.IsWindows()
            ? RunChildProcess(executablePath, args)
            : ReplaceProcess(executablePath, args);
    }

    /// <summary>
    /// Windows has no execv, so spawn a child. No redirection: the child inherits stdin/stdout/stderr
    /// handles directly, and we propagate its exit code so agents still read a truthful status.
    /// </summary>
    private static int RunChildProcess(string executablePath, string[] args)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            UseShellExecute = false,
            CreateNoWindow = false,
        };

        foreach (string arg in args)
        {
            startInfo.ArgumentList.Add(arg);
        }

        startInfo.Environment[CliInstallLayout.DispatchGuardEnvironmentVariable] = "1";

        Process? process;
        try
        {
            process = Process.Start(startInfo);
        }
        catch (Exception exception)
        {
            throw new CliDispatchException(exception.Message);
        }

        if (process is null)
        {
            throw new CliDispatchException("Process.Start returned no process.");
        }

        using (process)
        {
            process.WaitForExit();
            return process.ExitCode;
        }
    }

    /// <summary>
    /// execve replaces the process image, so the exit code and the standard streams pass through
    /// untouched. We pass an explicit environment block because .NET keeps its own managed copy of
    /// the environment on Unix and never writes the guard variable back to the native `environ`.
    /// </summary>
    private static int ReplaceProcess(string executablePath, string[] args)
    {
        IntPtr pathPointer = IntPtr.Zero;
        IntPtr[]? argv = null;
        IntPtr[]? envp = null;

        try
        {
            pathPointer = Marshal.StringToCoTaskMemUTF8(executablePath);
            argv = BuildArgumentVector(executablePath, args);
            envp = BuildEnvironmentVector();

            Execve(pathPointer, argv, envp);

            // execve only returns on failure.
            int errno = Marshal.GetLastWin32Error();
            throw new CliDispatchException("execve failed with errno " + errno + ".");
        }
        finally
        {
            FreeVector(argv);
            FreeVector(envp);
            if (pathPointer != IntPtr.Zero)
            {
                Marshal.ZeroFreeCoTaskMemUTF8(pathPointer);
            }
        }
    }

    private static IntPtr[] BuildArgumentVector(string executablePath, string[] args)
    {
        var argv = new IntPtr[args.Length + 2];
        argv[0] = Marshal.StringToCoTaskMemUTF8(executablePath);
        for (int i = 0; i < args.Length; i++)
        {
            argv[i + 1] = Marshal.StringToCoTaskMemUTF8(args[i]);
        }

        argv[argv.Length - 1] = IntPtr.Zero;
        return argv;
    }

    private static IntPtr[] BuildEnvironmentVector()
    {
        var entries = new List<string>();
        foreach (DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            if (entry.Key is not string key || key.Length == 0)
            {
                continue;
            }

            if (string.Equals(key, CliInstallLayout.DispatchGuardEnvironmentVariable, StringComparison.Ordinal))
            {
                continue;
            }

            entries.Add(key + "=" + (entry.Value as string ?? string.Empty));
        }

        entries.Add(CliInstallLayout.DispatchGuardEnvironmentVariable + "=1");

        var envp = new IntPtr[entries.Count + 1];
        for (int i = 0; i < entries.Count; i++)
        {
            envp[i] = Marshal.StringToCoTaskMemUTF8(entries[i]);
        }

        envp[envp.Length - 1] = IntPtr.Zero;
        return envp;
    }

    private static void FreeVector(IntPtr[]? vector)
    {
        if (vector is null)
        {
            return;
        }

        foreach (IntPtr pointer in vector)
        {
            if (pointer != IntPtr.Zero)
            {
                Marshal.ZeroFreeCoTaskMemUTF8(pointer);
            }
        }
    }

    [DllImport("libc", EntryPoint = "execve", SetLastError = true)]
    private static extern int Execve(IntPtr path, IntPtr[] argv, IntPtr[] envp);
}
