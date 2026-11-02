using System.Diagnostics;
using LocalAgentBroker.Shared;

namespace LocalAgentBroker;

/// <summary>Starts the published Windows tray companion when it is not already running.</summary>
internal static class BrokerUiLauncher
{
    public static void EnsureStarted(string repositoryRoot, string stateRoot)
    {
        if (!OperatingSystem.IsWindows())
            return;

        try
        {
            if (IsRunning(stateRoot))
                return;
            string trayDirectory = Path.Combine(AppContext.BaseDirectory, "tray");
            string executablePath = Path.Combine(
                trayDirectory,
                "LocalAgentBroker.Tray.exe");
            string assemblyPath = Path.Combine(
                trayDirectory,
                "LocalAgentBroker.Tray.dll");

            ProcessStartInfo? startInfo = null;
            if (File.Exists(executablePath))
            {
                startInfo = new ProcessStartInfo(executablePath);
            }
            else if (File.Exists(assemblyPath))
            {
                startInfo = new ProcessStartInfo("dotnet");
                startInfo.ArgumentList.Add(assemblyPath);
            }

            if (startInfo is null)
                return;

            startInfo.ArgumentList.Add("--repo-root");
            startInfo.ArgumentList.Add(repositoryRoot);
            startInfo.ArgumentList.Add("--state-root");
            startInfo.ArgumentList.Add(stateRoot);
            startInfo.UseShellExecute = false;
            startInfo.CreateNoWindow = true;
            startInfo.WorkingDirectory = repositoryRoot;
            Process.Start(startInfo)?.Dispose();
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception
            or IOException
            or UnauthorizedAccessException
            or InvalidOperationException)
        {
            // Tray startup is supplemental and must never strand an accepted API run.
        }
    }

    private static bool IsRunning(string stateRoot)
    {
        try
        {
            if (!Mutex.TryOpenExisting(BrokerUiInstanceName.Create(stateRoot), out Mutex? mutex))
                return false;
            mutex.Dispose();
            return true;
        }
        catch (WaitHandleCannotBeOpenedException)
        {
            return false;
        }
    }
}
