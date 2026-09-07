namespace SplitOS.Persistence;

public static class StoragePaths
{
    public static string MachineDataRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "SplitOS", "Data");
    public static string MachineDatabase => Path.Combine(MachineDataRoot, "machine.db");
    public static string MachineBootstrapMarker => Path.Combine(MachineDataRoot, "machine-store.initialized");
    public static string MachineQuarantineMarker => Path.Combine(MachineDataRoot, "machine-store.quarantined.json");

    public static string MachinePersistenceMaintenanceRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "SplitOS",
        "Maintenance",
        "Persistence");
    public static string MachineBackupRoot => Path.Combine(MachinePersistenceMaintenanceRoot, "Backups", "Machine");
    public static string MachineQuarantineRoot => Path.Combine(MachinePersistenceMaintenanceRoot, "Quarantine", "Machine");

    public static string UserDataRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SplitOS", "Data");
    public static string UserDatabase => Path.Combine(UserDataRoot, "user.db");

    public static string UserCacheRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SplitOS", "Cache");
    public static string ProjectionDatabase => Path.Combine(UserCacheRoot, "projection.db");
}
