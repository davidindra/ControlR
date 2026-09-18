using System.Collections.Frozen;
using ControlR.Libraries.Api.Contracts.Dtos.ServerApi.V1.PermissionAssignments;

namespace ControlR.Web.Client.Helpers;

/// <summary>
/// Derives the display families of the permission catalog. A permission's family is the segment of
/// its <see cref="PermissionCatalogEntryDto.Name"/> before the first <c>.</c>, which is the
/// de-facto resource family the server names permissions by (<c>device.read</c>,
/// <c>device-group.assign-devices</c>, <c>tenant.users.read</c>). Splitting on the first separator
/// only is what keeps <c>device-group</c> out of the <c>device</c> family.
/// </summary>
internal static class PermissionGrouping
{
  private const char FamilySeparator = '.';

  /// <summary>
  /// Explicit labels so a header is never a surprise-cased prefix. Unknown families throw rather
  /// than fall back to a generated label or an "Other" bucket, because a silently mislabelled
  /// permission in a picker is worse than a build the owner of the new permission has to finish.
  /// <c>PermissionGroupingTests</c> holds the catalog against this map.
  /// </summary>
  private static readonly FrozenDictionary<string, string> _groupLabels = new Dictionary<string, string>
  {
    ["agent"] = "Agent",
    ["device"] = "Devices",
    ["device-group"] = "Device Groups",
    ["installer-key"] = "Installer Keys",
    ["personal-access-token"] = "Personal Access Tokens",
    ["server"] = "Server",
    ["service-account"] = "Service Accounts",
    ["tenant"] = "Tenant",
    ["user-group"] = "User Groups",
  }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

  internal static IReadOnlyDictionary<string, string> GroupLabels => _groupLabels;

  /// <summary>
  /// Groups catalog entries for display: groups ordered by label, entries ordered by
  /// <see cref="PermissionCatalogEntryDto.Name"/> within a group. Empty groups cannot occur because
  /// a group only exists once it has an entry, so callers that filter the entries first get
  /// empty-group hiding for free.
  /// </summary>
  internal static IReadOnlyList<PermissionGroup> GroupForDisplay(IEnumerable<PermissionCatalogEntryDto> entries) =>
    [
      .. entries
        .GroupBy(entry => GroupKeyOf(entry.Name), StringComparer.OrdinalIgnoreCase)
        .OrderBy(group => LabelOf(group.Key), StringComparer.OrdinalIgnoreCase)
        .Select(group => new PermissionGroup(
          group.Key,
          LabelOf(group.Key),
          [.. group.OrderBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)]))
    ];

  internal static string GroupLabel(string permissionName) => LabelOf(GroupKeyOf(permissionName));

  private static string GroupKeyOf(string permissionName) =>
    permissionName.IndexOf(FamilySeparator) is var separatorIndex && separatorIndex >= 0
      ? permissionName[..separatorIndex]
      : permissionName;

  private static string LabelOf(string groupKey) =>
    _groupLabels.TryGetValue(groupKey, out var label)
      ? label
      : throw new InvalidOperationException(
        $"The permission family '{groupKey}' has no display label. Add it to {nameof(PermissionGrouping)}'s group labels.");
}
