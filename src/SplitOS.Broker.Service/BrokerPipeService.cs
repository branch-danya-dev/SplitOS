using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SplitOS.Contracts.Protocol;
using SplitOS.Ipc;
using SplitOS.Ipc.Windows;

namespace SplitOS.Broker.Service;

public sealed class BrokerPipeService(
    ILogger<BrokerPipeService> logger,
    BrokerCallerValidator callerValidator,
    BrokerMessageHandler messageHandler) : BackgroundService
{
    private const uint NoConsoleSession = uint.MaxValue;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var activeSessionId = WindowsSessionInfo.ActiveConsoleSessionId;
            if (activeSessionId == NoConsoleSession)
            {
                await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken).ConfigureAwait(false);
                continue;
            }

            var pipeName = NamedPipeNames.BrokerForSession(checked((int)activeSessionId));
            var server = NamedPipeRpcServer.Create(pipeName);
            try
            {
                logger.LogDebug("Broker waiting on pipe {PipeName}.", pipeName);
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
                        logger.LogWarning(
                            "Broker caller denied. PID={ProcessId} Session={SessionId} Image={ImagePath} Reason={Reason} ClaimedComponent={ClaimedComponent}",
                            identity.ProcessId,
                            identity.SessionId,
                            identity.ImagePath,
                            authorization.Reason,
                            hello.Component);
                        return ValueTask.FromResult(HandshakeDecision.Deny(authorization.Reason ?? ErrorCodes.CallerNotAuthorized));
                    }

                    logger.LogInformation(
                        "Broker caller accepted. PID={ProcessId} Session={SessionId} Image={ImagePath} ClaimedComponent={ClaimedComponent}",
                        identity.ProcessId,
                        identity.SessionId,
                        identity.ImagePath,
                        hello.Component);
                    return ValueTask.FromResult(HandshakeDecision.Allow());
                },
                messageHandler.HandleAsync,
                cancellationToken).ConfigureAwait(false);
        }
    }
}
