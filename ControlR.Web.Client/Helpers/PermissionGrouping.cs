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

  private const char FamilyWordSeparator = '-';

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

  private static string GroupKeyOf(string permissionName) =>
    permissionName.IndexOf(FamilySeparator) is var separatorIndex && separatorIndex >= 0
      ? permissionName[..separatorIndex]
      : permissionName;

  /// <summary>
  /// Turns a family prefix into its header. Hyphenated words become space-separated, each word is
  /// capitalized, and the last one is pluralized, so <c>device-group</c> reads "Device Groups".
  /// Nothing here is per-family, so a family introduced by a newer server gets the same header the
  /// server's own build would produce.
  /// </summary>
  private static string LabelOf(string groupKey)
  {
    var words = groupKey.Split(FamilyWordSeparator, StringSplitOptions.RemoveEmptyEntries);

    for (var i = 0; i < words.Length; i++)
    {
      words[i] = $"{char.ToUpperInvariant(words[i][0])}{words[i][1..]}";
    }

    if (words.Length > 0)
    {
      words[^1] = $"{words[^1]}s";
    }

    return string.Join(' ', words);
  }
}
