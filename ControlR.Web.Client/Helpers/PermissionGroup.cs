using ControlR.Libraries.Api.Contracts.Dtos.ServerApi.V1.PermissionAssignments;

namespace ControlR.Web.Client.Helpers;

/// <summary>
/// One header row and the permission catalog entries that belong under it.
/// </summary>
/// <param name="Key">The family prefix taken from the permission name.</param>
/// <param name="Label">The text to show in the header row.</param>
/// <param name="Entries">The entries in the family, ordered by name.</param>
internal readonly record struct PermissionGroup(
  string Key,
  string Label,
  IReadOnlyList<PermissionCatalogEntryDto> Entries);
