using ControlR.Libraries.Api.Contracts.Dtos.ServerApi.V1.PermissionAssignments;

namespace ControlR.Web.Client.ViewModels;

/// <summary>
/// One header row and the permission catalog entries that belong under it.
/// </summary>
/// <param name="Key">The family prefix taken from the permission name.</param>
/// <param name="Label">The header text, derived from the family prefix.</param>
/// <param name="Entries">The entries in the family, ordered by name.</param>
internal readonly record struct PermissionGroup(
  string Key,
  string Label,
  IReadOnlyList<PermissionCatalogEntryDto> Entries);
