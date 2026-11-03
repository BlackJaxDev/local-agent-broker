using System.Collections.ObjectModel;

namespace LocalAgentBroker;

/// <summary>
/// Resolves source-focused repository paths without permitting traversal,
/// reparse-point escape, generated output, or common secret containers.
/// </summary>
internal sealed class RepositoryPathPolicy
{
    private static readonly ReadOnlySet<string> s_blockedDirectoryNames = new(
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".git", ".hg", ".svn", ".vs", ".idea", ".local-agent-broker", "Build", "bin", "obj",
            "node_modules", "packages", ".secrets", "secrets",
        });

    private static readonly ReadOnlySet<string> s_blockedFileNames = new(
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "credentials.json", "secrets.json", "session.json", "NuGet.Config",
            "id_rsa", "id_ed25519",
        });

    private static readonly ReadOnlySet<string> s_blockedExtensions = new(
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".key", ".pem", ".pfx", ".p12", ".snk", ".kdbx",
        });

    private static readonly ReadOnlySet<string> s_allowedExtensions = new(
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".bat", ".c", ".cmd", ".comp", ".config", ".cpp", ".cs",
            ".cshtml", ".csproj", ".css", ".csv", ".editorconfig", ".frag",
            ".geom", ".gitignore", ".gitattributes", ".glsl", ".h", ".hpp",
            ".htm", ".html", ".hlsl", ".inl", ".js", ".json", ".jsonc",
            ".jsx", ".lock", ".md", ".natvis", ".nuspec", ".props", ".proto",
            ".ps1", ".psd1", ".psm1", ".py", ".razor", ".resx", ".ruleset",
            ".scss", ".sh", ".sln", ".slnx", ".sql", ".targets", ".tesc",
            ".tese", ".toml", ".ts", ".tsv", ".tsx", ".txt", ".vert",
            ".xaml", ".xml", ".yaml", ".yml",
        });

    private static readonly ReadOnlySet<string> s_allowedExtensionlessNames = new(
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".editorconfig", ".gitattributes", ".gitignore", "Dockerfile", "Makefile",
        });

    private static readonly ReadOnlySet<string> s_reservedDeviceNames = new(
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "CON", "PRN", "AUX", "NUL", "CLOCK$",
            "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
            "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
        });

    private readonly string _repositoryRoot;
    private readonly string _repositoryRootPrefix;
    private readonly string? _stateRoot;
    private readonly string? _sessionsRoot;
    private readonly HashSet<string> _additionalTextExtensions;
    private readonly HashSet<string> _additionalTextFileNames;
    private readonly string[] _excludedPaths;

    public RepositoryPathPolicy(
        string repositoryRoot,
        string? stateRoot = null,
        BrokerRepositoryOptions? options = null,
        string? sessionsRoot = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        _repositoryRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(repositoryRoot));
        if (!Directory.Exists(_repositoryRoot))
            throw new DirectoryNotFoundException("The configured repository root does not exist.");
        _repositoryRootPrefix = _repositoryRoot + Path.DirectorySeparatorChar;
        _stateRoot = stateRoot is null
            ? null
            : Path.TrimEndingDirectorySeparator(Path.GetFullPath(stateRoot));
        _sessionsRoot = sessionsRoot is null
            ? null
            : Path.TrimEndingDirectorySeparator(Path.GetFullPath(sessionsRoot));
        options ??= new BrokerRepositoryOptions();
        ArgumentNullException.ThrowIfNull(options.AdditionalTextExtensions);
        ArgumentNullException.ThrowIfNull(options.AdditionalTextFileNames);
        ArgumentNullException.ThrowIfNull(options.ExcludedPaths);
        _additionalTextExtensions = new(StringComparer.OrdinalIgnoreCase);
        foreach (string extension in options.AdditionalTextExtensions)
        {
            if (string.IsNullOrWhiteSpace(extension) || extension.Length < 2
                || extension[0] != '.'
                || extension[1..].Any(static character =>
                    !char.IsAsciiLetterOrDigit(character) && character is not '_' and not '-'))
                throw new ArgumentException("Additional text extensions must start with a period and contain only letters, digits, underscores, or hyphens.");
            _additionalTextExtensions.Add(extension);
        }
        _additionalTextFileNames = new(StringComparer.OrdinalIgnoreCase);
        foreach (string fileName in options.AdditionalTextFileNames)
        {
            if (string.IsNullOrWhiteSpace(fileName)
                || fileName != fileName.Trim()
                || fileName is "." or ".."
                || fileName.IndexOfAny(['/', '\\', ':', '*', '?']) >= 0
                || fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
                || fileName.EndsWith('.') || fileName.EndsWith(' '))
                throw new ArgumentException("Additional text file names must be ordinary file names without paths or wildcards.");
            _additionalTextFileNames.Add(fileName);
        }
        _excludedPaths = new string[options.ExcludedPaths.Count];
        for (int index = 0; index < _excludedPaths.Length; index++)
        {
            string relativePath = NormalizeRelativePath(options.ExcludedPaths[index], false, false);
            _excludedPaths[index] = Path.GetFullPath(Path.Combine(_repositoryRoot, relativePath));
        }
    }

    internal string RepositoryRoot => _repositoryRoot;

    public IReadOnlyList<string> ResolveAllowedRoots(IReadOnlyList<string> relativeRoots)
    {
        ArgumentNullException.ThrowIfNull(relativeRoots);
        var resolved = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string relativeRoot in relativeRoots)
        {
            string fullPath = ResolveDirectory(relativeRoot);
            resolved[ToRelativePath(fullPath)] = fullPath;
        }

        return resolved
            .OrderBy(static pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .Select(static pair => pair.Value)
            .ToArray();
    }

    public string ResolveDirectory(string relativePath)
    {
        string normalized = NormalizeRelativePath(relativePath, allowRepositoryRoot: true);
        string fullPath = normalized.Length == 0
            ? _repositoryRoot
            : ResolveContainedFullPath(normalized);
        if (!Directory.Exists(fullPath))
            throw new ArgumentException($"Repository directory '{DisplayPath(normalized)}' was not found.");

        EnsureNoReparsePoints(fullPath);
        return fullPath;
    }

    public string ResolveTextFile(
        string relativePath,
        IReadOnlyList<string>? allowedRootFullPaths = null)
    {
        string normalized = NormalizeRelativePath(relativePath, allowRepositoryRoot: false);
        EnsureTextFileNameAllowed(normalized);
        string fullPath = ResolveContainedFullPath(normalized);
        if (allowedRootFullPaths is not null
            && !allowedRootFullPaths.Any(root => IsSameOrDescendant(fullPath, root)))
        {
            throw new ArgumentException($"Repository file '{normalized}' is outside the authorized roots.");
        }
        if (!File.Exists(fullPath))
            throw new ArgumentException($"Repository file '{normalized}' was not found.");

        FileAttributes attributes = File.GetAttributes(fullPath);
        if ((attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
            throw new ArgumentException($"Repository path '{normalized}' is not an ordinary text file.");
        EnsureNoReparsePoints(fullPath);
        return fullPath;
    }

    /// <summary>
    /// Resolves an allowed repository text-file path which may not exist yet.
    /// Existing paths are required to be ordinary files. Every existing ancestor
    /// is checked so a later create cannot traverse a reparse point.
    /// </summary>
    public string ResolvePotentialTextFile(string relativePath)
    {
        string normalized = NormalizeRelativePath(relativePath, allowRepositoryRoot: false);
        EnsureTextFileNameAllowed(normalized);
        string fullPath = ResolveContainedFullPath(normalized);
        EnsureNoReparsePointsThroughExistingPath(fullPath);
        if (Directory.Exists(fullPath))
            throw new ArgumentException($"Repository path '{normalized}' is a directory, not a text file.");
        if (!File.Exists(fullPath))
            return fullPath;

        FileAttributes attributes = File.GetAttributes(fullPath);
        if ((attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
            throw new ArgumentException($"Repository path '{normalized}' is not an ordinary text file.");
        return fullPath;
    }

    public string ToRelativePath(string fullPath)
        => Path.GetRelativePath(_repositoryRoot, fullPath).Replace('\\', '/');

    public bool IsTextFileCandidate(string fullPath)
    {
        try
        {
            string relativePath = ToRelativePath(fullPath);
            if (relativePath.StartsWith("../", StringComparison.Ordinal)
                || string.Equals(relativePath, "..", StringComparison.Ordinal))
            {
                return false;
            }

            string normalized = NormalizeRelativePath(relativePath, allowRepositoryRoot: false);
            EnsureTextFileNameAllowed(normalized);
            FileAttributes attributes = File.GetAttributes(fullPath);
            return (attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) == 0;
        }
        catch (Exception exception) when (
            exception is ArgumentException
            or IOException
            or UnauthorizedAccessException
            or NotSupportedException)
        {
            return false;
        }
    }

    public bool IsDirectoryTraversalAllowed(string fullPath)
    {
        try
        {
            string relativePath = ToRelativePath(fullPath);
            if (relativePath.StartsWith("../", StringComparison.Ordinal)
                || string.Equals(relativePath, "..", StringComparison.Ordinal))
            {
                return false;
            }

            _ = NormalizeRelativePath(relativePath, allowRepositoryRoot: false);
            FileAttributes attributes = File.GetAttributes(fullPath);
            return (attributes & FileAttributes.Directory) != 0
                && (attributes & FileAttributes.ReparsePoint) == 0;
        }
        catch (Exception exception) when (
            exception is ArgumentException
            or IOException
            or UnauthorizedAccessException
            or NotSupportedException)
        {
            return false;
        }
    }

    public FileStream OpenReadValidated(string fullPath)
    {
        EnsureContained(fullPath);
        EnsureNoReparsePoints(fullPath);
        FileAttributes attributes = File.GetAttributes(fullPath);
        if ((attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
            throw new IOException("The repository path is not an ordinary file.");

        return new FileStream(
            fullPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 16_384,
            FileOptions.SequentialScan);
    }

    private string NormalizeRelativePath(string path, bool allowRepositoryRoot, bool applyExclusions = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!string.Equals(path, path.Trim(), StringComparison.Ordinal))
            throw new ArgumentException("Repository paths cannot have leading or trailing whitespace.");
        if (Path.IsPathRooted(path)
            || path.StartsWith('\\')
            || path.StartsWith('/')
            || path.Contains(':', StringComparison.Ordinal)
            || path.StartsWith("\\\\?\\", StringComparison.Ordinal)
            || path.StartsWith("\\\\.\\", StringComparison.Ordinal))
        {
            throw new ArgumentException("Repository paths must be ordinary repository-relative paths.");
        }

        string[] segments = path.Split(['\\', '/'], StringSplitOptions.None);
        if (allowRepositoryRoot && segments.Length == 1 && segments[0] == ".")
            return string.Empty;
        if (segments.Length == 0 || segments.Any(static segment => segment.Length == 0))
            throw new ArgumentException("Repository paths cannot contain empty segments.");

        foreach (string segment in segments)
        {
            if (segment is "." or "..")
                throw new ArgumentException("Repository paths cannot contain traversal segments.");
            if (segment.EndsWith(' ') || segment.EndsWith('.'))
                throw new ArgumentException("Repository path segments cannot end in a space or period.");
            if (segment.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
                || segment.Contains('*')
                || segment.Contains('?'))
            {
                throw new ArgumentException("Repository paths contain an invalid character.");
            }

            string deviceName = Path.GetFileNameWithoutExtension(segment);
            if (s_reservedDeviceNames.Contains(deviceName))
                throw new ArgumentException("Repository paths cannot use reserved device names.");
            if (applyExclusions && s_blockedDirectoryNames.Contains(segment))
                throw new ArgumentException($"Repository path segment '{segment}' is excluded.");
        }

        string normalized = string.Join(Path.DirectorySeparatorChar, segments);
        if (applyExclusions && _stateRoot is not null
            && IsSameOrDescendant(Path.Combine(_repositoryRoot, normalized), _stateRoot))
        {
            throw new ArgumentException("Broker state is excluded from repository access.");
        }
        if (applyExclusions)
        {
            string fullPath = Path.Combine(_repositoryRoot, normalized);
            if (_sessionsRoot is not null && IsSameOrDescendant(fullPath, _sessionsRoot))
                throw new ArgumentException("Session storage is excluded from repository access.");
            foreach (string excludedPath in _excludedPaths)
                if (IsSameOrDescendant(fullPath, excludedPath))
                    throw new ArgumentException("Repository path is excluded by the workspace configuration.");
        }

        return normalized;
    }

    private string ResolveContainedFullPath(string normalizedRelativePath)
    {
        string fullPath = Path.GetFullPath(Path.Combine(_repositoryRoot, normalizedRelativePath));
        EnsureContained(fullPath);
        return fullPath;
    }

    private void EnsureContained(string fullPath)
    {
        string normalized = Path.GetFullPath(fullPath);
        if (!string.Equals(normalized, _repositoryRoot, StringComparison.OrdinalIgnoreCase)
            && !normalized.StartsWith(_repositoryRootPrefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Repository path escaped the configured repository root.");
        }
    }

    private void EnsureNoReparsePoints(string fullPath)
    {
        EnsureContained(fullPath);
        string relativePath = Path.GetRelativePath(_repositoryRoot, fullPath);
        if (relativePath == ".")
            return;

        string current = _repositoryRoot;
        foreach (string segment in relativePath.Split(Path.DirectorySeparatorChar))
        {
            current = Path.Combine(current, segment);
            FileAttributes attributes = File.GetAttributes(current);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new ArgumentException(
                    $"Repository path '{ToRelativePath(current)}' crosses a reparse point.");
            }
        }
    }

    private void EnsureNoReparsePointsThroughExistingPath(string fullPath)
    {
        EnsureContained(fullPath);
        string relativePath = Path.GetRelativePath(_repositoryRoot, fullPath);
        if (relativePath == ".")
            return;

        string current = _repositoryRoot;
        foreach (string segment in relativePath.Split(Path.DirectorySeparatorChar))
        {
            current = Path.Combine(current, segment);
            if (!File.Exists(current) && !Directory.Exists(current))
                return;

            FileAttributes attributes = File.GetAttributes(current);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new ArgumentException(
                    $"Repository path '{ToRelativePath(current)}' crosses a reparse point.");
            }
        }
    }

    private static bool IsSameOrDescendant(string candidate, string root)
    {
        string normalizedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        string prefix = normalizedRoot + Path.DirectorySeparatorChar;
        string normalizedCandidate = Path.GetFullPath(candidate);
        return string.Equals(normalizedCandidate, normalizedRoot, StringComparison.OrdinalIgnoreCase)
            || normalizedCandidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    private void EnsureTextFileNameAllowed(string normalizedRelativePath)
    {
        string fileName = Path.GetFileName(normalizedRelativePath);
        if (fileName.StartsWith(".env", StringComparison.OrdinalIgnoreCase)
            || s_blockedFileNames.Contains(fileName))
        {
            throw new ArgumentException($"Repository file '{fileName}' is excluded by the secret-file policy.");
        }

        string extension = Path.GetExtension(fileName);
        if (s_blockedExtensions.Contains(extension))
            throw new ArgumentException($"Repository file extension '{extension}' is excluded.");
        if (_additionalTextFileNames.Contains(fileName))
            return;
        if (extension.Length == 0)
        {
            if (!s_allowedExtensionlessNames.Contains(fileName))
                throw new ArgumentException($"Repository file '{fileName}' is not an allowed text type.");
            return;
        }
        if (!s_allowedExtensions.Contains(extension) && !_additionalTextExtensions.Contains(extension))
            throw new ArgumentException($"Repository file extension '{extension}' is not an allowed text type.");
    }

    private static string DisplayPath(string normalizedPath)
        => normalizedPath.Length == 0 ? "." : normalizedPath.Replace('\\', '/');
}
