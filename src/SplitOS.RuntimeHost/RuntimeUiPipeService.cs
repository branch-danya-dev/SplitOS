using System.Diagnostics;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SplitOS.Contracts.Protocol;
using SplitOS.Ipc;
using SplitOS.Ipc.Windows;

namespace SplitOS.RuntimeHost;

public sealed partial class RuntimeUiPipeService(
    ILogger<RuntimeUiPipeService> logger,
    RuntimeUiCallerValidator callerValidator) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var process = Process.GetCurrentProcess();
        var sessionId = process.SessionId;
        var pipeName = NamedPipeNames.RuntimeForSession(sessionId);
        LogPipeStarting(logger, pipeName, sessionId);

        while (!stoppingToken.IsCancellationRequested)
        {
            var server = NamedPipeRpcServer.Create(pipeName);
            try
            {
                await server.WaitForConnectionAsync(stoppingToken).ConfigureAwait(false);
                _ = HandleConnectionAsync(server, checked((uint)sessionId), stoppingToken);
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
                        LogCallerDenied(
                            logger,
                            identity.ProcessId,
                            identity.SessionId,
                            identity.ImagePath,
                            authorization.Reason);
                        return ValueTask.FromResult(HandshakeDecision.Deny(authorization.Reason ?? ErrorCodes.CallerNotAuthorized));
                    }

                    LogCallerAccepted(
                        logger,
                        identity.ProcessId,
                        identity.SessionId,
                        identity.ImagePath,
                        hello.Component);
                    return ValueTask.FromResult(HandshakeDecision.Allow());
                },
                HandleMessageAsync,
                cancellationToken).ConfigureAwait(false);
        }
    }

    private static ValueTask<WireMessage> HandleMessageAsync(WireMessage request, CancellationToken _)
    {
        if (!string.Equals(request.Capability, Capabilities.RuntimeHealthRead, StringComparison.Ordinal))
        {
            return ValueTask.FromResult(WireMessage.Respond(
                request,
                MessageTypes.ErrorResponse,
                new ErrorResponse(ErrorCodes.UnknownCapability, "Runtime capability is not allowlisted.")));
        }

        if (!string.Equals(request.MessageType, MessageTypes.HealthReadRequest, StringComparison.Ordinal))
        {
            return ValueTask.FromResult(WireMessage.Respond(
                request,
                MessageTypes.ErrorResponse,
                new ErrorResponse(ErrorCodes.UnsupportedMessage, "Capability does not support this message type.")));
        }

        using var process = Process.GetCurrentProcess();
        return ValueTask.FromResult(WireMessage.Respond(
            request,
            MessageTypes.HealthReadResult,
            new HealthReadResult(
                ComponentIdentity.Name,
                ComponentIdentity.Version,
                "HEALTHY",
                Environment.ProcessId,
                process.SessionId,
                DateTimeOffset.UtcNow)));
    }

    [LoggerMessage(2000, LogLevel.Information, "Runtime UI pipe {PipeName} starting for session {SessionId}.")]
    private static partial void LogPipeStarting(ILogger logger, string pipeName, int sessionId);

    [LoggerMessage(2001, LogLevel.Warning, "Runtime UI caller denied. PID={ProcessId} Session={SessionId} Image={ImagePath} Reason={Reason}")]
    private static partial void LogCallerDenied(
        ILogger logger,
        uint processId,
        uint sessionId,
        string? imagePath,
        string? reason);

    [LoggerMessage(2002, LogLevel.Information, "Runtime UI caller accepted. PID={ProcessId} Session={SessionId} Image={ImagePath} ClaimedComponent={ClaimedComponent}")]
    private static partial void LogCallerAccepted(
        ILogger logger,
        uint processId,
        uint sessionId,
        string? imagePath,
        string claimedComponent);
}
