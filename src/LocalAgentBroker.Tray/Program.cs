using LocalAgentBroker.Shared;

namespace LocalAgentBroker.Tray;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        try
        {
            (string repositoryRoot, string stateRoot, bool showHistory) = ParseArguments(args);
            string mutexName = BrokerUiInstanceName.Create(stateRoot);
            using var instanceMutex = new Mutex(initiallyOwned: true, mutexName, out bool createdNew);
            if (!createdNew)
                return 0;

            ApplicationConfiguration.Initialize();
            try
            {
                Application.Run(new TrayApplicationContext(repositoryRoot, stateRoot, showHistory));
            }
            finally
            {
                instanceMutex.ReleaseMutex();
            }
            return 0;
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                exception.Message,
                "Local Agent Broker",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return 1;
        }
    }

    private static (string RepositoryRoot, string StateRoot, bool ShowHistory) ParseArguments(string[] args)
    {
        string? repositoryRoot = null;
        string? stateRoot = null;
        bool showHistory = false;
        for (int index = 0; index < args.Length; index++)
        {
            string argument = args[index];
            if (string.Equals(argument, "--show-history", StringComparison.OrdinalIgnoreCase))
            {
                showHistory = true;
                continue;
            }

            if (++index >= args.Length || string.IsNullOrWhiteSpace(args[index]))
                throw new ArgumentException($"{argument} requires a value.");
            if (string.Equals(argument, "--repo-root", StringComparison.OrdinalIgnoreCase))
                repositoryRoot = args[index];
            else if (string.Equals(argument, "--state-root", StringComparison.OrdinalIgnoreCase))
                stateRoot = args[index];
            else
                throw new ArgumentException($"Unknown tray argument '{argument}'.");
        }

        if (string.IsNullOrWhiteSpace(repositoryRoot))
            throw new ArgumentException("The tray companion requires --repo-root <path>.");
        string root = Path.GetFullPath(repositoryRoot);
        if (!Directory.Exists(root))
            throw new DirectoryNotFoundException($"Repository root '{root}' does not exist.");
        stateRoot ??= Path.Combine(root, ".local-agent-broker");
        return (root, Path.GetFullPath(stateRoot), showHistory);
    }
}
