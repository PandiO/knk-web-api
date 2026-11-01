namespace knkwebapi_v2.Services;

/// <summary>
/// PandiO/knk-workspace#180: the task is not in a state that allows this (a second claim, a complete after a cancel, a
/// heartbeat for a task that was released, a claim that lost a race). The controller answers 409 with the message and the
/// task's current status.
/// </summary>
public class WorldTaskConflictException : InvalidOperationException
{
    public WorldTaskConflictException(string message, string? currentStatus = null) : base(message)
    {
        CurrentStatus = currentStatus;
    }

    public string? CurrentStatus { get; }
}

/// <summary>
/// #180: the result was refused (validation, or a gate scan that could not be applied); the task is saved as Failed. 422.
/// </summary>
public class WorldTaskRejectedException : InvalidOperationException
{
    public WorldTaskRejectedException(string message) : base(message) { }
}
