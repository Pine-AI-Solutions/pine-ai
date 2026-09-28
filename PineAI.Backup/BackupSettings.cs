namespace PineAI.Backup;

public class BackupSettings
{
    public List<DatabaseSettings> Databases { get; set; } = new();
    public string LocalTempPath { get; set; } = Path.GetTempPath();
    public int IntervalHours { get; set; } = 1;
    public BaleSettings Bale { get; set; } = new();
}

public class DatabaseSettings
{
    /// <summary>SQL Server database name. Also used as the prefix of the .bak/.zip file names.</summary>
    public string DatabaseName { get; set; } = string.Empty;

    /// <summary>Connection string used to run BACKUP DATABASE for this database.</summary>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>Set to false to skip this database without removing it from appsettings.json.</summary>
    public bool Enabled { get; set; } = true;
}

public class BaleSettings
{
    public string BotToken { get; set; } = string.Empty;
    public string ChatId { get; set; } = string.Empty;
}
