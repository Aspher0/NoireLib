using System;
using System.Collections.Generic;
using System.Linq;

namespace NoireLib.Database;

/// <summary>
/// Generic base class for database models, providing common functionality for querying and manipulating data.
/// </summary>
public abstract class NoireDbModelBase<TModel> : NoireDbModelBase where TModel : NoireDbModelBase<TModel>, new()
{
    /// <summary>
    /// Gets the database instance associated with the model.
    /// </summary>
    /// <returns>The <see cref="NoireDatabase"/> instance.</returns>
    public static NoireDatabase GetDatabase() => new TModel().GetDb();

    /// <summary>
    /// Creates a query builder for the model.
    /// </summary>
    /// <returns>A new instance of <see cref="QueryBuilder{TModel}"/> for the model.</returns>
    public static QueryBuilder<TModel> Query()
    {
        var model = new TModel();
        model.EnsureTableCreated();
        return new QueryBuilder<TModel>(model.ResolvedTableName, model.GetDb());
    }

    /// <summary>
    /// Creates a new model instance with optional column values.
    /// </summary>
    /// <param name="columns">The column values to set. Represented as a dictionary of column names to values.</param>
    /// <returns>A new instance of <typeparamref name="TModel"/> with the specified columns.</returns>
    public static TModel Make(IReadOnlyDictionary<string, object?>? columns = null)
    {
        var model = new TModel();
        if (columns != null)
            model.Fill(columns);

        return model;
    }

    /// <summary>
    /// Finds a row by its primary key.
    /// </summary>
    /// <param name="id">The primary key value.</param>
    /// <returns>The row casted as <typeparamref name="TModel"/> if found, otherwise null.</returns>
    public static TModel? Find(object id) => Find<TModel>(id);

    /// <summary>
    /// Finds a row by its primary key or throws if none is found.
    /// </summary>
    /// <param name="id">The primary key value.</param>
    /// <returns>The row casted as <typeparamref name="TModel"/> if found.</returns>
    /// <exception cref="InvalidOperationException">Thrown if no row is found with the given identifier.</exception>"
    public static TModel FindOrFail(object id)
    {
        var model = Find<TModel>(id);
        if (model == null)
            throw new InvalidOperationException($"Model with identifier {id} was not found.");

        return model;
    }

    /// <summary>
    /// Finds a row matching the provided criteria.
    /// </summary>
    /// <param name="criteria">The filter criteria. Represented as a dictionary of column names to values.</param>
    /// <returns>The row casted as <typeparamref name="TModel"/> if found, otherwise null.</returns>
    public static TModel? FindBy(IReadOnlyDictionary<string, object?> criteria) => FindBy<TModel>(criteria);

    /// <summary>
    /// Finds all rows matching the provided criteria.
    /// </summary>
    /// <param name="criteria">The filter criteria. Represented as a dictionary of column names to values.</param>
    /// <returns>A list of all rows casted as <typeparamref name="TModel"/>.</returns>
    public static List<TModel> FindAllBy(IReadOnlyDictionary<string, object?> criteria) => FindAllBy<TModel>(criteria);

    /// <summary>
    /// Retrieves all rows from the table.
    /// </summary>
    /// <returns>A list of all rows casted as <typeparamref name="TModel"/>.</returns>
    public static List<TModel> All()
    {
        var model = new TModel();
        model.EnsureTableCreated();
        var results = model.GetDb().FetchAll($"SELECT * FROM {NoireDatabase.EscapeColumn(model.ResolvedTableName)}");
        return results.Select(CreateModel<TModel>).ToList();
    }

    /// <summary>
    /// Deletes rows matching the provided criteria.
    /// </summary>
    /// <param name="criteria">The filter criteria. Represented as a dictionary of column names to values.</param>
    /// <returns>The number of rows deleted.</returns>
    public static int DeleteWhere(IReadOnlyDictionary<string, object?> criteria)
    {
        var model = new TModel();
        model.EnsureTableCreated();

        if (criteria.Count == 0)
            return 0;

        return model.GetDb().Delete(model.ResolvedTableName, criteria);
    }

    internal static void EnsureTable()
    {
        var model = new TModel();
        model.EnsureTableCreated();
    }

    internal static TModel FromDatabaseRow(IReadOnlyDictionary<string, object?> row) => CreateModel<TModel>(row);
}
