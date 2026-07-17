using Dalamud.Utility;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;

namespace NoireLib.Configuration.Migrations;

/// <summary>
/// Helper class for executing configuration migrations.
/// </summary>
public static class MigrationExecutor
{
    /// <summary>
    /// Migrations registered at runtime, keyed by configuration type.<br/>
    /// Registration is a public entry point and every plugin sharing this process reaches the same table, so a
    /// registration can run while another plugin is loading a configuration and reading it. The lists are treated as
    /// immutable once published and a registration replaces the whole list rather than appending to the one in the
    /// table, which lets a read take a list that nothing can go on to mutate underneath it.
    /// </summary>
    private static readonly ConcurrentDictionary<Type, List<IConfigMigration>> RuntimeMigrations = new();

    /// <summary>
    /// Discovers and executes all necessary migrations for a configuration type.
    /// </summary>
    /// <param name="configType">The configuration type to migrate.</param>
    /// <param name="json">The JSON string to migrate.</param>
    /// <param name="currentVersion">The current version of the JSON data.</param>
    /// <param name="targetVersion">The target version to migrate to.</param>
    /// <returns>The migrated JSON string, or null if migration failed.</returns>
    public static string? ExecuteMigrations(Type configType, string json, int currentVersion, int targetVersion)
    {
        if (currentVersion == targetVersion)
            return json;

        if (currentVersion > targetVersion)
        {
            NoireLogger.LogWarning($"Cannot migrate from version {currentVersion} to {targetVersion}: downgrade not supported.", "[MigrationExecutor] ");
            return null;
        }

        var migrations = DiscoverMigrations(configType);
        var migrationPath = BuildMigrationPath(migrations, currentVersion, targetVersion);

        if (migrationPath == null || migrationPath.Count == 0)
        {
            NoireLogger.LogWarning($"No migration path found from version {currentVersion} to {targetVersion} for {configType.Name}.", "[MigrationExecutor] ");
            return null;
        }

        NoireLogger.LogInfo($"Executing migration path for {configType.Name}: {string.Join(" -> ", migrationPath.Select(m => $"from V{m.FromVersion} to V{m.ToVersion}"))}");

        return ExecuteMigrationChain(json, migrationPath);
    }

    /// <summary>
    /// Discovers all migrations registered for a configuration type.
    /// </summary>
    /// <param name="configType">The configuration type.</param>
    /// <returns>A list of discovered migrations.</returns>
    private static List<IConfigMigration> DiscoverMigrations(Type configType)
    {
        var migrations = new List<IConfigMigration>();

        var attributes = configType.GetCustomAttributes<ConfigMigrationAttribute>(false);
        foreach (var attribute in attributes)
        {
            try
            {
                if (Activator.CreateInstance(attribute.MigrationType) is IConfigMigration migration)
                {
                    migrations.Add(migration);
                }
            }
            catch (Exception ex)
            {
                NoireLogger.LogError(ex, $"Failed to create migration instance of type {attribute.MigrationType.Name}", "[MigrationExecutor] ");
            }
        }

        var nestedTypes = configType.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic);
        foreach (var nestedType in nestedTypes)
        {
            if (typeof(IConfigMigration).IsAssignableFrom(nestedType) && !nestedType.IsAbstract && !nestedType.IsInterface)
            {
                try
                {
                    if (Activator.CreateInstance(nestedType) is IConfigMigration migration)
                    {
                        migrations.Add(migration);
                    }
                }
                catch (Exception ex)
                {
                    NoireLogger.LogError(ex, $"Failed to create nested migration instance of type {nestedType.Name}", "[MigrationExecutor] ");
                }
            }
        }

        migrations.AddRange(GetRuntimeMigrations(configType));

        return migrations;
    }

    /// <summary>
    /// Builds an optimal migration path from current version to target version.
    /// </summary>
    /// <param name="migrations">Available migrations.</param>
    /// <param name="currentVersion">Starting version.</param>
    /// <param name="targetVersion">Target version.</param>
    /// <returns>A list of migrations to execute in order, or null if no path exists.</returns>
    private static List<IConfigMigration>? BuildMigrationPath(List<IConfigMigration> migrations, int currentVersion, int targetVersion)
    {
        var graph = migrations
            .GroupBy(m => m.FromVersion)
            .ToDictionary(g => g.Key, g => g.ToList());

        var queue = new Queue<(int version, List<IConfigMigration> path)>();
        var visited = new HashSet<int>();

        queue.Enqueue((currentVersion, new List<IConfigMigration>()));
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
                        var newPath = new List<IConfigMigration>(path) { migration };
                        queue.Enqueue((migration.ToVersion, newPath));
                    }
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Executes a chain of migrations in order.
    /// </summary>
    /// <param name="json">The starting JSON string.</param>
    /// <param name="migrations">The migrations to execute in order.</param>
    /// <returns>The final migrated JSON string, or null if any migration failed.</returns>
    private static string? ExecuteMigrationChain(string json, List<IConfigMigration> migrations)
    {
        var currentJson = json;

        foreach (var migration in migrations)
        {
            try
            {
                NoireLogger.LogDebug($"Executing migration {migration.FromVersion} -> {migration.ToVersion}", "[MigrationExecutor] ");

                var document = JObject.Parse(currentJson);
                currentJson = migration.Migrate(document);

                if (currentJson.IsNullOrEmpty())
                {
                    NoireLogger.LogError($"Migration {migration.FromVersion} -> {migration.ToVersion} returned empty JSON", "[MigrationExecutor] ");
                    return null;
                }
            }
            catch (Exception ex)
            {
                NoireLogger.LogError(ex, $"Migration {migration.FromVersion} -> {migration.ToVersion} failed", "[MigrationExecutor] ");
                return null;
            }
        }

        return currentJson;
    }

    /// <summary>
    /// Registers a migration dynamically at runtime.
    /// This is useful for organizing migrations in separate classes outside the config.
    /// </summary>
    /// <param name="configType">The configuration type to register the migration for.</param>
    /// <param name="migration">The migration instance to register.</param>
    public static void RegisterMigration(Type configType, IConfigMigration migration)
    {
        // Copy on write: the replacement list is built off to the side and swapped in as one value, so a concurrent
        // read either sees the table without this migration or with it, and never a list mid-append. AddOrUpdate
        // retries its factory against the current value if another registration lands first, so two registrations
        // racing on the same type both survive.
        RuntimeMigrations.AddOrUpdate(
            configType,
            _ => new List<IConfigMigration> { migration },
            (_, existing) => new List<IConfigMigration>(existing) { migration });

        NoireLogger.LogDebug($"Registered runtime migration {migration.FromVersion} -> {migration.ToVersion} for {configType.Name}", "[MigrationExecutor] ");
    }

    /// <summary>
    /// Gets all runtime-registered migrations for a configuration type.
    /// </summary>
    /// <param name="configType">The configuration type.</param>
    /// <returns>A list of runtime-registered migrations.</returns>
    internal static List<IConfigMigration> GetRuntimeMigrations(Type configType)
    {
        // Copied rather than handed out directly, so that a caller cannot reach the list held in the table and the
        // copy-on-write registration above can keep assuming a published list is never mutated.
        return RuntimeMigrations.TryGetValue(configType, out var migrations)
            ? new List<IConfigMigration>(migrations)
            : new List<IConfigMigration>();
    }

    /// <summary>
    /// Clears all runtime-registered migrations.
    /// </summary>
    public static void ClearRuntimeMigrations()
    {
        RuntimeMigrations.Clear();
        NoireLogger.LogDebug("Cleared all runtime migrations", "[MigrationExecutor] ");
    }
}
