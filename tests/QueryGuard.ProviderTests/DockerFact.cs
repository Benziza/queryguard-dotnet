using System;
using System.Diagnostics;
using Xunit;

namespace QueryGuard.ProviderTests;

/// <summary>
/// A fact that skips itself when no Docker daemon is reachable.
/// </summary>
/// <remarks>
/// <para>
/// A contributor without Docker should be able to run the whole suite and get a green result. A
/// contributor <em>with</em> Docker, and CI, should run these for real, so this skips rather than
/// disappearing behind a compile-time flag, and the skip reason says exactly why.
/// </para>
/// <para>
/// This is not a licence to skip a flaky test. Container startup being unavailable is an environment
/// fact; a container that starts and then produces inconsistent results is a bug to diagnose.
/// </para>
/// </remarks>
public sealed class DockerFactAttribute : FactAttribute
{
    public DockerFactAttribute()
    {
        if (!DockerAvailability.IsAvailable)
        {
            Skip = "Docker is not available, so the container-backed provider suites cannot run here. "
                + "They run in CI.";
        }
    }
}

/// <summary>
/// Detects a reachable Docker daemon once per test run.
/// </summary>
internal static class DockerAvailability
{
    private static readonly Lazy<bool> Probe = new(Detect, isThreadSafe: true);

    internal static bool IsAvailable => Probe.Value;

    private static bool Detect()
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo("docker", "version --format {{.Server.Os}}")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            });

            if (process is null)
            {
                return false;
            }

            // A short timeout: `docker version` against a stopped daemon can hang for a long while, and
            // a suite that stalls before skipping is barely better than one that fails.
            if (!process.WaitForExit(10_000))
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException)
                {
                    // Already gone between the timeout and the kill; nothing to do.
                }

                return false;
            }

            return process.ExitCode == 0;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // No docker executable on PATH.
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }
}
