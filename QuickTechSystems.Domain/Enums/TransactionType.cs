namespace QuickTechSystems.Domain.Enums
{
    public enum TransactionType
    {
        Sale,
        Return,
        Purchase,
        Adjustment,
        Payment
    }

    public enum TransactionStatus
    {
        Pending,
        Completed,
        Cancelled
    }
}