using System.Diagnostics;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SplitOS.Contracts.Protocol;
using SplitOS.Ipc;
using SplitOS.Ipc.Windows;

namespace SplitOS.RuntimeHost;

public sealed class RuntimeUiPipeService(
    ILogger<RuntimeUiPipeService> logger,
    RuntimeUiCallerValidator callerValidator) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var sessionId = Process.GetCurrentProcess().SessionId;
        var pipeName = NamedPipeNames.RuntimeForSession(sessionId);
        logger.LogInformation("Runtime UI pipe {PipeName} starting for session {SessionId}.", pipeName, sessionId);

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
                        logger.LogWarning(
                            "Runtime UI caller denied. PID={ProcessId} Session={SessionId} Image={ImagePath} Reason={Reason}",
                            identity.ProcessId,
                            identity.SessionId,
                            identity.ImagePath,
                            authorization.Reason);
                        return ValueTask.FromResult(HandshakeDecision.Deny(authorization.Reason ?? ErrorCodes.CallerNotAuthorized));
                    }

                    logger.LogInformation(
                        "Runtime UI caller accepted. PID={ProcessId} Session={SessionId} Image={ImagePath} ClaimedComponent={ClaimedComponent}",
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

        var process = Process.GetCurrentProcess();
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
}
