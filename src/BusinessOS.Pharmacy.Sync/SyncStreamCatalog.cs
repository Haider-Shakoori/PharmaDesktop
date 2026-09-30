using BusinessOS.Pharmacy.Application.Abstractions.Sync;

namespace BusinessOS.Pharmacy.Sync;

public static class SyncStreamCatalog
{
    public const string Medicines = "medicines";
    public const string Customers = "customers";
    public const string Suppliers = "suppliers";
    public const string PurchaseOrders = "purchase_orders";
    public const string GoodsReceipts = "goods_receipts";
    public const string Inventory = "inventory";
    public const string InventoryMovements = "inventory_movements";
    public const string Sales = "sales";
    public const string SaleReturns = "sale_returns";
    public const string Expenses = "expenses";
    public const string DailyClosings = "daily_closings";
    public const string PharmacySettings = "pharmacy_settings";

    public static SyncConsistencyClass GetConsistencyClass(string stream) =>
        stream switch
        {
            Medicines or Customers or Suppliers or Inventory or PharmacySettings =>
                SyncConsistencyClass.VersionedMasterData,

            PurchaseOrders or GoodsReceipts or InventoryMovements or
            Sales or SaleReturns or Expenses or DailyClosings =>
                SyncConsistencyClass.ImmutableTransaction,

            _ => throw new ArgumentOutOfRangeException(
                nameof(stream),
                stream,
                "The sync stream is not registered.")
        };
}
