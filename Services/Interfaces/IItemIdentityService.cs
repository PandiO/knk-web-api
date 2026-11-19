using knkwebapi_v2.Dtos;

namespace knkwebapi_v2.Services.Interfaces;

/// <summary>Item identity (PandiO/knk-workspace#141): the material → blueprint map, shadow generation, match moves,
/// conversion counts and the admin report.</summary>
public interface IItemIdentityService
{
    Task<ItemIdentityMapDto> GetMaterialMapAsync(CancellationToken ct = default);

    Task<ItemShadowGenerationResultDto> GenerateShadowsAsync(CancellationToken ct = default);

    /// <summary>Moves the material's match to <paramref name="itemBlueprintId"/> (null clears it). 404 via
    /// <see cref="KeyNotFoundException"/> for an unknown material or blueprint.</summary>
    Task<ItemIdentityMatchResultDto> SetMatchAsync(int materialRefId, int? itemBlueprintId, CancellationToken ct = default);

    /// <summary>Adds the counts to their day's rows; returns how many lines were applied. 400 via <see cref="ArgumentException"/>.</summary>
    Task<int> RecordConversionCountsAsync(ItemConversionCountsDto dto, CancellationToken ct = default);

    Task<ItemIdentityReportDto> GetReportAsync(int days, CancellationToken ct = default);
}
