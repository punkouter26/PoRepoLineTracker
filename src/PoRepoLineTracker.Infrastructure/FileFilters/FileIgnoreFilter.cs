using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PoRepoLineTracker.Infrastructure.FileFilters;

/// <summary>
/// Filters files and directories that should be excluded from line counting.
/// </summary>
public class FileIgnoreFilter : IFileIgnoreFilter
{
    private readonly ILogger<FileIgnoreFilter> _logger;

    // Combined sets for faster lookup
    private readonly HashSet<string> _exactFileNames;
    private readonly HashSet<string> _extensionSuffixes;
    private readonly HashSet<string> _filePatterns;
    private readonly HashSet<string> _directoryPatterns;
    private const string MigrationFolder = "migrations/";

    public FileIgnoreFilter(ILogger<FileIgnoreFilter> logger)
    {
        _logger = logger;

        // Exact file names to ignore (case-insensitive)
        _exactFileNames =
        [
            "packages.config", "package-lock.json", "yarn.lock",
            "paket.lock", "paket.dependencies", "launchsettings.json"
        ];

        // Extension suffixes to ignore
        _extensionSuffixes =
        [
            // Build output
            ".dll", ".exe", ".pdb", ".obj", ".cache", ".lib", ".exp", ".ilk", ".idb", ".nupkg",
            // Auto-generated
            ".designer.cs", ".g.cs", ".g.i.cs", ".designer.vb", ".g.vb",
            // Third-party minified
            ".min.js", ".min.css",
            // IDE config
            ".user", ".suo", ".vspscc", ".vssscc",
            // Web assets
            ".woff", ".woff2", ".ttf", ".eot", ".otf",
            // Compiled resources
            ".resources"
        ];

        // File name patterns to ignore (contains match)
        _filePatterns =
        [
            "reference.cs", "temporarygeneratedfile", "assemblyinfo", "jquery", "bootstrap"
        ];

        // Directory patterns to ignore
        _directoryPatterns =
        [
            "bin/", "obj/", "debug/", "release/", "node_modules/", "bower_components/",
            "jspm_packages/", "typings/", ".vs/", ".vscode/", ".idea/", "wwwroot/lib/",
            ".git/", "packages/"
        ];
    }

    /// <inheritdoc />
    public bool ShouldIgnoreFile(string fileName, string filePath)
    {
        var nameLower = fileName.ToLowerInvariant();

        // Fast exact match
        if (_exactFileNames.Contains(nameLower))
        {
            _logger.LogDebug("Ignoring file (exact match): {FileName}", fileName);
            return true;
        }

        // Extension suffix match
        if (_extensionSuffixes.Any(nameLower.EndsWith))
        {
            _logger.LogDebug("Ignoring file (extension): {FileName}", fileName);
            return true;
        }

        // Pattern match
        if (_filePatterns.Any(nameLower.Contains))
        {
            _logger.LogDebug("Ignoring file (pattern): {FileName}", fileName);
            return true;
        }

        // Migration folder check
        if (filePath.Contains(MigrationFolder, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogDebug("Ignoring migration file: {FileName}", fileName);
            return true;
        }

        return false;
    }

    /// <inheritdoc />
    public bool ShouldIgnoreDirectory(string directoryPath)
    {
        var normalized = directoryPath.Replace("\\", "/").ToLowerInvariant() + "/";

        var shouldIgnore = _directoryPatterns.Any(p =>
            normalized.EndsWith(p) || normalized.Contains("/" + p) || normalized.StartsWith(p));

        if (shouldIgnore)
            _logger.LogDebug("Ignoring directory: {DirectoryPath}", directoryPath);

        return shouldIgnore;
    }
}
