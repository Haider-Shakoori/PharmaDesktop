namespace BusinessOS.Pharmacy.Sync;

public enum SyncState
{
    Pending = 0,
    Syncing = 1,
    Synced = 2,
    Conflict = 3,
    Failed = 4,
}
