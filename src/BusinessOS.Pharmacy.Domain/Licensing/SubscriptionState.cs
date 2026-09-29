namespace BusinessOS.Pharmacy.Domain.Licensing;

public enum SubscriptionState
{
    Pending = 0,
    Trial = 1,
    Active = 2,
    GracePeriod = 3,
    Expired = 4,
    Suspended = 5,
    Cancelled = 6,
}
