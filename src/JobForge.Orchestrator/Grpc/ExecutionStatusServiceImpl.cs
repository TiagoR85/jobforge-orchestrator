using Grpc.Core;

namespace JobForge.Orchestrator.Grpc;

public sealed class ExecutionStatusServiceImpl(
    ExecutionStatusProcessor processor,
    ILogger<ExecutionStatusServiceImpl> logger) : ExecutionStatus.ExecutionStatusBase
{
    public override async Task Report(
        IAsyncStreamReader<StatusReport> requestStream,
        IServerStreamWriter<ReportAck> responseStream,
        ServerCallContext context)
    {
        await foreach (var report in requestStream.ReadAllAsync(context.CancellationToken))
        {
            bool persisted;
            try
            {
                persisted = await processor.PersistAsync(report, context.CancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to persist status for {ExecutionId}", report.ExecutionId);
                persisted = false;
            }
            await responseStream.WriteAsync(new ReportAck
            {
                ExecutionId = report.ExecutionId,
                Persisted = persisted
            });
        }
    }
}
