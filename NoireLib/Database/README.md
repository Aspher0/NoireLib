
# NoireLib Documentation - NoireDatabase

You are reading the documentation for the `NoireDatabase` system.

## Table of Contents
- [Overview](#overview)
- [Getting Started](#getting-started)
- [Defining Database Models](#defining-database-models)
  - [1. Model Basics](#model-basics)
  - [2. Column Definitions](#column-definitions)
  - [3. Manual Accessors](#manual-accessors)
  - [4. Custom Schema Definitions](#custom-schema-definitions)
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
- [Database Migrations](#database-migrations)
  - [1. Create migrations](#create-migrations)
  - [2. Use common operations](#use-common-operations)
  - [3. Check current schema version](#check-current-schema-version)
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

To avoid boilerplate `GetColumn`/`SetColumn` accessors, mark your model as `partial` and use `partial` auto-properties with `[NoireDbColumn]`.
The source generator will emit the accessors in a generated `*.g.cs` file while keeping the same class type.

All database models must inherit from `NoireDbModelBase<T>` and implement `DatabaseName`, `PrimaryKey` and `TableName`.
Tables are created on demand when you query or save models.

```csharp
using NoireLib.Database;
using System;

namespace MyPlugin.Database;

public sealed partial class ProfileModel : NoireDbModelBase<ProfileModel>
{
    protected override string DatabaseName => "MyPluginDatabase";
    protected override string? TableName => "user_profiles"; // set to null for default
    protected override string PrimaryKey => "user_id";

    [NoireDbColumn("user_id", IsPrimaryKey = true, IsAutoIncrement = true)]
    public partial long UserId { get; set; }

    [NoireDbColumn("name", Type = "TEXT", IsNullable = false)]
    public partial string Name { get; set; }

    [NoireDbColumn("created_at", Type = "TEXT")]
    public partial DateTime CreatedAt { get; set; }

    protected override IReadOnlyDictionary<string, DbColumnCast> Casts =>
        new Dictionary<string, DbColumnCast>
        {
            ["created_at"] = DbColumnCast.DateTime
        };
}

// Usage
var profile = new ProfileModel { Name = "Noire", CreatedAt = DateTime.UtcNow };
profile.Name = "New Name";
var saved = profile.Save();

// Retrieving later
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

If you use `[NoireDbColumn]`, declare the class and the properties as `partial` to let the source generator produce the accessors.

```csharp
public sealed partial class ItemModel : NoireDbModelBase<ItemModel>
{
    protected override string DatabaseName => "MyPluginDatabase";
    protected override  string? TableName => "items";  // set to null for default
    protected override string PrimaryKey => "user_id";

    [NoireDbColumn("item_id", IsPrimaryKey = true, IsAutoIncrement = true)]
    public partial long ItemId { get; set; }
}
```

### 2. Column Definitions

Use `[NoireDbColumn]` on properties to customize column metadata.

```csharp
[NoireDbColumn("name", Type = "TEXT", IsNullable = false)]
public partial string Name { get; set; }

[NoireDbColumn("is_active", Type = "INTEGER", DefaultValue = 1)]
public partial bool IsActive { get; set; }
```

### 3. Manual Accessors

If you prefer not to use the source generator, keep the class non-`partial` and set the accessors manually with `GetColumn` and `SetColumn`.

```csharp
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
}
```

### 4. Custom Schema Definitions

Optionally, for whatever reason, you can override `Columns` to define schema programmatically instead.

```csharp
protected override IReadOnlyDictionary<string, DbColumnDefinition> Columns =>
    new Dictionary<string, DbColumnDefinition>
    {
        ["user_id"] = new DbColumnDefinition("user_id", "INTEGER")
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
    .WhereBetween("created_at", [DateTime.UtcNow.AddDays(-7), DateTime.UtcNow])
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
            LocalKey = "note_id"
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
var db = NoireDatabase.GetInstance("MyPluginDatabase");

db.Execute("CREATE TABLE IF NOT EXISTS notes (id INTEGER PRIMARY KEY AUTOINCREMENT, body TEXT)");

var id = db.Insert("notes", new Dictionary<string, object?>
{
    { "body", "Hello" },
});

var row = db.Fetch("SELECT * FROM notes WHERE id = @p0", [ id ]);
var all = db.FetchAll("SELECT * FROM notes");

db.BeginTransaction();
// ... do work
db.Commit();
```

---

## Database Migrations

Database migrations let you evolve schemas safely using the SQLite `PRAGMA user_version` value.
Migrations run automatically the first time a database is opened and whenever the schema version is behind.

#### 1. Create migrations

Create a class per migration step and annotate it with `[DatabaseMigration("DatabaseName")]`.
Implement `FromVersion`, `ToVersion`, and `Migrate`.

```csharp
using NoireLib.Database;
using NoireLib.Database.Migrations;
using System.Collections.Generic;

namespace MyPlugin.Database.Migrations;

[DatabaseMigration("MyPluginDatabase")]
public sealed class ProfileMigrationV0ToV1 : DatabaseMigrationBase
{
    public override int FromVersion => 0;
    public override int ToVersion => 1;

    public override void Migrate(NoireDatabase database)
    {
        var columns = new List<DbColumnDefinition>
        {
            new("user_id", "INTEGER") { IsPrimaryKey = true, IsAutoIncrement = true, IsNullable = false },
            new("name", "TEXT") { IsNullable = false },
            new("created_at", "TEXT") { IsNullable = false }
        };

        DatabaseMigrationBuilder.Create(database)
            .CreateTable("user_profiles", columns)
            .Apply();
    }
}

[DatabaseMigration("MyPluginDatabase")]
public sealed class ProfileMigrationV1ToV2 : DatabaseMigrationBase
{
    public override int FromVersion => 1;
    public override int ToVersion => 2;

    public override void Migrate(NoireDatabase database)
    {
        DatabaseMigrationBuilder.Create(database)
            .AddColumn("user_profiles", "last_login_at", "TEXT")
            .Apply();
    }
}
```

#### 2. Use common operations

`DatabaseMigrationBuilder` supports:
- `CreateTable`, `DropTable`, `RenameTable`
- `AddColumn`, `RenameColumn`, `DropColumn`
- `ExecuteRaw` for custom SQL

```csharp
DatabaseMigrationBuilder.Create(database)
    .RenameTable("profiles", "profiles_archive")
    .CreateTable("profiles", columns)
    .ExecuteRaw("CREATE INDEX IF NOT EXISTS idx_profiles_name ON profiles(name)")
    .Apply();
```

#### 3. Check current schema version

```csharp
var db = NoireDatabase.GetInstance("MyPluginDatabase");
var version = db.GetSchemaVersion();
```

You can also check externally with a SQLite tool by running:

```
PRAGMA user_version;
```

---

## Advanced Features

### Preventing Preloading Databases at Initialization

By default, databases are preloaded when the plugin loads. This is the recommended setting to avoid loading SQLite files during gameplay.
Set `LoadDatabaseOnInit` to `false` to prevent that if needed.

```csharp
public sealed class ProfileModel : NoireDbModelBase<ProfileModel>
{
    protected override string DatabaseName => "MyPluginDatabase";
    protected override string? TableName => null;
    protected override string PrimaryKey => "user_id";
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
public sealed class ProfileModel : NoireDbModelBase<ProfileModel>
{
    protected override string DatabaseName => "MyPluginDatabase";
    protected override string? TableName => null;
    protected override string PrimaryKey => "user_id";

    protected override string? DatabaseDirectoryOverride => FileHelper.GetPluginConfigDirectory() is { } directory
        ? Path.Combine(directory, "DatabasesDev")
        : null;
}

NoireDatabase.RemoveDatabaseDirectoryOverride("MyPluginDatabase");
NoireDatabase.ClearDatabaseDirectoryOverrides();
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
