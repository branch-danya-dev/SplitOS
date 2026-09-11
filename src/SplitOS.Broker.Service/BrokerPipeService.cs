using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SplitOS.Contracts.Protocol;
using SplitOS.Ipc;
using SplitOS.Ipc.Windows;
using SplitOS.Persistence.Machine;

namespace SplitOS.Broker.Service;

public sealed partial class BrokerPipeService(
    ILogger<BrokerPipeService> logger,
    BrokerCallerValidator callerValidator,
    BrokerMessageHandler messageHandler,
    MachineStateStore machineStateStore) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        BrokerServiceIdentity.EnsureLocalSystem();
        await machineStateStore.InitializeAsync(stoppingToken).ConfigureAwait(false);

        while (!stoppingToken.IsCancellationRequested)
        {
            var activeSessionId = WindowsSessionInfo.ActiveConsoleSessionId;
            if (activeSessionId == WindowsSessionInfo.NoConsoleSession)
            {
                await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken).ConfigureAwait(false);
                continue;
            }

            var pipeName = NamedPipeNames.BrokerForSession(checked((int)activeSessionId));
            var server = WindowsNamedPipeServerFactory.CreateBrokerForSession(pipeName, activeSessionId);
            try
            {
                LogWaiting(logger, pipeName, activeSessionId);
                await server.WaitForConnectionAsync(stoppingToken).ConfigureAwait(false);
                _ = HandleConnectionAsync(server, activeSessionId, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                await server.DisposeAsync().ConfigureAwait(false);
                break;
            }
            catch
            {
                await server.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }
    }

    private async Task HandleConnectionAsync(
        System.IO.Pipes.NamedPipeServerStream server,
        uint expectedSessionId,
        CancellationToken cancellationToken)
    {
        await using (server.ConfigureAwait(false))
        {
            await NamedPipeRpcServer.HandleConnectionAsync(
                server,
                ComponentIdentity.Name,
                ComponentIdentity.Version,
                (pipe, hello, _) =>
                {
                    var identity = PipeClientIdentityReader.Read(pipe);
                    var authorization = callerValidator.Validate(identity, expectedSessionId);
                    if (!authorization.Allowed)
                    {
                        LogCallerDenied(logger, identity.ProcessId, identity.SessionId, identity.ImagePath, authorization.Reason, hello.Component);
                        return ValueTask.FromResult(HandshakeDecision.Deny(authorization.Reason ?? ErrorCodes.CallerNotAuthorized));
                    }

                    LogCallerAccepted(logger, identity.ProcessId, identity.SessionId, identity.ImagePath, hello.Component);
                    return ValueTask.FromResult(HandshakeDecision.Allow());
                },
                async (request, token) =>
                {
                    // Recheck OS-derived authority for each command, including console switches
                    // after the handshake. No payload field grants physical-console ownership.
                    var currentSession = WindowsSessionInfo.ActiveConsoleSessionId;
                    var authorization = currentSession == WindowsSessionInfo.NoConsoleSession
                        ? BrokerAuthorization.Deny("NO_PHYSICAL_CONSOLE")
                        : callerValidator.Validate(PipeClientIdentityReader.Read(server), currentSession);
                    if (!authorization.Allowed)
                        return WireMessage.Respond(request, MessageTypes.ErrorResponse,
                            new ErrorResponse(ErrorCodes.CallerNotAuthorized, authorization.Reason ?? "Caller denied."));
                    return await messageHandler.HandleAsync(request, token).ConfigureAwait(false);
                },
                cancellationToken).ConfigureAwait(false);
        }
    }

    [LoggerMessage(1000, LogLevel.Debug, "Broker waiting on pipe {PipeName} for physical console session {SessionId}.")]
    private static partial void LogWaiting(ILogger logger, string pipeName, uint sessionId);

    [LoggerMessage(1001, LogLevel.Warning, "Broker caller denied. PID={ProcessId} Session={SessionId} Image={ImagePath} Reason={Reason} ClaimedComponent={ClaimedComponent}")]
    private static partial void LogCallerDenied(ILogger logger, uint processId, uint sessionId, string? imagePath, string? reason, string claimedComponent);

    [LoggerMessage(1002, LogLevel.Information, "Broker caller accepted. PID={ProcessId} Session={SessionId} Image={ImagePath} ClaimedComponent={ClaimedComponent}")]
    private static partial void LogCallerAccepted(ILogger logger, uint processId, uint sessionId, string? imagePath, string claimedComponent);
}
