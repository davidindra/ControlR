using System.Reflection;
using ControlR.Libraries.Api.Contracts.Authz;
using ControlR.Libraries.Api.Contracts.Dtos.ServerApi.V1.PermissionAssignments;
using ControlR.Libraries.Api.Contracts.Enums;
using ControlR.Web.Client.Helpers;

namespace ControlR.Web.Client.Tests;

/// <summary>
/// Pins the client's permission display grouping against the server's authoritative permission set.
/// The server publishes the <see cref="PermissionNames"/> constants, and
/// <c>PermissionPolicyMapTests</c> (ControlR.Web.Server.Tests) asserts those constants are exactly
/// the <c>PermissionCatalog.All</c> keys that <c>GET /api/v1/permission-assignments/catalog</c>
/// serves. Every catalog entry a picker can receive therefore descends from these constants, so
/// pinning the grouping against them pins it against the server catalog.
/// </summary>
public class PermissionGroupingTests
{
  /// <summary>
  /// The baseline: the family prefix (the segment of the permission name before the first ".") and
  /// the label shown over it. Held here as a literal on purpose - adding a family or renaming a
  /// label has to be a deliberate edit in two places.
  /// </summary>
  private static readonly IReadOnlyDictionary<string, string> _expectedGroupLabels =
    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
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
    };

  [Fact]
  public void GroupForDisplay_EveryServerPermissionNameLandsInAKnownGroup()
  {
    var groups = PermissionGrouping.GroupForDisplay(CatalogFromServerPermissionNames());

    var unlabelled = groups
      .Where(group => !_expectedGroupLabels.TryGetValue(group.Key, out var label) || label != group.Label)
      .ToArray();

    Assert.True(
      unlabelled.Length == 0,
      $"Unknown or mislabelled permission groups: {string.Join(", ", unlabelled.Select(group => $"{group.Key}={group.Label}"))}");

    Assert.Equal(
      GetServerPermissionNames().Count,
      groups.Sum(group => group.Entries.Count));

    // The header order a grouped picker would render. "Device Groups" precedes "Devices" and
    // "Server" precedes "Service Accounts" because a separator sorts before a letter.
    Assert.Equal(
      [
        "Agent",
        "Device Groups",
        "Devices",
        "Installer Keys",
        "Personal Access Tokens",
        "Server",
        "Service Accounts",
        "Tenant",
        "User Groups",
      ],
      groups.Select(group => group.Label));
  }

  [Fact]
  public void GroupForDisplay_OrdersGroupsByLabelAndEntriesByName()
  {
    var groups = PermissionGrouping.GroupForDisplay(
    [
      Entry(PermissionNames.ServerTenantsRead),
      Entry(PermissionNames.AgentInstall),
      Entry("device.Zebra.Read"),
      Entry("device.alpha.read"),
      Entry(PermissionNames.UserGroupAssignUsers),
    ]);

    Assert.Equal(
      ["Agent", "Devices", "Server", "User Groups"],
      groups.Select(group => group.Label));

    Assert.Equal(
      ["device.alpha.read", "device.Zebra.Read"],
      groups.Single(group => group.Key == "device").Entries.Select(entry => entry.Name));
  }

  /// <summary>
  /// A family is the segment before the first "." only. Splitting further would fold
  /// <c>device-group.assign-devices</c> into the <c>device</c> family.
  /// </summary>
  [Fact]
  public void GroupForDisplay_SplitsOnTheFirstDotOnly()
  {
    var groups = PermissionGrouping.GroupForDisplay(
    [
      Entry(PermissionNames.DeviceRead),
      Entry(PermissionNames.DeviceGroupAssignDevices),
    ]);

    // "Device Groups" precedes "Devices" because the separator sorts before a letter.
    Assert.Equal(["Device Groups", "Devices"], groups.Select(group => group.Label));
    Assert.Equal(["device-group", "device"], groups.Select(group => group.Key));
  }

  [Fact]
  public void GroupForDisplay_WhenThereAreNoEntries_ReturnsNoGroups()
  {
    Assert.Empty(PermissionGrouping.GroupForDisplay([]));
  }

  [Fact]
  public void GroupLabels_AreExactlyTheBaselineMap()
  {
    Assert.Equal(LabelPairs(_expectedGroupLabels), LabelPairs(PermissionGrouping.GroupLabels));
  }

  [Fact]
  public void GroupLabel_WhenFamilyIsUnknown_ThrowsInsteadOfInventingALabel()
  {
    var message = Assert.Throws<InvalidOperationException>(
      () => PermissionGrouping.GroupLabel("widget.read")).Message;

    Assert.Contains("widget", message);
  }

  private static IReadOnlyList<PermissionCatalogEntryDto> CatalogFromServerPermissionNames() =>
    [.. GetServerPermissionNames().Select(Entry)];

  private static PermissionCatalogEntryDto Entry(string name) =>
    new(name, name, "Test description.", [PermissionScopeKind.Tenant], true);

  private static IReadOnlyList<string> GetServerPermissionNames() =>
    typeof(PermissionNames)
      .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
      .Where(field => field.IsLiteral && field.FieldType == typeof(string))
      .Select(field => (string)field.GetRawConstantValue()!)
      .ToArray();

  private static string[] LabelPairs(IReadOnlyDictionary<string, string> labels) =>
    [
      .. labels
        .Select(label => $"{label.Key} -> {label.Value}")
        .OrderBy(pair => pair, StringComparer.OrdinalIgnoreCase)
    ];
}
