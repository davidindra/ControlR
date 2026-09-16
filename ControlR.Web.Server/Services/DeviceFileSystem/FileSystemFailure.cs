namespace ControlR.Web.Server.Services.DeviceFileSystem;

/// <summary>
/// Why a device file system operation stopped before it produced a payload. Carries no HTTP meaning;
/// each caller picks its own status for a given condition.
/// </summary>
public enum FileSystemFailure
{
  /// <summary>
  /// The request reached the agent and the operation completed.
  /// </summary>
  None = 0,

  /// <summary>
  /// No device with the requested id was visible to the caller.
  /// </summary>
  DeviceNotFound,

  /// <summary>
  /// The caller does not hold the device resource policy the operation requires.
  /// </summary>
  Forbidden,

  /// <summary>
  /// The device record exists but the agent is not currently connected.
  /// </summary>
  DeviceOffline,

  /// <summary>
  /// Waiting on the agent was canceled, usually because the request timed out.
  /// </summary>
  Cancelled,

  /// <summary>
  /// The agent answered, and the operation failed on the device. <see cref="FileSystemOutcome.Reason" />
  /// holds the agent's own explanation.
  /// </summary>
  RemoteFailure,

  /// <summary>
  /// The agent never answered, so there is nothing to report about the operation itself.
  /// <see cref="FileSystemOutcome.Reason" /> is null.
  /// </summary>
  NoResponse,

  /// <summary>
  /// The dispatch threw something other than cancellation.
  /// </summary>
  Unexpected
}
