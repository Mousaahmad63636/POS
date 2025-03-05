// Events/SubscriptionEvents.cs
public class MeterReadingUpdatedEvent
{
    public int CustomerId { get; }
    public decimal NewReading { get; }
    public DateTime ReadingDate { get; }

    public MeterReadingUpdatedEvent(int customerId, decimal newReading, DateTime readingDate)
    {
        CustomerId = customerId;
        NewReading = newReading;
        ReadingDate = readingDate;
    }
}