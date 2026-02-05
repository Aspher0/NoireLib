using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace NoireLib.Database.Migrations;

/// <summary>
/// Helper class for executing database migrations.
/// </summary>
public static class DatabaseMigrationExecutor
{
    private static readonly Dictionary<string, List<IDatabaseMigration>> RuntimeMigrations = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Discovers and registers migrations from an assembly using <see cref="DatabaseMigrationAttribute"/>.
    /// </summary>
    /// <param name="assembly">The assembly to scan.</param>
    public static void RegisterMigrationsFromAssembly(Assembly assembly)
    {
        Type[] types;
        try
        {
            types = assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            types = ex.Types.Where(type => type != null).Cast<Type>().ToArray();
        }

        foreach (var type in types)
        {
            if (type.IsAbstract || type.IsInterface)
                continue;

            if (!typeof(IDatabaseMigration).IsAssignableFrom(type))
                continue;

            var attributes = type.GetCustomAttributes<DatabaseMigrationAttribute>(false).ToArray();
            if (attributes.Length == 0)
                continue;

            foreach (var attribute in attributes)
            {
                try
                {
                    if (Activator.CreateInstance(type) is IDatabaseMigration migration)
                        RegisterMigration(attribute.DatabaseName, migration);
                }
                catch (Exception ex)
                {
                    NoireLogger.LogError(ex, $"Failed to create database migration instance of type {type.Name}", "[DatabaseMigrationExecutor] ");
                }
            }
        }
    }

    /// <summary>
    /// Registers a migration for a database name at runtime.
    /// </summary>
    /// <param name="databaseName">The database name.</param>
    /// <param name="migration">The migration instance.</param>
    public static void RegisterMigration(string databaseName, IDatabaseMigration migration)
    {
        if (string.IsNullOrWhiteSpace(databaseName))
            throw new ArgumentException("Database name cannot be null or empty.", nameof(databaseName));

        if (!RuntimeMigrations.TryGetValue(databaseName, out var migrations))
        {
            migrations = new List<IDatabaseMigration>();
            RuntimeMigrations[databaseName] = migrations;
        }

        migrations.Add(migration);
        NoireLogger.LogDebug($"Registered database migration {migration.FromVersion} -> {migration.ToVersion} for {databaseName}", "[DatabaseMigrationExecutor] ");
    }

    /// <summary>
    /// Executes all required migrations for a database.
    /// </summary>
    /// <param name="database">The database instance.</param>
    /// <returns>True if migrations succeeded or were not required; otherwise, false.</returns>
    public static bool ExecuteMigrations(NoireDatabase database)
    {
        var databaseName = database.DatabaseName;
        var migrations = GetMigrations(databaseName).ToList();
        if (migrations.Count == 0)
            return true;

        var currentVersion = database.GetSchemaVersion();
        var targetVersion = migrations.Max(m => m.ToVersion);

        if (currentVersion == targetVersion)
            return true;

        if (currentVersion > targetVersion)
        {
            NoireLogger.LogWarning($"Cannot migrate database {databaseName} from version {currentVersion} to {targetVersion}: downgrade not supported.", "[DatabaseMigrationExecutor] ");
            return false;
        }

        var migrationPath = BuildMigrationPath(migrations, currentVersion, targetVersion);
        if (migrationPath == null || migrationPath.Count == 0)
        {
            NoireLogger.LogWarning($"No migration path found from version {currentVersion} to {targetVersion} for database {databaseName}.", "[DatabaseMigrationExecutor] ");
            return false;
        }

        NoireLogger.LogInfo($"Executing database migration path for {databaseName}");
        return ExecuteMigrationChain(database, migrationPath);
    }

    /// <summary>
    /// Gets all registered migrations for a database name.
    /// </summary>
    /// <param name="databaseName">The database name.</param>
    /// <returns>The migrations.</returns>
    public static IReadOnlyCollection<IDatabaseMigration> GetMigrations(string databaseName)
    {
        if (RuntimeMigrations.TryGetValue(databaseName, out var migrations))
            return migrations.AsReadOnly();

        return Array.Empty<IDatabaseMigration>();
    }

    private static List<IDatabaseMigration>? BuildMigrationPath(List<IDatabaseMigration> migrations, int currentVersion, int targetVersion)
    {
        var graph = migrations
            .GroupBy(m => m.FromVersion)
            .ToDictionary(g => g.Key, g => g.ToList());

        var queue = new Queue<(int version, List<IDatabaseMigration> path)>();
        var visited = new HashSet<int>();

        queue.Enqueue((currentVersion, new List<IDatabaseMigration>()));
        visited.Add(currentVersion);

        while (queue.Count > 0)
        {
            var (version, path) = queue.Dequeue();

            if (version == targetVersion)
                return path;

            if (graph.TryGetValue(version, out var availableMigrations))
            {
                foreach (var migration in availableMigrations)
                {
                    if (!visited.Contains(migration.ToVersion))
                    {
                        visited.Add(migration.ToVersion);
                        var newPath = new List<IDatabaseMigration>(path) { migration };
                        queue.Enqueue((migration.ToVersion, newPath));
                    }
                }
            }
        }

        return null;
    }

    private static bool ExecuteMigrationChain(NoireDatabase database, List<IDatabaseMigration> migrations)
    {
        foreach (var migration in migrations)
        {
            try
            {
                NoireLogger.LogDebug($"Executing database migration {migration.FromVersion} -> {migration.ToVersion} on {database.DatabaseName}", "[DatabaseMigrationExecutor] ");
                migration.Migrate(database);
                database.SetSchemaVersion(migration.ToVersion);
            }
            catch (Exception ex)
            {
                NoireLogger.LogError(ex, $"Database migration {migration.FromVersion} -> {migration.ToVersion} failed on {database.DatabaseName}", "[DatabaseMigrationExecutor] ");
                return false;
            }
        }

        return true;
    }
}
