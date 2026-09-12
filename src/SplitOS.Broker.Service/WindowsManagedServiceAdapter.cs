using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace SplitOS.Broker.Service;

/// <summary>
/// Narrow SCM adapter for release-owned managed service targets. The adapter accepts a trusted
/// catalog entry, never an IPC-provided service key name or access mask. Start/stop submission is
/// followed by bounded QueryServiceStatusEx read-back before a verified result is returned.
/// </summary>
public sealed class WindowsManagedServiceAdapter : IManagedServiceAdapter
{
    private const uint ScManagerConnect = 0x0001;
    private const uint ServiceControlStop = 0x00000001;
    private const int ScStatusProcessInfo = 0;

    private const uint ServiceStopped = 0x00000001;
    private const uint ServiceStartPending = 0x00000002;
    private const uint ServiceStopPending = 0x00000003;
    private const uint ServiceRunning = 0x00000004;
    private const uint ServiceContinuePending = 0x00000005;
    private const uint ServicePausePending = 0x00000006;
    private const uint ServicePaused = 0x00000007;

    private const int ErrorAccessDenied = 5;
    private const int ErrorDependentServicesRunning = 1051;
    private const int ErrorServiceAlreadyRunning = 1056;
    private const int ErrorServiceDisabled = 1058;
    private const int ErrorServiceDoesNotExist = 1060;
    private const int ErrorServiceCannotAcceptControl = 1061;
    private const int ErrorServiceNotActive = 1062;

    public ValueTask<ManagedServiceObservation> QueryAsync(
        ManagedServiceCatalogEntry entry,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        cancellationToken.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsWindows())
        {
            return ValueTask.FromResult(new ManagedServiceObservation(ManagedServiceObservedState.Unknown));
        }

        using var scm = NativeMethods.OpenSCManagerW(null, null, ScManagerConnect);
        if (scm.IsInvalid)
        {
            var error = Marshal.GetLastWin32Error();
            return ValueTask.FromResult(new ManagedServiceObservation(
                error == ErrorAccessDenied
                    ? ManagedServiceObservedState.AccessDenied
                    : ManagedServiceObservedState.Unknown,
                NativeErrorCode: error));
        }

        using var service = NativeMethods.OpenServiceW(
            scm,
            entry.WindowsServiceName,
            (uint)entry.AccessPolicy.QueryAccess);
        if (service.IsInvalid)
        {
            var error = Marshal.GetLastWin32Error();
            return ValueTask.FromResult(new ManagedServiceObservation(
                error switch
                {
                    ErrorServiceDoesNotExist => ManagedServiceObservedState.NotFound,
                    ErrorAccessDenied => ManagedServiceObservedState.AccessDenied,
                    _ => ManagedServiceObservedState.Unknown
                },
                NativeErrorCode: error));
        }

        return ValueTask.FromResult(QueryOpenedService(service));
    }

    public async ValueTask<ManagedServiceTechnicalResult> ApplyAsync(
        ManagedServiceCatalogEntry entry,
        ManagedServiceDesiredState desiredState,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (!entry.AllowedStates.Contains(desiredState))
        {
            return Failure(
                ManagedServiceApplyDisposition.OperationRejected,
                false,
                "DESIRED_STATE_NOT_ALLOWLISTED",
                ManagedServiceObservedState.Unknown,
                "NOT_VERIFIED",
                "DESIRED_STATE_NOT_ALLOWLISTED");
        }

        if (!OperatingSystem.IsWindows())
        {
            return Failure(
                ManagedServiceApplyDisposition.UnsupportedCapability,
                false,
                "WINDOWS_REQUIRED",
                ManagedServiceObservedState.Unknown,
                "NOT_VERIFIED",
                "UNSUPPORTED_CAPABILITY");
        }

        var build = Environment.OSVersion.Version.Build;
        if (build < entry.MinimumWindowsBuild ||
            (entry.MaximumWindowsBuild.HasValue && build > entry.MaximumWindowsBuild.Value))
        {
            return Failure(
                ManagedServiceApplyDisposition.UnsupportedCapability,
                false,
                "WINDOWS_BUILD_NOT_SUPPORTED",
                ManagedServiceObservedState.Unknown,
                "NOT_VERIFIED",
                "UNSUPPORTED_CAPABILITY");
        }

        cancellationToken.ThrowIfCancellationRequested();
        using var scm = NativeMethods.OpenSCManagerW(null, null, ScManagerConnect);
        if (scm.IsInvalid)
        {
            return OpenFailure(Marshal.GetLastWin32Error(), false);
        }

        var access = (uint)entry.AccessPolicy.Resolve(desiredState);
        using var service = NativeMethods.OpenServiceW(scm, entry.WindowsServiceName, access);
        if (service.IsInvalid)
        {
            return OpenFailure(Marshal.GetLastWin32Error(), false);
        }

        var current = QueryOpenedService(service);
        if (current.State is ManagedServiceObservedState.NotFound or ManagedServiceObservedState.AccessDenied or ManagedServiceObservedState.Unknown)
        {
            return ObservationFailure(current, false);
        }

        if (IsTarget(current.State, desiredState))
        {
            return Success(
                ManagedServiceApplyDisposition.AlreadySatisfied,
                false,
                "ALREADY_SATISFIED",
                current.State);
        }

        if (IsTransitional(current.State))
        {
            current = await WaitUntilTargetOrStableAsync(
                service,
                desiredState,
                entry.TransitionTimeout,
                current,
                cancellationToken).ConfigureAwait(false);
            if (IsTarget(current.State, desiredState))
            {
                return Success(
                    ManagedServiceApplyDisposition.AlreadySatisfied,
                    false,
                    "ALREADY_SATISFIED_AFTER_PENDING",
                    current.State);
            }

            if (IsTransitional(current.State))
            {
                return Failure(
                    ManagedServiceApplyDisposition.VerificationFailed,
                    false,
                    "PREEXISTING_TRANSITION_TIMEOUT",
                    current.State,
                    "NOT_VERIFIED",
                    "VERIFICATION_FAILED",
                    current.NativeErrorCode);
            }
        }

        if (!CanIssueMutation(current.State, desiredState))
        {
            return Failure(
                ManagedServiceApplyDisposition.OperationRejected,
                false,
                "CURRENT_STATE_NOT_MUTABLE_TO_TARGET",
                current.State,
                "NOT_VERIFIED",
                "OPERATION_REJECTED",
                current.NativeErrorCode);
        }

        cancellationToken.ThrowIfCancellationRequested();
        var submitted = desiredState == ManagedServiceDesiredState.Running
            ? NativeMethods.StartServiceW(service, 0, IntPtr.Zero)
            : StopService(service);
        var immediateError = submitted ? 0 : Marshal.GetLastWin32Error();

        if (!submitted && !IsBenignReplayError(immediateError, desiredState))
        {
            return Failure(
                immediateError == ErrorAccessDenied
                    ? ManagedServiceApplyDisposition.AccessDenied
                    : ManagedServiceApplyDisposition.OperationRejected,
                true,
                "SCM_OPERATION_REJECTED",
                current.State,
                "NOT_VERIFIED",
                MapNativeError(immediateError),
                immediateError);
        }

        // Cancellation class is CANCELABLE_UNTIL_APPLY. Once SCM accepted the state-changing call,
        // continue bounded read-back even if the caller abandons its request. Runtime may reconcile
        // a lost response from durable action evidence and actual state.
        var verified = await WaitForTargetAsync(
            service,
            desiredState,
            entry.TransitionTimeout,
            CancellationToken.None).ConfigureAwait(false);
        if (IsTarget(verified.State, desiredState))
        {
            return Success(
                ManagedServiceApplyDisposition.AppliedVerified,
                true,
                submitted ? "SCM_OPERATION_ACCEPTED" : "IDEMPOTENT_REPLAY_ACCEPTED",
                verified.State);
        }

        if (verified.State == ManagedServiceObservedState.NotFound)
        {
            return Failure(
                ManagedServiceApplyDisposition.TargetNotFound,
                true,
                "TARGET_DISAPPEARED",
                verified.State,
                "NOT_VERIFIED",
                "TARGET_NOT_FOUND",
                verified.NativeErrorCode);
        }

        if (verified.State == ManagedServiceObservedState.AccessDenied)
        {
            return Failure(
                ManagedServiceApplyDisposition.AccessDenied,
                true,
                "READBACK_ACCESS_DENIED",
                verified.State,
                "NOT_VERIFIED",
                "ACCESS_DENIED",
                verified.NativeErrorCode);
        }

        return Failure(
            ManagedServiceApplyDisposition.VerificationFailed,
            true,
            submitted ? "SCM_OPERATION_ACCEPTED" : "IDEMPOTENT_REPLAY_ACCEPTED",
            verified.State,
            "NOT_VERIFIED",
            "VERIFICATION_FAILED",
            verified.NativeErrorCode);
    }

    private static bool StopService(SafeServiceHandle service)
    {
        var status = new ServiceStatus();
        return NativeMethods.ControlService(service, ServiceControlStop, ref status);
    }

    private static ManagedServiceObservation QueryOpenedService(SafeServiceHandle service)
    {
        var status = new ServiceStatusProcess();
        var size = Marshal.SizeOf<ServiceStatusProcess>();
        if (!NativeMethods.QueryServiceStatusEx(
                service,
                ScStatusProcessInfo,
                ref status,
                size,
                out _))
        {
            var error = Marshal.GetLastWin32Error();
            return new ManagedServiceObservation(
                error switch
                {
                    ErrorServiceDoesNotExist => ManagedServiceObservedState.NotFound,
                    ErrorAccessDenied => ManagedServiceObservedState.AccessDenied,
                    _ => ManagedServiceObservedState.Unknown
                },
                NativeErrorCode: error);
        }

        return new ManagedServiceObservation(
            NormalizeState(status.CurrentState),
            status.WaitHint,
            null);
    }

    private static async ValueTask<ManagedServiceObservation> WaitUntilTargetOrStableAsync(
        SafeServiceHandle service,
        ManagedServiceDesiredState desiredState,
        TimeSpan timeout,
        ManagedServiceObservation initial,
        CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow.Add(timeout);
        var current = initial;
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (IsTarget(current.State, desiredState) || !IsTransitional(current.State)) return current;
            await Task.Delay(PollDelay(current.WaitHintMilliseconds), cancellationToken).ConfigureAwait(false);
            current = QueryOpenedService(service);
            if (current.State is ManagedServiceObservedState.NotFound or ManagedServiceObservedState.AccessDenied or ManagedServiceObservedState.Unknown)
                return current;
        }

        return current;
    }

    private static async ValueTask<ManagedServiceObservation> WaitForTargetAsync(
        SafeServiceHandle service,
        ManagedServiceDesiredState desiredState,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow.Add(timeout);
        var current = QueryOpenedService(service);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (IsTarget(current.State, desiredState)) return current;
            if (current.State is ManagedServiceObservedState.NotFound or ManagedServiceObservedState.AccessDenied or ManagedServiceObservedState.Unknown)
                return current;
            await Task.Delay(PollDelay(current.WaitHintMilliseconds), cancellationToken).ConfigureAwait(false);
            current = QueryOpenedService(service);
        }

        return current;
    }

    private static TimeSpan PollDelay(uint waitHintMilliseconds)
    {
        var candidate = waitHintMilliseconds == 0 ? 200 : waitHintMilliseconds / 10;
        return TimeSpan.FromMilliseconds(Math.Clamp(candidate, 100u, 1000u));
    }

    private static bool CanIssueMutation(
        ManagedServiceObservedState state,
        ManagedServiceDesiredState desiredState)
        => desiredState switch
        {
            ManagedServiceDesiredState.Running => state == ManagedServiceObservedState.Stopped,
            ManagedServiceDesiredState.Stopped => state is ManagedServiceObservedState.Running or ManagedServiceObservedState.Paused,
            _ => false
        };

    private static bool IsTarget(
        ManagedServiceObservedState state,
        ManagedServiceDesiredState desiredState)
        => desiredState switch
        {
            ManagedServiceDesiredState.Running => state == ManagedServiceObservedState.Running,
            ManagedServiceDesiredState.Stopped => state == ManagedServiceObservedState.Stopped,
            _ => false
        };

    private static bool IsTransitional(ManagedServiceObservedState state)
        => state is
            ManagedServiceObservedState.StartPending or
            ManagedServiceObservedState.StopPending or
            ManagedServiceObservedState.OtherTransitional;

    private static ManagedServiceObservedState NormalizeState(uint state)
        => state switch
        {
            ServiceStopped => ManagedServiceObservedState.Stopped,
            ServiceStartPending => ManagedServiceObservedState.StartPending,
            ServiceStopPending => ManagedServiceObservedState.StopPending,
            ServiceRunning => ManagedServiceObservedState.Running,
            ServicePaused => ManagedServiceObservedState.Paused,
            ServiceContinuePending or ServicePausePending => ManagedServiceObservedState.OtherTransitional,
            _ => ManagedServiceObservedState.Unknown
        };

    private static bool IsBenignReplayError(int error, ManagedServiceDesiredState desiredState)
        => desiredState switch
        {
            ManagedServiceDesiredState.Running => error == ErrorServiceAlreadyRunning,
            ManagedServiceDesiredState.Stopped => error == ErrorServiceNotActive,
            _ => false
        };

    private static string MapNativeError(int error)
        => error switch
        {
            ErrorAccessDenied => "ACCESS_DENIED",
            ErrorServiceDoesNotExist => "TARGET_NOT_FOUND",
            ErrorDependentServicesRunning => "DEPENDENT_SERVICES_RUNNING",
            ErrorServiceCannotAcceptControl => "SERVICE_CANNOT_ACCEPT_CONTROL",
            ErrorServiceDisabled => "SERVICE_DISABLED",
            _ => "SCM_ERROR"
        };

    private static ManagedServiceTechnicalResult OpenFailure(int error, bool attempted)
        => error switch
        {
            ErrorServiceDoesNotExist => Failure(
                ManagedServiceApplyDisposition.TargetNotFound,
                attempted,
                "OPEN_SERVICE_FAILED",
                ManagedServiceObservedState.NotFound,
                "NOT_VERIFIED",
                "TARGET_NOT_FOUND",
                error),
            ErrorAccessDenied => Failure(
                ManagedServiceApplyDisposition.AccessDenied,
                attempted,
                "OPEN_SERVICE_FAILED",
                ManagedServiceObservedState.AccessDenied,
                "NOT_VERIFIED",
                "ACCESS_DENIED",
                error),
            _ => Failure(
                ManagedServiceApplyDisposition.TechnicalFailure,
                attempted,
                "OPEN_SERVICE_FAILED",
                ManagedServiceObservedState.Unknown,
                "NOT_VERIFIED",
                "SCM_ERROR",
                error)
        };

    private static ManagedServiceTechnicalResult ObservationFailure(
        ManagedServiceObservation observation,
        bool attempted)
        => observation.State switch
        {
            ManagedServiceObservedState.NotFound => Failure(
                ManagedServiceApplyDisposition.TargetNotFound,
                attempted,
                "QUERY_FAILED",
                observation.State,
                "NOT_VERIFIED",
                "TARGET_NOT_FOUND",
                observation.NativeErrorCode),
            ManagedServiceObservedState.AccessDenied => Failure(
                ManagedServiceApplyDisposition.AccessDenied,
                attempted,
                "QUERY_FAILED",
                observation.State,
                "NOT_VERIFIED",
                "ACCESS_DENIED",
                observation.NativeErrorCode),
            _ => Failure(
                ManagedServiceApplyDisposition.TechnicalFailure,
                attempted,
                "QUERY_FAILED",
                observation.State,
                "NOT_VERIFIED",
                "SCM_ERROR",
                observation.NativeErrorCode)
        };

    private static ManagedServiceTechnicalResult Success(
        ManagedServiceApplyDisposition disposition,
        bool attempted,
        string immediateResult,
        ManagedServiceObservedState actualState)
        => new(
            disposition,
            attempted,
            immediateResult,
            actualState,
            "VERIFIED");

    private static ManagedServiceTechnicalResult Failure(
        ManagedServiceApplyDisposition disposition,
        bool attempted,
        string immediateResult,
        ManagedServiceObservedState actualState,
        string verificationStatus,
        string? errorCode,
        int? nativeErrorCode = null)
        => new(
            disposition,
            attempted,
            immediateResult,
            actualState,
            verificationStatus,
            errorCode,
            nativeErrorCode);

    [StructLayout(LayoutKind.Sequential)]
    private struct ServiceStatusProcess
    {
        public uint ServiceType;
        public uint CurrentState;
        public uint ControlsAccepted;
        public uint Win32ExitCode;
        public uint ServiceSpecificExitCode;
        public uint CheckPoint;
        public uint WaitHint;
        public uint ProcessId;
        public uint ServiceFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ServiceStatus
    {
        public uint ServiceType;
        public uint CurrentState;
        public uint ControlsAccepted;
        public uint Win32ExitCode;
        public uint ServiceSpecificExitCode;
        public uint CheckPoint;
        public uint WaitHint;
    }

    private sealed class SafeServiceHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        public SafeServiceHandle() : base(true) { }

        protected override bool ReleaseHandle() => NativeMethods.CloseServiceHandle(handle);
    }

    private static class NativeMethods
    {
        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern SafeServiceHandle OpenSCManagerW(
            string? machineName,
            string? databaseName,
            uint desiredAccess);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern SafeServiceHandle OpenServiceW(
            SafeServiceHandle serviceControlManager,
            string serviceName,
            uint desiredAccess);

        [DllImport("advapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool QueryServiceStatusEx(
            SafeServiceHandle service,
            int infoLevel,
            ref ServiceStatusProcess buffer,
            int bufferSize,
            out int bytesNeeded);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool StartServiceW(
            SafeServiceHandle service,
            int argumentCount,
            IntPtr arguments);

        [DllImport("advapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool ControlService(
            SafeServiceHandle service,
            uint control,
            ref ServiceStatus status);

        [DllImport("advapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool CloseServiceHandle(IntPtr serviceHandle);
    }
}
