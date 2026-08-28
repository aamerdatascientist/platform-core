using MediatR;
using Platform.Application.Common.Interfaces;

namespace Platform.Application.Analytics.Queries.GetStockMovementBreakdown;

public enum StockMovementType
{
    GoodsReceipt,
    MaterialIssue,
    StockTransfer,
    StockAdjustment
}

public record GetStockMovementBreakdownQuery : IRequest<IReadOnlyList<StockMovementBreakdownDto>>;

public record StockMovementBreakdownDto(
    StockMovementType MovementType, Guid LocationId, string LocationName,
    Guid MaterialId, string MaterialCode, string MaterialName, decimal TotalQuantity);

/// <summary>
/// One flat, already-grouped (movement type, location, material) -> quantity list - the
/// frontend's tree layout nests it into movement-type branches, then destination nodes,
/// then material leaves. See the Phase 0 audit: none of the four movement forms has a real
/// Lookup to Projects, so "destination" here is always a Location.
///
/// Per-form quantity/location column mapping - deliberately explicit, not a shared column
/// name across tables (they're all named differently):
///  - Goods Receipt: quantity_received / location - material arrives here, stock increases.
///  - Material Issue: quantity_issued / from_location - the only real Lookup on this form is
///    the location stock leaves FROM, not a true destination (its only destination-shaped
///    field, project_work_order, is optional free text with no referential integrity - see
///    the audit - so it's not usable for grouping).
///  - Stock Transfer: quantity / to_location - the one with a genuine destination; from_location
///    (the source) is not represented in this breakdown.
///  - Stock Adjustment: quantity_adjusted / location - a single correction point, not a
///    movement pair. Summed and passed through exactly as stored: nothing here assumes a
///    sign convention (positive = found vs. negative = shortage, or the reverse) - that's
///    unconfirmed against real data, per the audit.
/// </summary>
public class GetStockMovementBreakdownQueryHandler
    : IRequestHandler<GetStockMovementBreakdownQuery, IReadOnlyList<StockMovementBreakdownDto>>
{
    private static readonly (string FormCode, StockMovementType MovementType, string QuantityColumn, string LocationColumn)[] MovementForms =
    {
        ("goods-receipt", StockMovementType.GoodsReceipt, "quantity_received", "location"),
        ("material-issue", StockMovementType.MaterialIssue, "quantity_issued", "from_location"),
        ("stock-transfer", StockMovementType.StockTransfer, "quantity", "to_location"),
        ("stock-adjustment", StockMovementType.StockAdjustment, "quantity_adjusted", "location"),
    };

    private readonly IApplicationDbContext _db;
    private readonly IDynamicDataRepository _dynamicDataRepository;
    private readonly IExecutiveOverviewRepository _executiveOverviewRepository;

    public GetStockMovementBreakdownQueryHandler(
        IApplicationDbContext db, IDynamicDataRepository dynamicDataRepository, IExecutiveOverviewRepository executiveOverviewRepository)
    {
        _db = db;
        _dynamicDataRepository = dynamicDataRepository;
        _executiveOverviewRepository = executiveOverviewRepository;
    }

    public async Task<IReadOnlyList<StockMovementBreakdownDto>> Handle(
        GetStockMovementBreakdownQuery request, CancellationToken cancellationToken)
    {
        var materialsForm = await ExecutiveOverviewSupport.FindPublishedFormAsync(_db, "materials", cancellationToken);
        var materialsById = materialsForm is null
            ? new Dictionary<Guid, (string Code, string Name)>()
            : await ExecutiveOverviewSupport.ResolveMasterDataAsync(
                _dynamicDataRepository, materialsForm, "item_code", "description", cancellationToken);

        var locationsForm = await ExecutiveOverviewSupport.FindPublishedFormAsync(_db, "locations", cancellationToken);
        var locationsById = locationsForm is null
            ? new Dictionary<Guid, (string Code, string Name)>()
            : await ExecutiveOverviewSupport.ResolveMasterDataAsync(
                _dynamicDataRepository, locationsForm, "location_name", "location_name", cancellationToken);

        var results = new List<StockMovementBreakdownDto>();

        foreach (var (formCode, movementType, quantityColumn, locationColumn) in MovementForms)
        {
            var form = await ExecutiveOverviewSupport.FindPublishedFormAsync(_db, formCode, cancellationToken);
            if (form is null) continue;

            var rows = await _executiveOverviewRepository.GetMovementQuantitiesAsync(
                form.TableName!, quantityColumn, locationColumn, cancellationToken);

            foreach (var row in rows)
            {
                var (materialCode, materialName) = materialsById.GetValueOrDefault(row.MaterialId, (string.Empty, string.Empty));
                var (locationName, _) = locationsById.GetValueOrDefault(row.LocationId, (string.Empty, string.Empty));

                results.Add(new StockMovementBreakdownDto(
                    movementType, row.LocationId, locationName, row.MaterialId, materialCode, materialName, row.TotalQuantity));
            }
        }

        return results;
    }
}
