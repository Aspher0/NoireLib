
# NoireLib Documentation - NoireDatabase

You are reading the documentation for the `NoireDatabase` system.

## Table of Contents
- [Overview](#overview)
- [Getting Started](#getting-started)
- [Defining Database Models](#defining-database-models)
  - [1. Model Basics](#model-basics)
  - [2. Column Definitions](#column-definitions)
  - [3. Custom Schema Definitions](#custom-schema-definitions)
- [Querying Data](#querying-data)
  - [1. Query Builder Basics](#query-builder-basics)
  - [2. Filtering, Joins, and Aggregates](#filtering-joins-and-aggregates)
  - [3. Pagination and Chunking](#pagination-and-chunking)
- [Relationships](#relationships)
  - [1. HasOne / HasMany](#hasone--hasmany)
  - [2. BelongsTo](#belongsto)
  - [3. BelongsToMany](#belongstomany)
- [Validation Rules](#validation-rules)
- [Casting](#casting)
- [Using the Database Core](#using-the-database-core)
- [Advanced Features](#advanced-features)
- [Troubleshooting](#troubleshooting)
- [See Also](#see-also)

---

## Overview

The `NoireDatabase` system provides a lightweight SQLite-backed ORM-style layer with:
- **Model-based CRUD** for rows and tables
- **Automatic schema creation** from models and attributes
- **Fluent query builder** for complex SQL queries
- **Relationships and validation** via model metadata
- **Typed casting** for columns
- **Transactions, caching, and query logging**

---

## Getting Started

***❗ We will assume you have already initialized NoireLib in your plugin.
If not, please refer to the [NoireLib documentation](https://github.com/Aspher0/NoireLib/blob/main/NoireLib/README.md).***

### Things to know and quick example

All database models must inherit from `NoireDbModelBase<T>` and implement `DatabaseName`, `PrimaryKey` and `TableName`.
Tables are created on demand when you query or save models.

```csharp
using NoireLib.Database;
using System;

namespace MyPlugin.Database;

public sealed class ProfileModel : NoireDbModelBase<ProfileModel>
{
    protected override string DatabaseName => "MyPluginDatabase";
    protected override string? TableName => "user_profiles"; // set to null for default
    protected override string PrimaryKey => "user_id";

    [NoireDbColumn("user_id", IsPrimaryKey = true, IsAutoIncrement = true)]
    public long UserId
    {
        get => GetColumn<long>("user_id");
        set => SetColumn("user_id", value);
    }

    [NoireDbColumn("name", Type = "TEXT", IsNullable = false)]
    public string Name
    {
        get => GetColumn<string>("name") ?? string.Empty;
        set => SetColumn("name", value);
    }

    [NoireDbColumn("created_at", Type = "TEXT")]
    public DateTime CreatedAt
    {
        get => GetColumn<DateTime>("created_at");
        set => SetColumn("created_at", value);
    }

    protected override IReadOnlyDictionary<string, DbColumnCast> Casts =>
        new Dictionary<string, DbColumnCast>
        {
            ["created_at"] = DbColumnCast.DateTime
        };
}

// Usage
var profile = new ProfileModel { Name = "Noire", CreatedAt = DateTime.UtcNow };
var saved = profile.Save();

var profileModel = ProfileModel.Find(profile.UserId);
```

---

## Defining Database Models

NoireDatabase offers **three primary ways** to define schema and behavior.

### 1. Model Basics

At minimum you provide:
- `DatabaseName` (SQLite file name)
- `TableName` (set to `null` for the default)
- `PrimaryKey` column

```csharp
public sealed class ItemModel : NoireDbModelBase<ItemModel>
{
    protected override string DatabaseName => "my_plugin";
    protected override string? TableName => null;
    protected override string PrimaryKey => "id";

    [NoireDbColumn("id", IsPrimaryKey = true, IsAutoIncrement = true)]
    public long Id { get => GetColumn<long>("id"); set => SetColumn("id", value); }
}
```

### 2. Column Definitions

Use `[NoireDbColumn]` on properties to customize column metadata.

```csharp
[NoireDbColumn("name", Type = "TEXT", IsNullable = false)]
public string Name
{
    get => GetColumn<string>("name") ?? string.Empty;
    set => SetColumn("name", value);
}

[NoireDbColumn("is_active", Type = "INTEGER", DefaultValue = 1)]
public bool IsActive
{
    get => GetColumn<bool>("is_active");
    set => SetColumn("is_active", value);
}
```

### 3. Custom Schema Definitions

For full control, override `Columns` to define schema programmatically.

```csharp
protected override IReadOnlyDictionary<string, DbColumnDefinition> Columns =>
    new Dictionary<string, DbColumnDefinition>
    {
        ["id"] = new DbColumnDefinition("id", "INTEGER")
        {
            IsPrimaryKey = true,
            IsAutoIncrement = true,
            IsNullable = false
        },
        ["name"] = new DbColumnDefinition("name", "TEXT")
        {
            IsNullable = false
        }
    };
```

---

## Querying Data

### 1. Query Builder Basics

```csharp
var activeProfiles = ProfileModel.Query()
    .Where("is_active", true)
    .OrderByDesc("created_at")
    .Get();

var firstProfile = ProfileModel.Query()
    .Where("name", "Noire")
    .First();
```

### 2. Filtering, Joins, and Aggregates

```csharp
var count = ProfileModel.Query().Count();

var recent = ProfileModel.Query()
    .WhereBetween("created_at", new object?[] { DateTime.UtcNow.AddDays(-7), DateTime.UtcNow })
    .OrderByDesc("created_at")
    .Get();

var joined = ProfileModel.Query()
    .Join("profiles_notes", "profiles.id", "=", "profiles_notes.profile_id")
    .Select("profiles.*", "profiles_notes.note")
    .Get();
```

### 3. Pagination and Chunking

```csharp
var page = ProfileModel.Query().Paginate(20, 1);
var items = page.Items;
var meta = page.Pagination;

ProfileModel.Query().Chunk(50, (chunk, pageNumber) =>
{
    // Process chunk
    return true;
});
```

---

## Relationships

Relationships are defined via `Relations` and consumed via `GetRelation`.

### 1. HasOne / HasMany

```csharp
protected override IReadOnlyDictionary<string, DbRelationDefinition> Relations =>
    new Dictionary<string, DbRelationDefinition>
    {
        ["notes"] = new DbRelationDefinition(DbRelationType.HasMany, typeof(ProfileNoteModel))
        {
            ForeignKey = "profile_id",
            LocalKey = "id"
        }
    };

public IReadOnlyList<ProfileNoteModel> Notes =>
    (IReadOnlyList<ProfileNoteModel>)GetRelation("notes")!;
```

### 2. BelongsTo

```csharp
protected override IReadOnlyDictionary<string, DbRelationDefinition> Relations =>
    new Dictionary<string, DbRelationDefinition>
    {
        ["profile"] = new DbRelationDefinition(DbRelationType.BelongsTo, typeof(ProfileModel))
        {
            OwnerKey = "profile_id"
        }
    };

public ProfileModel? Profile => (ProfileModel?)GetRelation("profile");
```

### 3. BelongsToMany

```csharp
protected override IReadOnlyDictionary<string, DbRelationDefinition> Relations =>
    new Dictionary<string, DbRelationDefinition>
    {
        ["tags"] = new DbRelationDefinition(DbRelationType.BelongsToMany, typeof(TagModel))
        {
            PivotTable = "note_tags",
            ForeignPivotKey = "note_id",
            RelatedPivotKey = "tag_id",
            ParentKey = "id"
        }
    };

public IReadOnlyList<TagModel> Tags =>
    (IReadOnlyList<TagModel>)GetRelation("tags")!;
```

---

## Validation Rules

Use `ValidationRules` to validate data before saving.

```csharp
protected override IReadOnlyDictionary<string, IReadOnlyList<DbValidationRuleDefinition>> ValidationRules =>
    new Dictionary<string, IReadOnlyList<DbValidationRuleDefinition>>
    {
        ["name"] = new List<DbValidationRuleDefinition>
        {
            new(DbValidationRule.Required),
            new(DbValidationRule.Max, new[] { "32" })
        },
        ["level"] = new List<DbValidationRuleDefinition>
        {
            new(DbValidationRule.Integer),
            new(DbValidationRule.Min, new[] { "1" })
        }
    };

var model = new ProfileModel();
if (!model.Save())
{
    var errors = model.GetErrors();
}
```

---

## Casting

Use `Casts` to cast columns automatically on read/write.

```csharp
protected override IReadOnlyDictionary<string, DbColumnCast> Casts =>
    new Dictionary<string, DbColumnCast>
    {
        ["created_at"] = DbColumnCast.DateTime,
        ["is_active"] = DbColumnCast.Boolean
    };
```

Supported casts:
- `Integer`, `Float`, `String`, `Boolean`
- `Array`, `Json`
- `DateTime`, `Timestamp`

---

## Using the Database Core

If you need raw SQL or low-level control, use `NoireDatabase` directly.

```csharp
var db = NoireDatabase.GetInstance("my_plugin");

db.Execute("CREATE TABLE IF NOT EXISTS notes (id INTEGER PRIMARY KEY AUTOINCREMENT, body TEXT)");

var id = db.Insert("notes", new Dictionary<string, object?>
{
    ["body"] = "Hello"
});

var row = db.Fetch("SELECT * FROM notes WHERE id = @p0", new object?[] { id });
var all = db.FetchAll("SELECT * FROM notes");

db.BeginTransaction();
// ... do work
db.Commit();
```

---

## Advanced Features

### Preloading Databases at Initialization

By default, databases are preloaded when the plugin loads. This is the recommended setting to avoid loading SQLite files during gameplay.
Set `LoadDatabaseOnInit` to `false` to prevent that if needed.

```csharp
public sealed class ProfileModel : NoireDbModelBase<ProfileModel>
{
    protected override string DatabaseName => "my_plugin";
    protected override string? TableName => null;
    protected override string PrimaryKey => "id";
    protected override bool LoadDatabaseOnInit => false;
}
```

### Caching

```csharp
var cached = db.Cache("profiles.count", db => db.Count("profiles"), TimeSpan.FromMinutes(5));
db.ClearCache();
```

### Query Logging

```csharp
db.SetLogQueries(true);
var results = db.FetchAll("SELECT * FROM profiles");
var queries = db.GetQueries();
```

### Database Path Overrides

```csharp
NoireDatabase.SetDatabaseFilePathOverride("my_plugin", "C:\\path\\to\\database.db");
NoireDatabase.RemoveDatabaseFilePathOverride("my_plugin");
NoireDatabase.ClearDatabaseFilePathOverrides();
```

### Concurrency Settings

```csharp
NoireDatabase.BusyTimeout = TimeSpan.FromSeconds(10);
NoireDatabase.UseWriteAheadLogging = true;
```

---

## Troubleshooting

### Table not created
- Ensure you are calling `Save()`, `Query()`, or `All()` at least once
- Confirm `DatabaseName` and `PrimaryKey` are implemented

### Save returns false
- Check validation errors via `GetErrors()`
- Ensure the primary key exists for updates

### Relationships return empty
- Verify foreign key columns exist
- Ensure the related table has data
- For belongs-to-many, ensure the pivot table exists

### Casting issues
- Ensure the column is included in `Casts`
- Check that stored values match the expected type

---

## See Also

- [NoireLib Documentation](https://github.com/Aspher0/NoireLib/blob/main/NoireLib/README.md)
