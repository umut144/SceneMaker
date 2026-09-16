using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using SceneMaker.Core;

namespace SceneMaker.App;

/// <summary>
/// world01 refuses to load an export SceneMaker hasn't synced into its own
/// asset tree - that sync is <c>scripts/sync_scenemaker_world.sh</c> in the
/// world01 repository, and until now an author had to remember to run it by
/// hand after every Export. This runs it automatically immediately after a
/// successful export of the world01 Workspace, so the two can no longer
/// drift apart because someone forgot the manual step.
/// </summary>
internal static class World01Sync
{
    /// <summary>
    /// The one Workspace this applies to. The sync script is specific to
    /// world01, and a Workspace directory's name is already required to
    /// equal its own key (see <see cref="WorkspaceSession"/>), so this is
    /// also exactly what world01's exports directory is named.
    /// </summary>
    private const string ApplicableWorkspaceKey = "world01";

    private const string ScriptRelativePath = "scripts/sync_scenemaker_world.sh";

    /// <summary>
    /// Overrides where the world01 checkout lives, the same convention
    /// <c>sync_polytools_world.sh</c> and <c>sync_scenemaker_world.sh</c>
    /// already use for the opposite direction. Without it, SceneMaker
    /// assumes the sibling layout AGENTS.md documents - <c>GodotProjects/SceneMaker</c>
    /// and <c>BevyProjects/world01</c> side by side under the same home
    /// directory - since there is no configured link between the two
    /// repositories anywhere in either one's data.
    /// </summary>
    private const string RepoDirEnvironmentVariable = "WORLD01_REPO_DIR";

    private static readonly TimeSpan ProcessTimeout = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Runs the sync for <paramref name="session"/>'s Workspace if it is
    /// world01; every other Workspace has no such script, so this silently
    /// does nothing and returns null. Otherwise returns whether the sync
    /// succeeded and a one-line message describing the outcome, meant to be
    /// appended to the export's own report.
    /// </summary>
    public static (bool Succeeded, string Message)? RunIfApplicable(WorkspaceSession session)
    {
        if (session.WorkspaceKey != ApplicableWorkspaceKey) return null;

        string repoRoot;
        try
        {
            repoRoot = ResolveWorld01RepoRoot(session);
        }
        catch (InvalidOperationException exception)
        {
            return (false, $"world01 sync skipped: {exception.Message}");
        }

        var script = Path.Combine(repoRoot, ScriptRelativePath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(script))
        {
            return (false, $"world01 sync skipped: '{script}' not found.");
        }

        return Run(script, repoRoot, session.DirectoryPath);
    }

    private static string ResolveWorld01RepoRoot(WorkspaceSession session)
    {
        var overridden = Environment.GetEnvironmentVariable(RepoDirEnvironmentVariable);
        if (!string.IsNullOrEmpty(overridden)) return Path.GetFullPath(overridden);

        // workspaces/<key> sits two directories below SceneMaker's own
        // repository root; from there, the assumed sibling layout locates
        // world01 the same way world01's own sync script locates SceneMaker.
        var workspacesDirectory = Directory.GetParent(session.DirectoryPath)
            ?? throw new InvalidOperationException(
                $"cannot resolve a parent directory above '{session.DirectoryPath}'.");
        var sceneMakerRoot = workspacesDirectory.Parent
            ?? throw new InvalidOperationException(
                $"cannot resolve a parent directory above '{workspacesDirectory.FullName}'.");
        return Path.GetFullPath(Path.Combine(sceneMakerRoot.FullName, "..", "..", "BevyProjects", "world01"));
    }

    private static (bool Succeeded, string Message) Run(string script, string repoRoot, string workspaceDirectory)
    {
        var startInfo = new ProcessStartInfo("/bin/bash")
        {
            WorkingDirectory = repoRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add(script);
        startInfo.EnvironmentVariables["SCENEMAKER_WORKSPACE"] = workspaceDirectory;

        try
        {
            using var process = new Process { StartInfo = startInfo };
            var stdout = new StringBuilder();
            var stderr = new StringBuilder();
            process.OutputDataReceived += (_, e) => { if (e.Data is not null) stdout.AppendLine(e.Data); };
            process.ErrorDataReceived += (_, e) => { if (e.Data is not null) stderr.AppendLine(e.Data); };
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            if (!process.WaitForExit((int)ProcessTimeout.TotalMilliseconds))
            {
                process.Kill(entireProcessTree: true);
                return (false, $"world01 sync timed out after {ProcessTimeout.TotalMinutes:N0} minutes and was stopped.");
            }

            if (process.ExitCode == 0) return (true, "world01 synced.");

            var detail = stderr.Length > 0 ? stderr.ToString() : stdout.ToString();
            return (false, $"world01 sync FAILED (exit {process.ExitCode}): {FirstLine(detail)}");
        }
        catch (Exception exception) when (exception is InvalidOperationException or Win32Exception)
        {
            return (false, $"world01 sync failed to start: {exception.Message}");
        }
    }

    private static string FirstLine(string text)
    {
        var trimmed = text.Trim();
        if (trimmed.Length == 0) return "(no output)";
        var newline = trimmed.IndexOf('\n');
        return newline < 0 ? trimmed : trimmed[..newline].TrimEnd('\r');
    }
}
