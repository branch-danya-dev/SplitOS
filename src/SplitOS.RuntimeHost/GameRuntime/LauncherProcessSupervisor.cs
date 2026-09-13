using System.Diagnostics;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SplitOS.Ipc.Windows;

namespace SplitOS.RuntimeHost.GameRuntime;

public sealed record LauncherProcessObservation(
    int ProcessId,
    int SessionId,
    string? ImagePath,
    DateTimeOffset? StartedAtUtc);

public interface ILauncherProcessPlatform
{
    int CurrentSessionId { get; }
    string TrustedLauncherPath { get; }
    IReadOnlyList<LauncherProcessObservation> EnumerateLauncherProcesses();
    LauncherProcessObservation StartTrustedLauncher();
}

public sealed class WindowsLauncherProcessPlatform : ILauncherProcessPlatform
{
    public WindowsLauncherProcessPlatform()
    {
        using var current = Process.GetCurrentProcess();
        CurrentSessionId = current.SessionId;
        var releaseRoot = ReleaseLayout.ResolveCurrentReleaseRoot("RuntimeHost");
        TrustedLauncherPath = ReleaseLayout.ComponentExecutable(
            releaseRoot,
            "GameLauncher",
            "SplitOS.GameLauncher.exe");
    }

    public int CurrentSessionId { get; }
    public string TrustedLauncherPath { get; }

    public IReadOnlyList<LauncherProcessObservation> EnumerateLauncherProcesses()
    {
        var result = new List<LauncherProcessObservation>();
        foreach (var process in Process.GetProcessesByName("SplitOS.GameLauncher"))
        {
            using (process)
            {
                try
                {
                    var sessionId = process.SessionId;
                    if (sessionId != CurrentSessionId)
                        continue;

                    string? imagePath = null;
                    DateTimeOffset? startedAtUtc = null;
                    try { imagePath = process.MainModule?.FileName; } catch { }
                    try { startedAtUtc = process.StartTime.ToUniversalTime(); } catch { }
                    result.Add(new LauncherProcessObservation(process.Id, sessionId, imagePath, startedAtUtc));
                }
                catch (InvalidOperationException)
                {
                    // Process exited between enumeration and observation; a fresh pass will reconcile.
                }
            }
        }

        return result;
    }

    public LauncherProcessObservation StartTrustedLauncher()
    {
        var workingDirectory = Path.GetDirectoryName(TrustedLauncherPath)
            ?? throw new InvalidOperationException("Trusted Game Launcher directory cannot be resolved.");
        var process = Process.Start(new ProcessStartInfo
        {
            FileName = TrustedLauncherPath,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false
        }) ?? throw new InvalidOperationException("Windows did not return a Game Launcher process handle.");

        using (process)
        {
            DateTimeOffset? startedAtUtc = null;
            try { startedAtUtc = process.StartTime.ToUniversalTime(); } catch { }
            return new LauncherProcessObservation(process.Id, process.SessionId, TrustedLauncherPath, startedAtUtc);
        }
    }
}

public enum LauncherProcessSupervisionState
{
    Stopped,
    Starting,
    Running,
    Degraded
}

public static class LauncherProcessSupervisionReasonCodes
{
    public const string NotRequired = "LAUNCHER_NOT_REQUIRED";
    public const string TrustedInstanceRunning = "TRUSTED_LAUNCHER_RUNNING";
    public const string StartIssued = "TRUSTED_LAUNCHER_START_ISSUED";
    public const string RestartBackoff = "LAUNCHER_RESTART_BACKOFF";
    public const string RestartBudgetExhausted = "LAUNCHER_RESTART_BUDGET_EXHAUSTED";
    public const string UntrustedNameCollision = "UNTRUSTED_LAUNCHER_NAME_COLLISION";
    public const string MultipleTrustedInstances = "MULTIPLE_TRUSTED_LAUNCHER_INSTANCES";
    public const string StartedInstanceInvalid = "STARTED_LAUNCHER_IDENTITY_INVALID";
}

public sealed record LauncherProcessSupervisionDecision(
    LauncherProcessSupervisionState State,
    string ReasonCode,
    int? ProcessId,
    DateTimeOffset? NextStartAllowedUtc);

/// <summary>
/// Maintains at most one trusted Game Launcher instance in the Runtime Host's interactive session.
/// Same-name processes from any other path are never attached to, killed, or treated as SplitOS.
/// Restart attempts are bounded so a repeatedly crashing UI becomes a typed degraded condition.
/// </summary>
public sealed class LauncherProcessSupervisor
{
    private readonly ILauncherProcessPlatform _platform;
    private readonly int _maxRestartAttempts;
    private readonly TimeSpan _restartWindow;
    private readonly TimeSpan _minimumRestartDelay;
    private readonly Queue<DateTimeOffset> _restartAttempts = new();
    private DateTimeOffset? _nextStartAllowedUtc;

    public LauncherProcessSupervisor(
        ILauncherProcessPlatform platform,
        int maxRestartAttempts = 3,
        TimeSpan? restartWindow = null,
        TimeSpan? minimumRestartDelay = null)
    {
        _platform = platform ?? throw new ArgumentNullException(nameof(platform));
        if (maxRestartAttempts <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxRestartAttempts));
        _maxRestartAttempts = maxRestartAttempts;
        _restartWindow = restartWindow ?? TimeSpan.FromSeconds(30);
        _minimumRestartDelay = minimumRestartDelay ?? TimeSpan.FromSeconds(2);
        if (_restartWindow <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(restartWindow));
        if (_minimumRestartDelay < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(minimumRestartDelay));
    }

    public LauncherProcessSupervisionDecision EnsureRunning(DateTimeOffset nowUtc, bool required)
    {
        var currentSession = _platform.EnumerateLauncherProcesses()
            .Where(process => process.SessionId == _platform.CurrentSessionId)
            .ToArray();
        var trusted = currentSession
            .Where(process => ReleaseLayout.IsExactPath(process.ImagePath, _platform.TrustedLauncherPath))
            .ToArray();
        var untrusted = currentSession.Length - trusted.Length;

        if (trusted.Length > 1)
        {
            return new(
                LauncherProcessSupervisionState.Degraded,
                LauncherProcessSupervisionReasonCodes.MultipleTrustedInstances,
                null,
                _nextStartAllowedUtc);
        }

        if (trusted.Length == 1)
        {
            PruneAttempts(nowUtc);
            return new(
                LauncherProcessSupervisionState.Running,
                LauncherProcessSupervisionReasonCodes.TrustedInstanceRunning,
                trusted[0].ProcessId,
                null);
        }

        if (!required)
        {
            return new(
                LauncherProcessSupervisionState.Stopped,
                LauncherProcessSupervisionReasonCodes.NotRequired,
                null,
                null);
        }

        if (untrusted > 0)
        {
            return new(
                LauncherProcessSupervisionState.Degraded,
                LauncherProcessSupervisionReasonCodes.UntrustedNameCollision,
                null,
                _nextStartAllowedUtc);
        }

        PruneAttempts(nowUtc);
        if (_nextStartAllowedUtc is not null && nowUtc < _nextStartAllowedUtc.Value)
        {
            return new(
                LauncherProcessSupervisionState.Starting,
                LauncherProcessSupervisionReasonCodes.RestartBackoff,
                null,
                _nextStartAllowedUtc);
        }

        if (_restartAttempts.Count >= _maxRestartAttempts)
        {
            return new(
                LauncherProcessSupervisionState.Degraded,
                LauncherProcessSupervisionReasonCodes.RestartBudgetExhausted,
                null,
                _nextStartAllowedUtc);
        }

        var started = _platform.StartTrustedLauncher();
        _restartAttempts.Enqueue(nowUtc);
        _nextStartAllowedUtc = nowUtc + _minimumRestartDelay;

        if (started.SessionId != _platform.CurrentSessionId
            || !ReleaseLayout.IsExactPath(started.ImagePath, _platform.TrustedLauncherPath))
        {
            return new(
                LauncherProcessSupervisionState.Degraded,
                LauncherProcessSupervisionReasonCodes.StartedInstanceInvalid,
                null,
                _nextStartAllowedUtc);
        }

        return new(
            LauncherProcessSupervisionState.Starting,
            LauncherProcessSupervisionReasonCodes.StartIssued,
            started.ProcessId,
            _nextStartAllowedUtc);
    }

    private void PruneAttempts(DateTimeOffset nowUtc)
    {
        while (_restartAttempts.TryPeek(out var attempt)
               && nowUtc - attempt >= _restartWindow)
        {
            _restartAttempts.Dequeue();
        }

        if (_restartAttempts.Count == 0 && _nextStartAllowedUtc is not null && nowUtc >= _nextStartAllowedUtc.Value)
            _nextStartAllowedUtc = null;
    }
}

public sealed partial class LauncherProcessSupervisorService(
    ILogger<LauncherProcessSupervisorService> logger,
    LauncherProcessSupervisor supervisor,
    RuntimeStateState runtimeState,
    LauncherReadinessState readinessState) : BackgroundService
{
    private LauncherProcessSupervisionDecision? _lastDecision;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var runtime = runtimeState.Snapshot;
            var readiness = readinessState.Snapshot;
            var required = string.Equals(runtime.OperationalMode, "GAME", StringComparison.Ordinal)
                || readiness.ExpectedOperation is not null;

            try
            {
                var decision = supervisor.EnsureRunning(DateTimeOffset.UtcNow, required);
                if (_lastDecision?.State != decision.State
                    || !string.Equals(_lastDecision?.ReasonCode, decision.ReasonCode, StringComparison.Ordinal))
                {
                    LogSupervision(logger, decision.State.ToString(), decision.ReasonCode, decision.ProcessId);
                    _lastDecision = decision;
                }
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                LogSupervisionFailure(logger, ex.Message);
            }

            await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken).ConfigureAwait(false);
        }
    }

    [LoggerMessage(2700, LogLevel.Information, "Game Launcher supervision state={State} reason={Reason} pid={ProcessId}.")]
    private static partial void LogSupervision(ILogger logger, string state, string reason, int? processId);

    [LoggerMessage(2701, LogLevel.Warning, "Game Launcher supervision failed: {Message}")]
    private static partial void LogSupervisionFailure(ILogger logger, string message);
}
