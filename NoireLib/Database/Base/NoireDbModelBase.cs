using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text.Json;

namespace NoireLib.Database;


/// <summary>
/// Base class representing a database model with change tracking, validation, and relationship management.<br/>
/// To make it simpler, this class represents a table row in the database.<br/>
/// Models can be filled with values, validated, and updated or saved to the database as a new row.<br/>
/// Relationships between models are supported through defined relation configurations.<br/>
/// </summary>
public abstract class NoireDbModelBase
{
    private static readonly ConcurrentDictionary<string, IReadOnlyCollection<string>> TableColumnsCache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Initializes a new instance of the <see cref="NoireDbModelBase"/> class.
    /// </summary>
    protected NoireDbModelBase()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="NoireDbModelBase"/> class with initial column values.
    /// </summary>
    /// <param name="columns">The initial column values. Represented as a dictionary of column names to values.</param>
    protected NoireDbModelBase(IReadOnlyDictionary<string, object?> columns)
    {
        Fill(columns);
    }

    /// <summary>
    /// Gets a value indicating whether the model (row) exists in the database.
    /// </summary>
    public bool Exists { get; protected set; }

    /// <summary>
    /// Gets the database name used by the model.
    /// </summary>
    protected abstract string DatabaseName { get; }

    /// <summary>
    /// Gets the table name used by the model.
    /// </summary>
    protected abstract string? TableName { get; }

    /// <summary>
    /// Gets the directory override for the database file path.
    /// </summary>
    protected virtual string? DatabaseDirectoryOverride => null;

    /// <summary>
    /// Gets a value indicating whether the database should load at plugin initialization.
    /// </summary>
    protected virtual bool LoadDatabaseOnInit { get; } = true;

    /// <summary>
    /// Gets the resolved table name, falling back to the default when not provided.
    /// </summary>
    protected string ResolvedTableName => string.IsNullOrWhiteSpace(TableName) ? GetDefaultTableName() : TableName;

    /// <summary>
    /// Gets the primary key column name.
    /// </summary>
    protected abstract string PrimaryKey { get; }

    /// <summary>
    /// Gets the column cast definitions.<br/>
    /// Represented as a dictionary where the key is the column name and the value is the target type for casting.<br/>
    /// </summary>
    protected virtual IReadOnlyDictionary<string, DbColumnCast> Casts =>
        new Dictionary<string, DbColumnCast>();

    /// <summary>
    /// Gets the relation definitions for the model.<br/>
    /// Represented as a dictionary where the key is the relation name and the value is the relation configuration.
    /// </summary>
    protected virtual IReadOnlyDictionary<string, DbRelationDefinition> Relations =>
        new Dictionary<string, DbRelationDefinition>();

    /// <summary>
    /// Gets the validation rules for model columns.<br/>
    /// Represented as a dictionary where the key is the column name and the value is a list of validation rules to apply to that column.
    /// </summary>
    protected virtual IReadOnlyDictionary<string, IReadOnlyList<DbValidationRuleDefinition>> ValidationRules =>
        new Dictionary<string, IReadOnlyList<DbValidationRuleDefinition>>();

    /// <summary>
    /// Gets the column definitions used when creating the table schema.
    /// </summary>
    protected virtual IReadOnlyDictionary<string, DbColumnDefinition> Columns => new Dictionary<string, DbColumnDefinition>();

    /// <summary>
    /// Stores the current column values for the model.
    /// </summary>
    protected readonly Dictionary<string, object?> ColumnValues = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Stores the original column values for change tracking.
    /// </summary>
    protected readonly Dictionary<string, object?> OriginalColumnValues = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Stores column changes for the current model instance.
    /// </summary>
    protected readonly Dictionary<string, (object? From, object? To)> ColumnChanges = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Stores validation errors keyed by column name.
    /// </summary>
    protected readonly Dictionary<string, List<string>> Errors = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Fills the model with the provided column values and tracks changes.<br/>
    /// Use this over <see cref="FillUnsafe"/> when you want to ensure that change tracking is properly maintained.<br/>
    /// </summary>
    /// <param name="columns">The column values to apply.</param>
    /// <returns>The current model instance.</returns>
    public NoireDbModelBase Fill(IReadOnlyDictionary<string, object?> columns)
    {
        foreach (var (key, value) in columns)
        {
            if (!ColumnValues.TryGetValue(key, out var existing) || !Equals(existing, value))
                ColumnChanges[key] = (existing, value);

            ColumnValues[key] = CastColumn(key, value);
        }

        return this;
    }

    /// <summary>
    /// Fills the model with the provided column values without tracking changes.<br/>
    /// Use this over <see cref="Fill"/> for hydration from the database or when you want to bypass change tracking for any reason.
    /// </summary>
    /// <param name="columns">The column values to apply.</param>
    /// <returns>The current model instance.</returns>
    public NoireDbModelBase FillUnsafe(IReadOnlyDictionary<string, object?> columns)
    {
        foreach (var (key, value) in columns)
            ColumnValues[key] = CastColumn(key, value);

        SyncOriginal();
        return this;
    }

    /// <summary>
    /// Synchronizes the original column snapshot with current values.<br/>
    /// In short, it marks the model as clean by clearing the change tracking and updating the original values to match the current columns.
    /// </summary>
    /// <returns>The current model instance.</returns>
    public NoireDbModelBase SyncOriginal()
    {
        OriginalColumnValues.Clear();
        foreach (var (key, value) in ColumnValues)
            OriginalColumnValues[key] = value;

        ColumnChanges.Clear();
        return this;
    }

    /// <summary>
    /// Gets the current model column values.
    /// </summary>
    /// <returns>A read-only dictionary of column names and values.</returns>
    public IReadOnlyDictionary<string, object?> GetColumns() => ColumnValues;

    /// <summary>
    /// Gets the tracked column changes.
    /// </summary>
    /// <returns>A read-only dictionary of column names and their original and current values.</returns>
    public IReadOnlyDictionary<string, (object? From, object? To)> GetChanges() => ColumnChanges;

    /// <summary>
    /// Gets the validation errors for the model.
    /// </summary>
    /// <returns>A read-only dictionary of column names and their associated error messages.</returns>
    public IReadOnlyDictionary<string, List<string>> GetErrors() => Errors;

    /// <summary>
    /// Returns whether the model has pending changes.
    /// </summary>
    /// <param name="attribute">An optional column name to check.</param>
    /// <returns>True if there are changes for the specified column or any column if none specified; otherwise, false.</returns>
    public bool IsDirty(string? attribute = null)
    {
        if (!string.IsNullOrWhiteSpace(attribute))
            return ColumnChanges.ContainsKey(attribute);

        return ColumnChanges.Count > 0;
    }

    /// <summary>
    /// Gets a typed column value.
    /// </summary>
    /// <typeparam name="T">The column type.</typeparam>
    /// <param name="key">The column name.</param>
    /// <returns>The column value cast to the specified type, or default if not found or cannot be cast.</returns>
    public T? GetColumn<T>(string key)
    {
        if (!ColumnValues.TryGetValue(key, out var value) || value == null)
            return default;

        if (value is T typed)
            return typed;

        return (T)Convert.ChangeType(value, typeof(T), CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Sets a column value and tracks changes.
    /// </summary>
    /// <param name="key">The column name.</param>
    /// <param name="value">The new column value.</param>
    public void SetColumn(string key, object? value)
    {
        if (!ColumnValues.TryGetValue(key, out var existing) || !Equals(existing, value))
            ColumnChanges[key] = (existing, value);

        ColumnValues[key] = CastColumn(key, value);
    }

    /// <summary>
    /// Sets multiple column values and tracks changes.
    /// </summary>
    /// <param name="columns">The column values to set.</param>
    /// <returns>The current model instance.</returns>
    public NoireDbModelBase SetColumns(IReadOnlyDictionary<string, object?> columns)
    {
        foreach (var (key, value) in columns)
            SetColumn(key, value);

        return this;
    }

    /// <summary>
    /// Gets or sets a column value by name.
    /// </summary>
    /// <param name="key">The column name.</param>
    public object? this[string key]
    {
        get => ColumnValues.TryGetValue(key, out var value) ? value : null;
        set => SetColumn(key, value);
    }

    /// <summary>
    /// Creates a new model instance with only the specified columns.
    /// </summary>
    /// <param name="columns">The column names to include.</param>
    /// <returns>A new instance of the model containing only the specified columns.</returns>
    public NoireDbModelBase Only(IEnumerable<string> columns)
    {
        var instance = (NoireDbModelBase)Activator.CreateInstance(GetType())!;

        foreach (var column in columns)
        {
            if (ColumnValues.TryGetValue(column, out var value))
                instance.ColumnValues[column] = value;
        }

        instance.Exists = Exists;
        instance.SyncOriginal();
        return instance;
    }

    /// <summary>
    /// Creates a new model instance without the specified columns.
    /// </summary>
    /// <param name="columns">The column names to exclude.</param>
    /// <returns>A new instance of the model excluding the specified columns.</returns>
    public NoireDbModelBase Except(IEnumerable<string> columns)
    {
        var instance = (NoireDbModelBase)Activator.CreateInstance(GetType())!;

        foreach (var (key, value) in ColumnValues)
        {
            if (!columns.Contains(key, StringComparer.OrdinalIgnoreCase))
                instance.ColumnValues[key] = value;
        }

        instance.Exists = Exists;
        instance.SyncOriginal();
        return instance;
    }

    /// <summary>
    /// Saves the model to the database.
    /// </summary>
    /// <returns>True if the save operation was successful; otherwise, false.</returns>
    public bool Save()
    {
        EnsureTableCreated();

        if (!Validate())
            return false;

        var database = GetDb();
        var columns = GetColumnsForDb();

        if (columns.TryGetValue(PrimaryKey, out var primaryValue) && primaryValue == null)
            columns.Remove(PrimaryKey);

        if (Exists)
        {
            if (!ColumnValues.ContainsKey(PrimaryKey))
                return false;

            var updateData = new Dictionary<string, object?>();
            foreach (var (key, change) in ColumnChanges)
            {
                if (key.Equals(PrimaryKey, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (columns.TryGetValue(key, out var value))
                    updateData[key] = value;
            }

            if (updateData.Count > 0)
            {
                var where = new Dictionary<string, object?>
                {
                    [PrimaryKey] = ColumnValues[PrimaryKey]
                };

                var result = database.Update(ResolvedTableName, updateData, where);
                if (result <= 0)
                    return false;
            }
        }
        else
        {
            var id = database.Insert(ResolvedTableName, columns);
            if (id <= 0)
                return false;

            ColumnValues[PrimaryKey] = id;
            Exists = true;
        }

        SyncOriginal();
        return true;
    }

    /// <summary>
    /// Deletes the model from the database.
    /// </summary>
    /// <returns>True if the delete operation was successful; otherwise, false.</returns>
    public bool Delete()
    {
        if (!Exists || !ColumnValues.TryGetValue(PrimaryKey, out var id))
            return false;

        var where = new Dictionary<string, object?>
        {
            [PrimaryKey] = id
        };

        var result = GetDb().Delete(ResolvedTableName, where);
        if (result <= 0)
            return false;

        Exists = false;
        return true;
    }

    /// <summary>
    /// Refreshes the model values from the database.
    /// </summary>
    /// <returns>True if the refresh operation was successful; otherwise, false.</returns>
    public bool Refresh()
    {
        if (!Exists || !ColumnValues.TryGetValue(PrimaryKey, out var id))
            return false;

        var refreshed = InvokeFindBy(GetType(), new Dictionary<string, object?> { [PrimaryKey] = id }) as NoireDbModelBase;
        if (refreshed == null)
            return false;

        ColumnValues.Clear();
        foreach (var (key, value) in refreshed.ColumnValues)
            ColumnValues[key] = value;

        SyncOriginal();
        return true;
    }

    /// <summary>
    /// Validates the model columns.
    /// </summary>
    /// <returns>True if validation passes; otherwise, false with <see cref="Errors"/> populated.</returns>
    public bool Validate()
    {
        Errors.Clear();

        if (ValidationRules.Count == 0)
            return true;

        foreach (var (column, rules) in ValidationRules)
        {
            var value = ColumnValues.TryGetValue(column, out var v) ? v : null;
            foreach (var rule in rules)
            {
                if (!ValidateColumn(column, value, rule))
                    break;
            }
        }

        return Errors.Count == 0;
    }

    /// <summary>
    /// Serializes the model columns to JSON.
    /// </summary>
    /// <param name="options">Optional JSON serialization options.</param>
    /// <returns>A JSON string representing the model columns.</returns>
    public string ToJson(JsonSerializerOptions? options = null)
    {
        return JsonSerializer.Serialize(ToDictionary(), options ?? new JsonSerializerOptions { WriteIndented = true });
    }

    /// <summary>
    /// Returns a dictionary representation of the model columns.
    /// </summary>
    /// <returns>A read-only dictionary of column names and values.</returns>
    public IReadOnlyDictionary<string, object?> ToDictionary() => ColumnValues;

    /// <summary>
    /// Validates a single column using the specified rule.
    /// </summary>
    /// <param name="column">The column name.</param>
    /// <param name="value">The column value.</param>
    /// <param name="ruleDefinition">The validation rule definition.</param>
    /// <returns>True if the column passes validation; otherwise, false with an error added to <see cref="Errors"/>.</returns>
    protected virtual bool ValidateColumn(string column, object? value, DbValidationRuleDefinition ruleDefinition)
    {
        var parameters = ruleDefinition.Parameters;

        switch (ruleDefinition.Rule)
        {
            case DbValidationRule.Required:
                if (value == null || (value is string str && string.IsNullOrEmpty(str)))
                {
                    AddError(column, $"The {column} field is required.");
                    return false;
                }
                break;
            case DbValidationRule.Numeric:
                if (value != null && !decimal.TryParse(value.ToString(), out _))
                {
                    AddError(column, $"The {column} field must be a number.");
                    return false;
                }
                break;
            case DbValidationRule.Integer:
                if (value != null && !int.TryParse(value.ToString(), out _))
                {
                    AddError(column, $"The {column} field must be an integer.");
                    return false;
                }
                break;
            case DbValidationRule.Min:
                if (parameters.Count > 0 && int.TryParse(parameters[0], out var min))
                {
                    if (value is string strValue && strValue.Length < min)
                    {
                        AddError(column, $"The {column} field must be at least {min} characters.");
                        return false;
                    }

                    if (value != null && decimal.TryParse(value.ToString(), out var number) && number < min)
                    {
                        AddError(column, $"The {column} field must be at least {min}.");
                        return false;
                    }
                }
                break;
            case DbValidationRule.Max:
                if (parameters.Count > 0 && int.TryParse(parameters[0], out var max))
                {
                    if (value is string strMaxValue && strMaxValue.Length > max)
                    {
                        AddError(column, $"The {column} field must not exceed {max} characters.");
                        return false;
                    }

                    if (value != null && decimal.TryParse(value.ToString(), out var maxNumber) && maxNumber > max)
                    {
                        AddError(column, $"The {column} field must not exceed {max}.");
                        return false;
                    }
                }
                break;
            case DbValidationRule.Unique:
                if (value != null)
                {
                    var count = CountMatching(column, value);
                    if (count > 0)
                    {
                        AddError(column, $"The {column} field value is already in use.");
                        return false;
                    }
                }
                break;
            case DbValidationRule.Date:
                if (value != null && !DateTime.TryParse(value.ToString(), out _))
                {
                    AddError(column, $"The {column} field must be a valid date.");
                    return false;
                }
                break;
            case DbValidationRule.In:
                if (value != null && parameters.Count > 0 && !parameters.Contains(value.ToString()))
                {
                    AddError(column, $"The {column} field must be one of the following values: {string.Join(", ", parameters)}.");
                    return false;
                }
                break;
            case DbValidationRule.Regex:
                if (parameters.Count > 0 && value is string regexValue)
                {
                    if (!System.Text.RegularExpressions.Regex.IsMatch(regexValue, parameters[0]))
                    {
                        AddError(column, $"The {column} field format is invalid.");
                        return false;
                    }
                }
                break;
        }

        return true;
    }

    /// <summary>
    /// Gets a related model or collection based on the relation configuration.
    /// </summary>
    /// <param name="relation">The relation name.</param>
    /// <returns>The related model data.</returns>
    protected virtual object? GetRelation(string relation)
    {
        if (!Relations.TryGetValue(relation, out var config))
            throw new InvalidOperationException($"Relation '{relation}' is not defined.");

        return config.Type switch
        {
            DbRelationType.HasOne => GetHasOneRelation(config),
            DbRelationType.HasMany => GetHasManyRelation(config),
            DbRelationType.BelongsTo => GetBelongsToRelation(config),
            DbRelationType.BelongsToMany => GetBelongsToManyRelation(config),
            _ => throw new InvalidOperationException($"Relation type '{config.Type}' is not supported.")
        };
    }

    /// <summary>
    /// Resolves a has-one relation.
    /// </summary>
    /// <param name="config">The relation configuration.</param>
    /// <returns>The related model, or null if not found.</returns>
    protected virtual object? GetHasOneRelation(DbRelationDefinition config)
    {
        var foreignKey = config.ForeignKey ?? GetDefaultForeignKeyName(GetType());
        var localKey = config.LocalKey ?? PrimaryKey;

        if (!ColumnValues.TryGetValue(localKey, out var localValue))
            return null;

        return InvokeFindBy(config.RelatedModelType, new Dictionary<string, object?> { [foreignKey] = localValue });
    }

    /// <summary>
    /// Resolves a has-many relation.
    /// </summary>
    /// <param name="config">The relation configuration.</param>
    /// <returns>The related model collection.</returns>
    protected virtual object GetHasManyRelation(DbRelationDefinition config)
    {
        var foreignKey = config.ForeignKey ?? GetDefaultForeignKeyName(GetType());
        var localKey = config.LocalKey ?? PrimaryKey;

        if (!ColumnValues.TryGetValue(localKey, out var localValue))
            return Array.Empty<object>();

        return InvokeFindAllBy(config.RelatedModelType, new Dictionary<string, object?> { [foreignKey] = localValue });
    }

    /// <summary>
    /// Resolves a belongs-to relation.
    /// </summary>
    /// <param name="config">The relation configuration.</param>
    /// <returns>The related model, or null if not found.</returns>
    protected virtual object? GetBelongsToRelation(DbRelationDefinition config)
    {
        var ownerKey = config.OwnerKey ?? GetDefaultForeignKeyName(config.RelatedModelType);

        if (!ColumnValues.TryGetValue(ownerKey, out var ownerValue))
            return null;

        return InvokeFind(config.RelatedModelType, ownerValue!);
    }

    /// <summary>
    /// Resolves a belongs-to-many relation.
    /// </summary>
    /// <param name="config">The relation configuration.</param>
    /// <returns>The related model collection.</returns>
    protected virtual object GetBelongsToManyRelation(DbRelationDefinition config)
    {
        if (string.IsNullOrWhiteSpace(config.PivotTable))
            throw new InvalidOperationException("Pivot table must be defined for belongsToMany relations.");

        var relatedModel = (NoireDbModelBase)Activator.CreateInstance(config.RelatedModelType)!;
        var foreignPivotKey = config.ForeignPivotKey ?? GetDefaultForeignKeyName(GetType());
        var relatedPivotKey = config.RelatedPivotKey ?? GetDefaultForeignKeyName(config.RelatedModelType);
        var parentKey = config.ParentKey ?? PrimaryKey;

        if (!ColumnValues.TryGetValue(parentKey, out var parentValue))
            return Array.Empty<object>();

        var relatedTable = relatedModel.ResolvedTableName;
        var primaryKey = relatedModel.PrimaryKey;

        var sql = $"SELECT r.* FROM {NoireDatabase.EscapeColumn(relatedTable)} r INNER JOIN {NoireDatabase.EscapeColumn(config.PivotTable)} p ON r.{NoireDatabase.EscapeColumn(primaryKey)} = p.{NoireDatabase.EscapeColumn(relatedPivotKey)} WHERE p.{NoireDatabase.EscapeColumn(foreignPivotKey)} = @p0";
        var results = GetDb().FetchAll(sql, new[] { parentValue });

        return results.Select(result => CreateModelFromType(config.RelatedModelType, result)).ToList();
    }

    /// <summary>
    /// Gets the database instance for this model.
    /// </summary>
    /// <returns>The database instance.</returns>
    protected NoireDatabase GetDb()
    {
        if (!string.IsNullOrWhiteSpace(DatabaseDirectoryOverride))
            NoireDatabase.SetDatabaseDirectoryOverride(DatabaseName, DatabaseDirectoryOverride);

        return NoireDatabase.GetInstance(DatabaseName);
    }

    /// <summary>
    /// Ensures the database table exists for the model.
    /// </summary>
    protected void EnsureTableCreated()
    {
        var definitions = GetColumnDefinitions();
        if (definitions.Count == 0)
            return;

        var columns = definitions.Values.Select(BuildColumnDefinition).ToArray();
        var sql = $"CREATE TABLE IF NOT EXISTS {NoireDatabase.EscapeColumn(ResolvedTableName)} ({string.Join(", ", columns)})";
        GetDb().Execute(sql);

        TableColumnsCache.TryRemove(GetCacheKey(), out _);
    }

    /// <summary>
    /// Gets column definitions for schema creation.
    /// </summary>
    /// <returns>A dictionary of column definitions.</returns>
    protected IReadOnlyDictionary<string, DbColumnDefinition> GetColumnDefinitions()
    {
        if (Columns.Count > 0)
            return Columns;

        var definitions = new Dictionary<string, DbColumnDefinition>(StringComparer.OrdinalIgnoreCase);
        var properties = GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance);

        foreach (var property in properties)
        {
            if (!property.CanRead || !property.CanWrite)
                continue;

            if (property.DeclaringType == typeof(NoireDbModelBase))
                continue;

            var attribute = property.GetCustomAttribute<NoireDbColumnAttribute>();
            var name = attribute?.Name ?? property.Name;
            var type = attribute?.Type ?? MapTypeToSqlite(property.PropertyType);
            var isPrimaryKey = attribute?.IsPrimaryKey ?? name.Equals(PrimaryKey, StringComparison.OrdinalIgnoreCase);
            var isNullable = attribute?.IsNullable ?? IsNullableProperty(property.PropertyType);
            var isAutoIncrement = attribute?.IsAutoIncrement ?? (isPrimaryKey && IsIntegerProperty(property.PropertyType));

            definitions[name] = new DbColumnDefinition(name, type)
            {
                IsPrimaryKey = isPrimaryKey,
                IsNullable = isNullable,
                IsAutoIncrement = isAutoIncrement,
                DefaultValue = attribute?.DefaultValue
            };
        }

        if (!definitions.ContainsKey(PrimaryKey))
        {
            definitions[PrimaryKey] = new DbColumnDefinition(PrimaryKey, "INTEGER")
            {
                IsPrimaryKey = true,
                IsNullable = false,
                IsAutoIncrement = true
            };
        }

        return definitions;
    }

    /// <summary>
    /// Gets the column names from the database table.
    /// </summary>
    /// <returns>The column names in the table.</returns>
    protected IReadOnlyCollection<string> GetDatabaseColumns()
    {
        var cacheKey = GetCacheKey();
        if (TableColumnsCache.TryGetValue(cacheKey, out var cached))
            return cached;

        var results = GetDb().FetchAll($"PRAGMA table_info({NoireDatabase.EscapeColumn(ResolvedTableName)})");
        var columns = results
            .Where(row => row.TryGetValue("name", out _))
            .Select(row => row["name"]?.ToString() ?? string.Empty)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToArray();

        TableColumnsCache[cacheKey] = columns;
        return columns;
    }

    /// <summary>
    /// Gets column values formatted for database insertion.
    /// </summary>
    /// <returns>A dictionary of column values formatted for the database.</returns>
    protected Dictionary<string, object?> GetColumnsForDb()
    {
        var columns = GetDatabaseColumns();
        var columnValues = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        foreach (var (key, value) in ColumnValues)
        {
            if (!columns.Contains(key, StringComparer.OrdinalIgnoreCase))
                continue;

            if (value is DateTime dateTime)
            {
                columnValues[key] = dateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            }
            else if (value is System.Collections.IEnumerable && value is not string)
            {
                columnValues[key] = JsonSerializer.Serialize(value);
            }
            else
            {
                columnValues[key] = value;
            }
        }

        return columnValues;
    }

    /// <summary>
    /// Casts a column value according to the model's cast definitions.
    /// </summary>
    /// <param name="key">The column name.</param>
    /// <param name="value">The column value.</param>
    /// <returns>The casted value.</returns>
    protected object? CastColumn(string key, object? value)
    {
        if (value == null)
            return null;

        if (!Casts.TryGetValue(key, out var type))
            return value;

        return type switch
        {
            DbColumnCast.Integer => Convert.ToInt32(value, CultureInfo.InvariantCulture),
            DbColumnCast.Float => Convert.ToDouble(value, CultureInfo.InvariantCulture),
            DbColumnCast.String => Convert.ToString(value, CultureInfo.InvariantCulture),
            DbColumnCast.Boolean => Convert.ToBoolean(value, CultureInfo.InvariantCulture),
            DbColumnCast.Array => value is string text ? JsonSerializer.Deserialize<object[]>(text) : value,
            DbColumnCast.Json => value is string json ? JsonSerializer.Deserialize<object>(json) : JsonSerializer.Serialize(value),
            DbColumnCast.DateTime => value is DateTime dateTime ? dateTime : DateTime.Parse(value.ToString()!, CultureInfo.InvariantCulture),
            DbColumnCast.Timestamp => value is long longValue ? DateTimeOffset.FromUnixTimeSeconds(longValue).DateTime : DateTimeOffset.FromUnixTimeSeconds(Convert.ToInt64(value, CultureInfo.InvariantCulture)).DateTime,
            _ => value
        };
    }

    /// <summary>
    /// Adds a validation error for a column.
    /// </summary>
    /// <param name="column">The column name.</param>
    /// <param name="message">The error message.</param>
    protected void AddError(string column, string message)
    {
        if (!Errors.TryGetValue(column, out var list))
        {
            list = new List<string>();
            Errors[column] = list;
        }

        list.Add(message);
    }

    /// <summary>
    /// Counts rows matching a column value, excluding the current row when applicable.
    /// </summary>
    /// <param name="column">The column name.</param>
    /// <param name="value">The column value.</param>
    /// <returns>The number of matching rows.</returns>
    protected int CountMatching(string column, object value)
    {
        var sql = $"SELECT COUNT(*) FROM {NoireDatabase.EscapeColumn(ResolvedTableName)} WHERE {NoireDatabase.EscapeColumn(column)} = @p0";
        var parameters = new List<object?> { value };

        if (ColumnValues.TryGetValue(PrimaryKey, out var id) && id != null)
        {
            sql += $" AND {NoireDatabase.EscapeColumn(PrimaryKey)} != @p1";
            parameters.Add(id);
        }

        var result = GetDb().FetchScalar(sql, parameters);
        return result == null ? 0 : Convert.ToInt32(result);
    }

    /// <summary>
    /// Gets the default table name derived from the model type.
    /// </summary>
    /// <returns>The default table name.</returns>
    protected string GetDefaultTableName()
    {
        var className = GetType().Name;
        if (className.EndsWith("Model", StringComparison.OrdinalIgnoreCase))
            className = className[..^5];

        return ToSnakeCase(className) + "s";
    }

    /// <summary>
    /// Converts a string to snake_case.
    /// </summary>
    /// <param name="value">The input string.</param>
    /// <returns>The snake_case string.</returns>
    protected static string ToSnakeCase(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return value;

        var builder = new System.Text.StringBuilder();
        for (var i = 0; i < value.Length; i++)
        {
            var ch = value[i];
            if (char.IsUpper(ch))
            {
                if (i > 0)
                    builder.Append('_');
                builder.Append(char.ToLowerInvariant(ch));
            }
            else
            {
                builder.Append(ch);
            }
        }

        return builder.ToString();
    }

    /// <summary>
    /// Determines whether a type is nullable.
    /// </summary>
    /// <param name="type">The type to check.</param>
    /// <returns>True if the type is nullable; otherwise, false.</returns>
    protected static bool IsNullableProperty(Type type)
    {
        return !type.IsValueType || Nullable.GetUnderlyingType(type) != null;
    }

    /// <summary>
    /// Determines whether a type represents an integer value.
    /// </summary>
    /// <param name="type">The type to check.</param>
    /// <returns>True if the type represents an integer; otherwise, false.</returns>
    protected static bool IsIntegerProperty(Type type)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        return type == typeof(int) || type == typeof(long) || type == typeof(short) || type == typeof(byte);
    }

    /// <summary>
    /// Maps a CLR type to a SQLite column type.
    /// </summary>
    /// <param name="type">The CLR type to map.</param>
    /// <returns>The SQLite type name.</returns>
    protected static string MapTypeToSqlite(Type type)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;

        if (type == typeof(int) || type == typeof(long) || type == typeof(short) || type == typeof(byte) || type == typeof(bool))
            return "INTEGER";

        if (type == typeof(float) || type == typeof(double) || type == typeof(decimal))
            return "REAL";

        if (type == typeof(byte[]))
            return "BLOB";

        return "TEXT";
    }

    /// <summary>
    /// Builds the SQL column definition statement for a column.
    /// </summary>
    /// <param name="definition">The column definition.</param>
    /// <returns>The SQL column definition statement.</returns>
    protected string BuildColumnDefinition(DbColumnDefinition definition)
    {
        var parts = new List<string>
        {
            NoireDatabase.EscapeColumn(definition.Name),
            definition.Type
        };

        if (definition.IsPrimaryKey)
            parts.Add("PRIMARY KEY");

        if (definition.IsAutoIncrement)
            parts.Add("AUTOINCREMENT");

        if (!definition.IsNullable)
            parts.Add("NOT NULL");

        if (definition.DefaultValue != null)
        {
            var defaultValue = definition.DefaultValue is string
                ? $"'{definition.DefaultValue.ToString()?.Replace("'", "''")}'"
                : definition.DefaultValue.ToString();
            parts.Add("DEFAULT " + defaultValue);
        }

        return string.Join(" ", parts);
    }

    /// <summary>
    /// Gets the cache key used for table column metadata.
    /// </summary>
    /// <returns>The cache key.</returns>
    protected string GetCacheKey() => $"{DatabaseName}:{ResolvedTableName}";

    internal static IReadOnlyCollection<string> GetDatabasesToPreload(Assembly assembly)
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

        var databases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var type in types)
        {
            if (type.IsAbstract || !typeof(NoireDbModelBase).IsAssignableFrom(type))
                continue;

            if (Activator.CreateInstance(type) is not NoireDbModelBase model)
                continue;

            if (!string.IsNullOrWhiteSpace(model.DatabaseDirectoryOverride))
                NoireDatabase.SetDatabaseDirectoryOverride(model.DatabaseName, model.DatabaseDirectoryOverride);

            if (model.LoadDatabaseOnInit)
                databases.Add(model.DatabaseName);
        }

        return databases;
    }

    /// <summary>
    /// Invokes a FindBy method on a related model type.
    /// </summary>
    /// <param name="modelType">The related model type.</param>
    /// <param name="criteria">The filter criteria.</param>
    /// <returns>The related model instance.</returns>
    protected object? InvokeFindBy(Type modelType, IReadOnlyDictionary<string, object?> criteria)
    {
        var method = modelType.GetMethod("FindBy", BindingFlags.Public | BindingFlags.Static);
        return method?.Invoke(null, new object?[] { criteria });
    }

    /// <summary>
    /// Invokes a FindAllBy method on a related model type.
    /// </summary>
    /// <param name="modelType">The related model type.</param>
    /// <param name="criteria">The filter criteria.</param>
    /// <returns>The related model collection.</returns>
    protected object InvokeFindAllBy(Type modelType, IReadOnlyDictionary<string, object?> criteria)
    {
        var method = modelType.GetMethod("FindAllBy", BindingFlags.Public | BindingFlags.Static);
        return method?.Invoke(null, new object?[] { criteria }) ?? Array.Empty<object>();
    }

    /// <summary>
    /// Invokes a Find method on a related model type.
    /// </summary>
    /// <param name="modelType">The related model type.</param>
    /// <param name="id">The primary key value.</param>
    /// <returns>The related model instance.</returns>
    protected object? InvokeFind(Type modelType, object id)
    {
        var method = modelType.GetMethod("Find", BindingFlags.Public | BindingFlags.Static);
        return method?.Invoke(null, new[] { id });
    }

    /// <summary>
    /// Builds the default foreign key name for a model type.
    /// </summary>
    /// <param name="modelType">The model type.</param>
    /// <returns>The foreign key column name.</returns>
    protected static string GetDefaultForeignKeyName(Type modelType)
    {
        var name = modelType.Name;
        if (name.EndsWith("Model", StringComparison.OrdinalIgnoreCase))
            name = name[..^5];

        return ToSnakeCase(name) + "_id";
    }

    /// <summary>
    /// Creates a model instance from a row of data for a specific type.
    /// </summary>
    /// <param name="modelType">The model type.</param>
    /// <param name="data">The row data.</param>
    /// <returns>The model instance.</returns>
    protected static NoireDbModelBase CreateModelFromType(Type modelType, IReadOnlyDictionary<string, object?> data)
    {
        var instance = (NoireDbModelBase)Activator.CreateInstance(modelType)!;
        instance.FillUnsafe(data);
        instance.Exists = true;
        instance.SyncOriginal();
        return instance;
    }

    /// <summary>
    /// Creates a model instance from a row of data.
    /// </summary>
    /// <typeparam name="TModel">The model type.</typeparam>
    /// <param name="data">The row data.</param>
    /// <returns>The model instance.</returns>
    protected static TModel CreateModel<TModel>(IReadOnlyDictionary<string, object?> data) where TModel : NoireDbModelBase, new()
    {
        var instance = new TModel();
        instance.FillUnsafe(data);
        instance.Exists = true;
        instance.SyncOriginal();
        return instance;
    }

    /// <summary>
    /// Finds a single model by the provided criteria.
    /// </summary>
    /// <typeparam name="TModel">The model type.</typeparam>
    /// <param name="criteria">The filter criteria.</param>
    /// <returns>The first model matching the criteria, or null if none found.</returns>
    public static TModel? FindBy<TModel>(IReadOnlyDictionary<string, object?> criteria) where TModel : NoireDbModelBase<TModel>, new()
    {
        var model = new TModel();
        model.EnsureTableCreated();

        if (criteria.Count == 0)
            return null;

        var whereClause = string.Join(" AND ", criteria.Keys.Select((key, index) => $"{NoireDatabase.EscapeColumn(key)} = @p{index}"));
        var sql = $"SELECT * FROM {NoireDatabase.EscapeColumn(model.ResolvedTableName)} WHERE {whereClause}";
        var result = model.GetDb().Fetch(sql, criteria.Values.ToList());

        return result == null ? null : CreateModel<TModel>(result);
    }

    /// <summary>
    /// Finds all models matching the provided criteria.
    /// </summary>
    /// <typeparam name="TModel">The model type.</typeparam>
    /// <param name="criteria">The filter criteria.</param>
    /// <returns>A list of models matching the criteria.</returns>
    public static List<TModel> FindAllBy<TModel>(IReadOnlyDictionary<string, object?> criteria) where TModel : NoireDbModelBase<TModel>, new()
    {
        var model = new TModel();
        model.EnsureTableCreated();

        if (criteria.Count == 0)
            return new List<TModel>();

        var whereClause = string.Join(" AND ", criteria.Keys.Select((key, index) => $"{NoireDatabase.EscapeColumn(key)} = @p{index}"));
        var sql = $"SELECT * FROM {NoireDatabase.EscapeColumn(model.ResolvedTableName)} WHERE {whereClause}";
        var results = model.GetDb().FetchAll(sql, criteria.Values.ToList());

        return results.Select(CreateModel<TModel>).ToList();
    }

    /// <summary>
    /// Finds a model by its primary key.
    /// </summary>
    /// <typeparam name="TModel">The model type.</typeparam>
    /// <param name="id">The primary key value.</param>
    /// <returns>The model with the specified primary key, or null if not found.</returns>
    public static TModel? Find<TModel>(object id) where TModel : NoireDbModelBase<TModel>, new()
    {
        var model = new TModel();
        model.EnsureTableCreated();

        var sql = $"SELECT * FROM {NoireDatabase.EscapeColumn(model.ResolvedTableName)} WHERE {NoireDatabase.EscapeColumn(model.PrimaryKey)} = @p0";
        var result = model.GetDb().Fetch(sql, new[] { id });

        return result == null ? null : CreateModel<TModel>(result);
    }
}
