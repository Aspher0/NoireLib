namespace NoireLib.Helpers;

/// <summary>One world, and where it sits in the world tree.</summary>
/// <param name="RowId">The World row id.</param>
/// <param name="Name">The world's display name.</param>
/// <param name="InternalName">The world's internal name.</param>
/// <param name="DataCenterId">The WorldDCGroupType row id.</param>
/// <param name="DataCenterName">The data centre's name.</param>
/// <param name="RegionId">The physical region hosting it.</param>
/// <param name="IsPublic">Whether players can be on it. Most sheet rows are not.</param>
public sealed record WorldInfo(
    ushort RowId,
    string Name,
    string InternalName,
    uint DataCenterId,
    string DataCenterName,
    byte RegionId,
    bool IsPublic);
