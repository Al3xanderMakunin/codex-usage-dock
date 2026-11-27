using System;
using System.IO;
using System.Threading;

namespace CodexUsage;

internal sealed record UsageWindow(double Percent, DateTimeOffset? ResetsAt);

internal sealed record UsageSnapshot(UsageWindow? FiveHour, UsageWindow? Weekly, DateTimeOffset UpdatedAt);

/// <summary>Polls the local Codex session logs and updates reset countdowns.</summary>
internal sealed class UsageService : IDisposable
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(30);

    private readonly CodexSessionLogs _logs = new(AppInfo.SessionsDirectory);
    private readonly Timer _timer;
    private readonly Lock _lock = new();

    public UsageSnapshot? Latest { get; private set; }

    public event Action? Changed;

    public UsageService()
    {
        _timer = new Timer(_ => Refresh(), null, TimeSpan.Zero, TickInterval);
    }

    /// <summary>
    /// Reads the session logs and notifies listeners. Everything runs under one lock so updates are
    /// applied in order and a slow, older read can never overwrite a newer one in the UI.
    /// </summary>
    private void Refresh()
    {
        lock (_lock)
        {
            try
            {
                Latest = _logs.ReadLatest();
            }
            catch (FileNotFoundException)
            {
                Latest = null;
            }
            catch (DirectoryNotFoundException)
            {
                Latest = null;
            }
            catch (IOException)
            {
                // Retry transient filesystem errors on the next tick.
            }
            catch (UnauthorizedAccessException)
            {
            }

            // Handlers push property changes across COM to Command Palette. If the host has gone
            // away that throws, and an unhandled exception on a timer thread would kill the process.
            if (Changed is { } changed)
            {
                foreach (var handler in changed.GetInvocationList())
                {
                    try
                    {
                        ((Action)handler)();
                    }
                    catch
                    {
                    }
                }
            }
        }
    }

    public void Dispose() => _timer.Dispose();
}
