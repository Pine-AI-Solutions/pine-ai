namespace PineAI.Core.Features.Safir;

public enum SafirBroadcastJobState
{
    Queued = 0,
    Running = 1,
    Completed = 2,
    Failed = 3,
    Cancelled = 4,

    /// <summary>The service stopped (restart / crash) while the job was running. It can be resumed.</summary>
    Interrupted = 5
}
