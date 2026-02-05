using System;
using System.Collections.Generic;
using System.Linq;

namespace NoireLib.Database;

/// <summary>
/// Builds and executes SQL queries for a model type.
/// </summary>
public sealed class QueryBuilder<TModel> where TModel : NoireDbModelBase<TModel>, new()
{
    #region Private Fields/Properties and Constructor

    private readonly NoireDatabase _db;
    private readonly string _table;
    private string? _tableAlias;
    private readonly List<string> _columns = new() { "*" };
    private readonly List<WhereClause> _wheres = new();
    private readonly List<JoinClause> _joins = new();
    private readonly List<OrderClause> _orders = new();
    private readonly List<string> _groups = new();
    private readonly List<HavingClause> _havings = new();
    private int? _limit;
    private int? _offset;
    private readonly List<object?> _whereBindings = new();
    private readonly List<object?> _joinBindings = new();
    private readonly List<object?> _havingBindings = new();

    internal QueryBuilder(string table, NoireDatabase db)
    {
        _table = table;
        _db = db;
    }

    #endregion

    #region Public Methods

    /// <summary>
    /// Sets the selected columns for the query.
    /// </summary>
    /// <param name="columns">The columns to select.</param>
    /// <returns>The query builder instance for chaining.</returns>
    public QueryBuilder<TModel> Select(params string[] columns)
    {
        _columns.Clear();
        _columns.AddRange(columns.Length == 0 ? new[] { "*" } : columns);
        return this;
    }

    /// <summary>
    /// Adds a basic where clause with an equals operator.
    /// </summary>
    /// <param name="column">The column name.</param>
    /// <param name="value">The comparison value.</param>
    /// <returns>The query builder instance for chaining.</returns>
    public QueryBuilder<TModel> Where(string column, object? value)
    {
        return Where(column, "=", value, "AND");
    }

    /// <summary>
    /// Adds a basic where clause.
    /// </summary>
    /// <param name="column">The column name.</param>
    /// <param name="operatorValue">The comparison operator.</param>
    /// <param name="value">The comparison value.</param>
    /// <param name="boolean">The boolean operator.</param>
    /// <returns>The query builder instance for chaining.</returns>
    public QueryBuilder<TModel> Where(string column, string operatorValue, object? value, string boolean = "AND")
    {
        _wheres.Add(new WhereClause(WhereClauseType.Basic, boolean, column, operatorValue, value));
        _whereBindings.Add(value);
        return this;
    }

    /// <summary>
    /// Adds where clauses for the provided criteria.
    /// </summary>
    /// <param name="criteria">The filter criteria. Represented as a dictionary of column names to values.</param>
    /// <returns>The query builder instance for chaining.</returns>
    public QueryBuilder<TModel> Where(IReadOnlyDictionary<string, object?> criteria)
    {
        foreach (var (key, value) in criteria)
            Where(key, "=", value, "AND");

        return this;
    }

    /// <summary>
    /// Adds a basic OR where clause with an equals operator.
    /// </summary>
    /// <param name="column">The column name.</param>
    /// <param name="value">The comparison value.</param>
    /// <returns>The query builder instance for chaining.</returns>
    public QueryBuilder<TModel> OrWhere(string column, object? value)
    {
        return Where(column, "=", value, "OR");
    }

    /// <summary>
    /// Adds a basic OR where clause.
    /// </summary>
    /// <param name="column">The column name.</param>
    /// <param name="operatorValue">The comparison operator.</param>
    /// <param name="value">The comparison value.</param>
    /// <returns>The query builder instance for chaining.</returns>
    public QueryBuilder<TModel> OrWhere(string column, string operatorValue, object? value)
    {
        return Where(column, operatorValue, value, "OR");
    }

    /// <summary>
    /// Adds a raw where clause.
    /// </summary>
    /// <param name="sql">The raw SQL clause.</param>
    /// <param name="bindings">The binding values.</param>
    /// <param name="boolean">The boolean operator.</param>
    /// <returns>The query builder instance for chaining.</returns>
    public QueryBuilder<TModel> WhereRaw(string sql, IReadOnlyList<object?> bindings, string boolean = "AND")
    {
        _wheres.Add(new WhereClause(WhereClauseType.Raw, boolean, RawSql: sql));
        _whereBindings.AddRange(bindings);
        return this;
    }

    /// <summary>
    /// Adds a raw OR where clause.
    /// </summary>
    /// <param name="sql">The raw SQL clause.</param>
    /// <param name="bindings">The binding values.</param>
    /// <returns>The query builder instance for chaining.</returns>
    public QueryBuilder<TModel> OrWhereRaw(string sql, IReadOnlyList<object?> bindings)
    {
        return WhereRaw(sql, bindings, "OR");
    }

    /// <summary>
    /// Adds a where-in clause.
    /// </summary>
    /// <param name="column">The column name.</param>
    /// <param name="values">The comparison values.</param>
    /// <param name="boolean">The boolean operator.</param>
    /// <param name="not">Whether to negate the clause.</param>
    /// <returns>The query builder instance for chaining.</returns>
    public QueryBuilder<TModel> WhereIn(string column, IReadOnlyList<object?> values, string boolean = "AND", bool not = false)
    {
        _wheres.Add(new WhereClause(not ? WhereClauseType.NotIn : WhereClauseType.In, boolean, column, Values: values));
        _whereBindings.AddRange(values);
        return this;
    }

    /// <summary>
    /// Adds a where-not-in clause.
    /// </summary>
    /// <param name="column">The column name.</param>
    /// <param name="values">The comparison values.</param>
    /// <param name="boolean">The boolean operator.</param>
    /// <returns>The query builder instance for chaining.</returns>
    public QueryBuilder<TModel> WhereNotIn(string column, IReadOnlyList<object?> values, string boolean = "AND")
    {
        return WhereIn(column, values, boolean, true);
    }

    /// <summary>
    /// Adds a null check clause.
    /// </summary>
    /// <param name="column">The column name.</param>
    /// <param name="boolean">The boolean operator.</param>
    /// <param name="not">Whether to negate the clause.</param>
    /// <returns>The query builder instance for chaining.</returns>
    public QueryBuilder<TModel> WhereNull(string column, string boolean = "AND", bool not = false)
    {
        _wheres.Add(new WhereClause(not ? WhereClauseType.NotNull : WhereClauseType.Null, boolean, column));
        return this;
    }

    /// <summary>
    /// Adds a not-null check clause.
    /// </summary>
    /// <param name="column">The column name.</param>
    /// <param name="boolean">The boolean operator.</param>
    /// <returns>The query builder instance for chaining.</returns>
    public QueryBuilder<TModel> WhereNotNull(string column, string boolean = "AND")
    {
        return WhereNull(column, boolean, true);
    }

    /// <summary>
    /// Adds a between clause.
    /// </summary>
    /// <param name="column">The column name.</param>
    /// <param name="values">The range values.</param>
    /// <param name="boolean">The boolean operator.</param>
    /// <param name="not">Whether to negate the clause.</param>
    /// <returns>The query builder instance for chaining.</returns>
    public QueryBuilder<TModel> WhereBetween(string column, IReadOnlyList<object?> values, string boolean = "AND", bool not = false)
    {
        _wheres.Add(new WhereClause(not ? WhereClauseType.NotBetween : WhereClauseType.Between, boolean, column, Values: values));
        _whereBindings.AddRange(values);
        return this;
    }

    /// <summary>
    /// Adds a not-between clause.
    /// </summary>
    /// <param name="column">The column name.</param>
    /// <param name="values">The range values.</param>
    /// <param name="boolean">The boolean operator.</param>
    /// <returns>The query builder instance for chaining.</returns>
    public QueryBuilder<TModel> WhereNotBetween(string column, IReadOnlyList<object?> values, string boolean = "AND")
    {
        return WhereBetween(column, values, boolean, true);
    }

    /// <summary>
    /// Adds a like clause.
    /// </summary>
    /// <param name="column">The column name.</param>
    /// <param name="value">The pattern value.</param>
    /// <param name="boolean">The boolean operator.</param>
    /// <returns>The query builder instance for chaining.</returns>
    public QueryBuilder<TModel> WhereLike(string column, string value, string boolean = "AND")
    {
        return Where(column, "LIKE", value, boolean);
    }

    /// <summary>
    /// Adds an OR like clause.
    /// </summary>
    /// <param name="column">The column name.</param>
    /// <param name="value">The pattern value.</param>
    /// <returns>The query builder instance for chaining.</returns>
    public QueryBuilder<TModel> OrWhereLike(string column, string value)
    {
        return WhereLike(column, value, "OR");
    }

    /// <summary>
    /// Adds a nested where clause built by the provided callback.
    /// </summary>
    /// <param name="callback">The callback that configures the nested query.</param>
    /// <param name="boolean">The boolean operator.</param>
    /// <returns>The query builder instance for chaining.</returns>
    public QueryBuilder<TModel> WhereNested(Action<QueryBuilder<TModel>> callback, string boolean = "AND")
    {
        var nested = new QueryBuilder<TModel>(_table, _db);
        callback(nested);

        if (nested._wheres.Count == 0)
            return this;

        _wheres.Add(new WhereClause(WhereClauseType.Nested, boolean, NestedQuery: nested));
        _whereBindings.AddRange(nested._whereBindings);
        return this;
    }

    /// <summary>
    /// Adds a nested OR where clause built by the provided callback.
    /// </summary>
    /// <param name="callback">The callback that configures the nested query.</param>
    /// <returns>The query builder instance for chaining.</returns>
    public QueryBuilder<TModel> OrWhereNested(Action<QueryBuilder<TModel>> callback)
    {
        return WhereNested(callback, "OR");
    }

    /// <summary>
    /// Adds a join clause.
    /// </summary>
    /// <param name="table">The table to join.</param>
    /// <param name="first">The left column.</param>
    /// <param name="operatorValue">The join operator.</param>
    /// <param name="second">The right column.</param>
    /// <param name="type">The join type.</param>
    /// <returns>The query builder instance for chaining.</returns>
    public QueryBuilder<TModel> Join(string table, string first, string operatorValue, string second, string type = "INNER")
    {
        _joins.Add(new JoinClause(type, table, First: first, Operator: operatorValue, Second: second));
        return this;
    }

    /// <summary>
    /// Adds a left join clause.
    /// </summary>
    /// <param name="table">The table to join.</param>
    /// <param name="first">The left column.</param>
    /// <param name="operatorValue">The join operator.</param>
    /// <param name="second">The right column.</param>
    /// <returns>The query builder instance for chaining.</returns>
    public QueryBuilder<TModel> LeftJoin(string table, string first, string operatorValue, string second)
    {
        return Join(table, first, operatorValue, second, "LEFT");
    }

    /// <summary>
    /// Adds a right join clause.
    /// </summary>
    /// <param name="table">The table to join.</param>
    /// <param name="first">The left column.</param>
    /// <param name="operatorValue">The join operator.</param>
    /// <param name="second">The right column.</param>
    /// <returns>The query builder instance for chaining.</returns>
    public QueryBuilder<TModel> RightJoin(string table, string first, string operatorValue, string second)
    {
        return Join(table, first, operatorValue, second, "RIGHT");
    }

    /// <summary>
    /// Adds an order by clause.
    /// </summary>
    /// <param name="column">The column name.</param>
    /// <param name="direction">The sort direction.</param>
    /// <returns>The query builder instance for chaining.</returns>
    public QueryBuilder<TModel> OrderBy(string column, string direction = "ASC")
    {
        var normalized = direction.ToUpperInvariant();
        if (normalized != "ASC" && normalized != "DESC")
            normalized = "ASC";

        _orders.Add(new OrderClause(column, normalized));
        return this;
    }

    /// <summary>
    /// Adds a descending order by clause.
    /// </summary>
    /// <param name="column">The column name.</param>
    /// <returns>The query builder instance for chaining.</returns>
    public QueryBuilder<TModel> OrderByDesc(string column)
    {
        return OrderBy(column, "DESC");
    }

    /// <summary>
    /// Adds an ascending order by clause.
    /// </summary>
    /// <param name="column">The column name.</param>
    /// <returns>The query builder instance for chaining.</returns>
    public QueryBuilder<TModel> OrderByAsc(string column)
    {
        return OrderBy(column, "ASC");
    }

    /// <summary>
    /// Adds a group by clause.
    /// </summary>
    /// <param name="columns">The columns to group by.</param>
    /// <returns>The query builder instance for chaining.</returns>
    public QueryBuilder<TModel> GroupBy(params string[] columns)
    {
        _groups.AddRange(columns);
        return this;
    }

    /// <summary>
    /// Adds a having clause.
    /// </summary>
    /// <param name="column">The column name.</param>
    /// <param name="operatorValue">The comparison operator.</param>
    /// <param name="value">The comparison value.</param>
    /// <param name="boolean">The boolean operator.</param>
    /// <returns>The query builder instance for chaining.</returns>
    public QueryBuilder<TModel> Having(string column, string operatorValue, object? value, string boolean = "AND")
    {
        _havings.Add(new HavingClause(column, operatorValue, value, boolean));
        _havingBindings.Add(value);
        return this;
    }

    /// <summary>
    /// Adds an OR having clause.
    /// </summary>
    /// <param name="column">The column name.</param>
    /// <param name="operatorValue">The comparison operator.</param>
    /// <param name="value">The comparison value.</param>
    /// <returns>The query builder instance for chaining.</returns>
    public QueryBuilder<TModel> OrHaving(string column, string operatorValue, object? value)
    {
        return Having(column, operatorValue, value, "OR");
    }

    /// <summary>
    /// Sets the query limit.
    /// </summary>
    /// <param name="value">The maximum row count.</param>
    /// <returns>The query builder instance for chaining.</returns>
    public QueryBuilder<TModel> Limit(int value)
    {
        _limit = Math.Max(0, value);
        return this;
    }

    /// <summary>
    /// Sets the query offset.
    /// </summary>
    /// <param name="value">The offset value.</param>
    /// <returns>The query builder instance for chaining.</returns>
    public QueryBuilder<TModel> Offset(int value)
    {
        _offset = Math.Max(0, value);
        return this;
    }

    /// <summary>
    /// Sets the table alias.
    /// </summary>
    /// <param name="alias">The alias name.</param>
    /// <returns>The query builder instance for chaining.</returns>
    public QueryBuilder<TModel> As(string alias)
    {
        _tableAlias = alias;
        return this;
    }

    /// <summary>
    /// Builds the SQL query and parameters.
    /// </summary>
    /// <returns>A tuple containing the SQL string and parameter list.</returns>
    public (string Sql, IReadOnlyList<object?> Parameters) ToSql()
    {
        var sql = $"SELECT {CompileColumns()} FROM {CompileTable()}";

        if (_joins.Count > 0)
            sql += " " + CompileJoins();

        if (_wheres.Count > 0)
            sql += " " + CompileWheres();

        if (_groups.Count > 0)
            sql += " GROUP BY " + string.Join(", ", _groups);

        if (_havings.Count > 0)
            sql += " " + CompileHavings();

        if (_orders.Count > 0)
            sql += " " + CompileOrders();

        if (_limit.HasValue)
        {
            sql += " LIMIT " + _limit.Value;
            if (_offset.HasValue)
                sql += " OFFSET " + _offset.Value;
        }

        var parameters = _whereBindings
            .Concat(_joinBindings)
            .Concat(_havingBindings)
            .ToList();

        sql = ReplacePlaceholders(sql);

        return (sql, parameters);
    }

    /// <summary>
    /// Executes the query and returns all matching models.
    /// </summary>
    /// <returns>A list of matching models.</returns>
    public List<TModel> Get()
    {
        NoireDbModelBase<TModel>.EnsureTable();
        var query = ToSql();
        var results = _db.FetchAll(query.Sql, query.Parameters);

        var models = new List<TModel>();
        foreach (var result in results)
        {
            var model = NoireDbModelBase<TModel>.FromDatabaseRow(result);
            models.Add(model);
        }

        return models;
    }

    /// <summary>
    /// Executes the query and returns the first matching model.
    /// </summary>
    /// <returns>The first matching model, or null if no matches.</returns>
    public TModel? First()
    {
        Limit(1);
        NoireDbModelBase<TModel>.EnsureTable();
        var query = ToSql();
        var result = _db.Fetch(query.Sql, query.Parameters);

        if (result == null)
            return null;

        return NoireDbModelBase<TModel>.FromDatabaseRow(result);
    }

    /// <summary>
    /// Executes the query and returns the value of a single column.
    /// </summary>
    /// <param name="column">The column name.</param>
    /// <returns>The column value, or null if no matches.</returns>
    public object? Value(string column)
    {
        Select(column);
        NoireDbModelBase<TModel>.EnsureTable();
        var query = ToSql();
        return _db.FetchScalar(query.Sql, query.Parameters);
    }

    /// <summary>
    /// Executes a count aggregate.
    /// </summary>
    /// <param name="column">The column to count.</param>
    /// <returns>The count result.</returns>
    public int Count(string column = "*")
    {
        Select($"COUNT({column}) as count");
        NoireDbModelBase<TModel>.EnsureTable();
        var query = ToSql();
        var result = _db.Fetch(query.Sql, query.Parameters);
        if (result == null || !result.TryGetValue("count", out var value) || value == null)
            return 0;

        return Convert.ToInt32(value);
    }

    /// <summary>
    /// Executes an average aggregate.
    /// </summary>
    /// <param name="column">The column to average.</param>
    /// <returns>The average result.</returns>
    public double Avg(string column)
    {
        Select($"AVG({column}) as avg");
        NoireDbModelBase<TModel>.EnsureTable();
        var query = ToSql();
        var result = _db.Fetch(query.Sql, query.Parameters);
        if (result == null || !result.TryGetValue("avg", out var value) || value == null)
            return 0d;

        return Convert.ToDouble(value);
    }

    /// <summary>
    /// Executes a sum aggregate.
    /// </summary>
    /// <param name="column">The column to sum.</param>
    /// <returns>The sum result.</returns>
    public double Sum(string column)
    {
        Select($"SUM({column}) as sum");
        NoireDbModelBase<TModel>.EnsureTable();
        var query = ToSql();
        var result = _db.Fetch(query.Sql, query.Parameters);
        if (result == null || !result.TryGetValue("sum", out var value) || value == null)
            return 0d;

        return Convert.ToDouble(value);
    }

    /// <summary>
    /// Executes a minimum aggregate.
    /// </summary>
    /// <param name="column">The column to aggregate.</param>
    /// <returns>The minimum result, or null if no matches.</returns>
    public object? Min(string column)
    {
        Select($"MIN({column}) as min");
        NoireDbModelBase<TModel>.EnsureTable();
        var query = ToSql();
        var result = _db.Fetch(query.Sql, query.Parameters);
        if (result == null || !result.TryGetValue("min", out var value))
            return null;

        return value;
    }

    /// <summary>
    /// Executes a maximum aggregate.
    /// </summary>
    /// <param name="column">The column to aggregate.</param>
    /// <returns>The maximum result, or null if no matches.</returns>
    public object? Max(string column)
    {
        Select($"MAX({column}) as max");
        NoireDbModelBase<TModel>.EnsureTable();
        var query = ToSql();
        var result = _db.Fetch(query.Sql, query.Parameters);
        if (result == null || !result.TryGetValue("max", out var value))
            return null;

        return value;
    }

    /// <summary>
    /// Determines whether the query returns any rows.
    /// </summary>
    /// <returns>True if any rows match the query; otherwise, false.</returns>
    public bool Exists()
    {
        return Count() > 0;
    }

    /// <summary>
    /// Updates rows matching the current query.
    /// </summary>
    /// <param name="values">The values to update.</param>
    /// <returns>The number of rows affected.</returns>
    public int Update(IReadOnlyDictionary<string, object?> values)
    {
        NoireDbModelBase<TModel>.EnsureTable();

        var setClauses = values.Keys.Select((key, index) => $"{NoireDatabase.EscapeColumn(key)} = @p{index}").ToArray();
        var parameters = new List<object?>(values.Values);

        var sql = $"UPDATE {NoireDatabase.EscapeColumn(_table)} SET {string.Join(", ", setClauses)}";
        var whereClause = CompileWheres();

        if (!string.IsNullOrWhiteSpace(whereClause))
            sql += " " + whereClause;

        var whereParams = _whereBindings.ToList();
        parameters.AddRange(whereParams);
        sql = ReplacePlaceholders(sql, values.Count);

        return _db.Execute(sql, parameters);
    }

    /// <summary>
    /// Deletes rows matching the current query.
    /// </summary>
    /// <returns>The number of rows affected.</returns>
    public int Delete()
    {
        NoireDbModelBase<TModel>.EnsureTable();
        var sql = $"DELETE FROM {NoireDatabase.EscapeColumn(_table)}";
        var whereClause = CompileWheres();

        if (!string.IsNullOrWhiteSpace(whereClause))
            sql += " " + whereClause;

        var parameters = _whereBindings.ToList();
        sql = ReplacePlaceholders(sql);

        return _db.Execute(sql, parameters);
    }

    /// <summary>
    /// Inserts a row into the current table.
    /// </summary>
    /// <param name="values">The values to insert.</param>
    /// <returns>The ID of the inserted row.</returns>
    public long Insert(IReadOnlyDictionary<string, object?> values)
    {
        NoireDbModelBase<TModel>.EnsureTable();
        return _db.Insert(_table, values);
    }

    /// <summary>
    /// Executes the query and returns paginated results.
    /// </summary>
    /// <param name="perPage">The number of items per page.</param>
    /// <param name="page">The page number.</param>
    /// <returns>A paginated result containing the items and pagination metadata.</returns>
    public PaginatedResult<TModel> Paginate(int perPage = 15, int page = 1)
    {
        page = Math.Max(1, page);
        perPage = Math.Max(1, perPage);

        var originalColumns = _columns.ToList();
        var originalLimit = _limit;
        var originalOffset = _offset;

        var total = Count();
        _columns.Clear();
        _columns.AddRange(originalColumns);

        var offset = (page - 1) * perPage;
        Limit(perPage).Offset(offset);
        var items = Get();

        _limit = originalLimit;
        _offset = originalOffset;

        var lastPage = Math.Max(1, (int)Math.Ceiling(total / (double)perPage));
        var nextPage = page < lastPage ? page + 1 : (int?)null;
        var prevPage = page > 1 ? page - 1 : (int?)null;

        return new PaginatedResult<TModel>(
            items,
            new PaginationMetadata(total, perPage, page, lastPage, nextPage, prevPage, offset + 1, Math.Min(offset + perPage, total))
        );
    }

    /// <summary>
    /// Retrieves values for a column, optionally keyed by another column.<br/>
    /// In short, this method can be used to get a list of values from a single column, or a list of key-value pairs if a key column is specified.
    /// </summary>
    /// <param name="column">The value column.</param>
    /// <param name="key">The key column.</param>
    /// <returns>A list of values or key-value pairs.</returns>
    public List<object?> Pluck(string column, string? key = null)
    {
        if (key != null)
            Select(column, key);
        else
            Select(column);

        var results = Get();
        var output = new List<object?>();

        foreach (var row in results)
        {
            if (key == null)
            {
                output.Add(row.GetColumn<object?>(column));
            }
            else
            {
                var itemKey = row.GetColumn<object?>(key);
                var value = row.GetColumn<object?>(column);
                output.Add(new KeyValuePair<object?, object?>(itemKey, value));
            }
        }

        return output;
    }

    /// <summary>
    /// Processes the query results in chunks.<br/>
    /// In short, this method can be used to efficiently process large result sets by retrieving them in smaller batches,
    /// reducing memory usage and improving performance when working with large datasets.<br/>
    /// The callback takes the parameters of the current chunk of results and the page number,
    /// and it should return true or null to continue processing the next chunk, false to stop.
    /// </summary>
    /// <param name="count">The chunk size.</param>
    /// <param name="callback">The callback invoked for each chunk.</param>
    /// <returns>True if all chunks were processed; false if processing was stopped early.</returns>
    public bool Chunk(int count, Func<List<TModel>, int, bool?> callback)
    {
        var page = 1;

        while (true)
        {
            var results = Paginate(count, page).Items;
            if (results.Count == 0)
                break;

            var continueProcessing = callback(results, page);
            if (continueProcessing == false)
                return false;

            if (results.Count < count)
                break;

            page++;
        }

        return true;
    }

    #endregion

    #region Private Methods

    private string CompileColumns() => string.Join(", ", _columns);

    private string CompileTable()
    {
        var escapedTable = NoireDatabase.EscapeColumn(_table);
        return _tableAlias == null ? escapedTable : $"{escapedTable} AS {_tableAlias}";
    }

    private string CompileJoins()
    {
        var sql = new List<string>();

        foreach (var join in _joins)
        {
            var clause = $"{join.Type} JOIN {NoireDatabase.EscapeColumn(join.Table)} ON {NoireDatabase.EscapeColumn(join.First!)} {join.Operator} {NoireDatabase.EscapeColumn(join.Second!)}";
            sql.Add(clause);
        }

        return string.Join(" ", sql);
    }

    private string CompileWheres()
    {
        if (_wheres.Count == 0)
            return string.Empty;

        return "WHERE " + CompileWhereClauses(_wheres);
    }

    private string CompileWhereClauses(IReadOnlyList<WhereClause> wheres)
    {
        var sql = new List<string>();

        for (var index = 0; index < wheres.Count; index++)
        {
            var where = wheres[index];
            if (index > 0)
                sql.Add(where.Boolean);

            switch (where.Type)
            {
                case WhereClauseType.Basic:
                    sql.Add($"{NoireDatabase.EscapeColumn(where.Column!)} {where.Operator} ?");
                    break;
                case WhereClauseType.In:
                case WhereClauseType.NotIn:
                    var placeholders = string.Join(", ", where.Values!.Select(_ => "?"));
                    var op = where.Type == WhereClauseType.In ? "IN" : "NOT IN";
                    sql.Add($"{NoireDatabase.EscapeColumn(where.Column!)} {op} ({placeholders})");
                    break;
                case WhereClauseType.Null:
                case WhereClauseType.NotNull:
                    var nullOp = where.Type == WhereClauseType.Null ? "IS NULL" : "IS NOT NULL";
                    sql.Add($"{NoireDatabase.EscapeColumn(where.Column!)} {nullOp}");
                    break;
                case WhereClauseType.Between:
                case WhereClauseType.NotBetween:
                    var betweenOp = where.Type == WhereClauseType.Between ? "BETWEEN" : "NOT BETWEEN";
                    sql.Add($"{NoireDatabase.EscapeColumn(where.Column!)} {betweenOp} ? AND ?");
                    break;
                case WhereClauseType.Nested:
                    var nestedSql = CompileWhereClauses(where.NestedQuery!._wheres);
                    sql.Add($"({nestedSql})");
                    break;
                case WhereClauseType.Raw:
                    sql.Add($"({where.RawSql})");
                    break;
            }
        }

        return string.Join(" ", sql);
    }

    private string CompileOrders()
    {
        if (_orders.Count == 0)
            return string.Empty;

        var orders = _orders.Select(order => $"{NoireDatabase.EscapeColumn(order.Column)} {order.Direction}");
        return "ORDER BY " + string.Join(", ", orders);
    }

    private string CompileHavings()
    {
        if (_havings.Count == 0)
            return string.Empty;

        var sql = new List<string>();

        for (var index = 0; index < _havings.Count; index++)
        {
            var having = _havings[index];
            if (index > 0)
                sql.Add(having.Boolean);

            sql.Add($"{NoireDatabase.EscapeColumn(having.Column)} {having.Operator} ?");
        }

        return "HAVING " + string.Join(" ", sql);
    }

    private static string ReplacePlaceholders(string sql, int startIndex = 0)
    {
        if (!sql.Contains('?'))
            return sql;

        var builder = new System.Text.StringBuilder();
        var index = startIndex;

        foreach (var ch in sql)
        {
            if (ch == '?')
            {
                builder.Append($"@p{index}");
                index++;
            }
            else
            {
                builder.Append(ch);
            }
        }

        return builder.ToString();
    }

    #endregion

    #region Private Records and Enums

    private sealed record JoinClause(string Type, string Table, string? First = null, string? Operator = null, string? Second = null);

    private sealed record OrderClause(string Column, string Direction);

    private sealed record HavingClause(string Column, string Operator, object? Value, string Boolean);

    private sealed record WhereClause(WhereClauseType Type, string Boolean, string? Column = null, string? Operator = null, object? Value = null, IReadOnlyList<object?>? Values = null, string? RawSql = null, QueryBuilder<TModel>? NestedQuery = null);

    private enum WhereClauseType
    {
        Basic,
        In,
        NotIn,
        Null,
        NotNull,
        Between,
        NotBetween,
        Nested,
        Raw
    }

    #endregion
}

/// <summary>
/// Represents pagination metadata for a query.
/// </summary>
/// <param name="Total">The total number of items.</param>
/// <param name="PerPage">The number of items per page.</param>
/// <param name="CurrentPage">The current page number.</param>
/// <param name="LastPage">The last page number.</param>
/// <param name="NextPage">The next page number.</param>
/// <param name="PrevPage">The previous page number.</param>
/// <param name="From">The starting item index.</param>
/// <param name="To">The ending item index.</param>
public sealed record PaginationMetadata(int Total, int PerPage, int CurrentPage, int LastPage, int? NextPage, int? PrevPage, int From, int To);

/// <summary>
/// Represents a paginated query result.
/// </summary>
/// <typeparam name="TModel">The model type.</typeparam>
/// <param name="Items">The result items.</param>
/// <param name="Pagination">The pagination metadata.</param>
public sealed record PaginatedResult<TModel>(List<TModel> Items, PaginationMetadata Pagination);
