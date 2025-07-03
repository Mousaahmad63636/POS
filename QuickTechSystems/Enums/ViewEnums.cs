namespace QuickTechSystems.WPF.Enums
{
    public enum StockStatus
    {
        All = 0,
        OutOfStock = 1,
        LowStock = 2,
        AdequateStock = 3,
        Overstocked = 4
    }

    public enum SortOption
    {
        Name = 0,
        PurchasePrice = 1,
        SalePrice = 2,
        StockLevel = 3,
        CreationDate = 4,
        ProfitMargin = 5,
        TotalValue = 6,
        LastUpdated = 7
    }
}