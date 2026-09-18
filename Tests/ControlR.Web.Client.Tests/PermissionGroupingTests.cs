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
/// grouping them covers the whole server catalog.
/// </summary>
public class PermissionGroupingTests
{
  [Fact]
  public void GroupForDisplay_EveryServerPermissionNameIsFamilyPrefixed()
  {
    var groups = PermissionGrouping.GroupForDisplay(CatalogFromServerPermissionNames());

    Assert.Equal(
      GetServerPermissionNames().Count,
      groups.Sum(group => group.Entries.Count));

    // Grouping takes the family as the segment before the first ".", so a name without one would
    // head a group named after the whole permission.
    var unprefixed = GetServerPermissionNames()
      .Where(name => !name.Contains('.', StringComparison.Ordinal))
      .ToArray();

    Assert.True(
      unprefixed.Length == 0,
      $"Permissions with no family prefix: {string.Join(", ", unprefixed)}");

    // The header order a grouped picker renders. "Device Groups" precedes "Devices" because a
    // separator sorts before a letter.
    Assert.Equal(
      [
        "Agents",
        "Device Groups",
        "Devices",
        "Installer Keys",
        "Personal Access Tokens",
        "Servers",
        "Service Accounts",
        "Tenants",
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
      ["Agents", "Devices", "Servers", "User Groups"],
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

  /// <summary>
  /// The header comes from the family prefix, so a family this build has never seen still gets one
  /// instead of throwing or dropping its entries.
  /// </summary>
  [Fact]
  public void GroupForDisplay_WhenFamilyIsUnseen_DerivesItsHeader()
  {
    var groups = PermissionGrouping.GroupForDisplay(
    [
      Entry("widget.read"),
      Entry("widget.assign-widgets"),
      Entry(PermissionNames.DeviceRead),
    ]);

    Assert.Equal(["Devices", "Widgets"], groups.Select(group => group.Label));
    Assert.Equal(
      ["widget.assign-widgets", "widget.read"],
      groups.Single(group => group.Key == "widget").Entries.Select(entry => entry.Name));
  }

  [Fact]
  public void GroupForDisplay_WhenThereAreNoEntries_ReturnsNoGroups()
  {
    Assert.Empty(PermissionGrouping.GroupForDisplay([]));
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
}
