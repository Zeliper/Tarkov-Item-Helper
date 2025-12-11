using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using TarkovDBEditor.Models;

namespace TarkovDBEditor.Services;

/// <summary>
/// SQLite database service with dynamic schema management
/// </summary>
public class DatabaseService
{
    private static DatabaseService? _instance;
    public static DatabaseService Instance => _instance ??= new DatabaseService();

    private const string MetaTableName = "_schema_meta";
    private string _databasePath = string.Empty;
    private string _connectionString = string.Empty;

    public string DatabasePath => _databasePath;
    public bool IsConnected => !string.IsNullOrEmpty(_connectionString);

    public event EventHandler? SchemaChanged;
    public event EventHandler<string>? ErrorOccurred;

    private DatabaseService() { }

    /// <summary>
    /// Initialize database at default location
    /// </summary>
    public void Initialize()
    {
        var appDir = AppDomain.CurrentDomain.BaseDirectory;
        var dbPath = Path.Combine(appDir, "tarkov_data.db");
        Connect(dbPath);
    }

    /// <summary>
    /// Connect to a database file (creates if not exists)
    /// </summary>
    public void Connect(string databasePath)
    {
        _databasePath = databasePath;
        _connectionString = $"Data Source={databasePath}";

        EnsureMetaTableExists();
    }

    /// <summary>
    /// Create or open a new database file
    /// </summary>
    public void CreateDatabase(string databasePath)
    {
        Connect(databasePath);
    }

    private SqliteConnection GetConnection()
    {
        var conn = new SqliteConnection(_connectionString);
        conn.Open();
        return conn;
    }

    /// <summary>
    /// Ensure the schema metadata table exists
    /// </summary>
    private void EnsureMetaTableExists()
    {
        using var conn = GetConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $@"
            CREATE TABLE IF NOT EXISTS {MetaTableName} (
                TableName TEXT PRIMARY KEY,
                DisplayName TEXT,
                SchemaJson TEXT NOT NULL,
                CreatedAt TEXT DEFAULT CURRENT_TIMESTAMP,
                UpdatedAt TEXT DEFAULT CURRENT_TIMESTAMP
            )";
        cmd.ExecuteNonQuery();
    }

    #region Schema Management

    /// <summary>
    /// Get all table schemas
    /// </summary>
    public ObservableCollection<TableSchema> GetAllTableSchemas()
    {
        var schemas = new ObservableCollection<TableSchema>();

        using var conn = GetConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT TableName, DisplayName, SchemaJson FROM {MetaTableName}";

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var tableName = reader.GetString(0);
            var displayName = reader.IsDBNull(1) ? tableName : reader.GetString(1);
            var schemaJson = reader.GetString(2);

            var columns = JsonSerializer.Deserialize<List<ColumnSchema>>(schemaJson) ?? new();
            schemas.Add(new TableSchema
            {
                Name = tableName,
                DisplayName = displayName,
                Columns = new ObservableCollection<ColumnSchema>(columns)
            });
        }

        return schemas;
    }

    /// <summary>
    /// Get a specific table schema
    /// </summary>
    public TableSchema? GetTableSchema(string tableName)
    {
        using var conn = GetConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT DisplayName, SchemaJson FROM {MetaTableName} WHERE TableName = @name";
        cmd.Parameters.AddWithValue("@name", tableName);

        using var reader = cmd.ExecuteReader();
        if (reader.Read())
        {
            var displayName = reader.IsDBNull(0) ? tableName : reader.GetString(0);
            var schemaJson = reader.GetString(1);
            var columns = JsonSerializer.Deserialize<List<ColumnSchema>>(schemaJson) ?? new();

            return new TableSchema
            {
                Name = tableName,
                DisplayName = displayName,
                Columns = new ObservableCollection<ColumnSchema>(columns)
            };
        }

        return null;
    }

    /// <summary>
    /// Create a new table with the given schema
    /// </summary>
    public bool CreateTable(TableSchema schema)
    {
        try
        {
            using var conn = GetConnection();
            using var transaction = conn.BeginTransaction();

            // Create the actual table
            var createSql = BuildCreateTableSql(schema);
            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = transaction;
                cmd.CommandText = createSql;
                cmd.ExecuteNonQuery();
            }

            // Save schema metadata
            var schemaJson = JsonSerializer.Serialize(schema.Columns.ToList());
            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = transaction;
                cmd.CommandText = $@"
                    INSERT INTO {MetaTableName} (TableName, DisplayName, SchemaJson)
                    VALUES (@name, @displayName, @schema)";
                cmd.Parameters.AddWithValue("@name", schema.Name);
                cmd.Parameters.AddWithValue("@displayName", schema.DisplayName);
                cmd.Parameters.AddWithValue("@schema", schemaJson);
                cmd.ExecuteNonQuery();
            }

            transaction.Commit();
            SchemaChanged?.Invoke(this, EventArgs.Empty);
            return true;
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, ex.Message);
            return false;
        }
    }

    /// <summary>
    /// Add a column to an existing table
    /// </summary>
    public bool AddColumn(string tableName, ColumnSchema column)
    {
        try
        {
            using var conn = GetConnection();
            using var transaction = conn.BeginTransaction();

            // ALTER TABLE to add column
            var sqlType = GetSqlType(column.Type);
            var nullable = column.IsRequired ? "NOT NULL" : "";
            var defaultVal = !string.IsNullOrEmpty(column.DefaultValue)
                ? $"DEFAULT {GetDefaultValueSql(column)}"
                : (column.IsRequired ? "DEFAULT ''" : "");

            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = transaction;
                cmd.CommandText = $"ALTER TABLE [{tableName}] ADD COLUMN [{column.Name}] {sqlType} {nullable} {defaultVal}";
                cmd.ExecuteNonQuery();
            }

            // Update schema metadata
            var schema = GetTableSchemaInternal(conn, tableName);
            if (schema != null)
            {
                schema.Columns.Add(column);
                UpdateSchemaMetadata(conn, transaction, schema);
            }

            transaction.Commit();
            SchemaChanged?.Invoke(this, EventArgs.Empty);
            return true;
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, ex.Message);
            return false;
        }
    }

    /// <summary>
    /// Delete a table
    /// </summary>
    public bool DeleteTable(string tableName)
    {
        try
        {
            using var conn = GetConnection();
            using var transaction = conn.BeginTransaction();

            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = transaction;
                cmd.CommandText = $"DROP TABLE IF EXISTS [{tableName}]";
                cmd.ExecuteNonQuery();
            }

            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = transaction;
                cmd.CommandText = $"DELETE FROM {MetaTableName} WHERE TableName = @name";
                cmd.Parameters.AddWithValue("@name", tableName);
                cmd.ExecuteNonQuery();
            }

            transaction.Commit();
            SchemaChanged?.Invoke(this, EventArgs.Empty);
            return true;
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, ex.Message);
            return false;
        }
    }

    /// <summary>
    /// Update table display name
    /// </summary>
    public bool UpdateTableDisplayName(string tableName, string displayName)
    {
        try
        {
            using var conn = GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"UPDATE {MetaTableName} SET DisplayName = @displayName, UpdatedAt = CURRENT_TIMESTAMP WHERE TableName = @name";
            cmd.Parameters.AddWithValue("@displayName", displayName);
            cmd.Parameters.AddWithValue("@name", tableName);
            cmd.ExecuteNonQuery();

            SchemaChanged?.Invoke(this, EventArgs.Empty);
            return true;
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, ex.Message);
            return false;
        }
    }

    private TableSchema? GetTableSchemaInternal(SqliteConnection conn, string tableName)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT DisplayName, SchemaJson FROM {MetaTableName} WHERE TableName = @name";
        cmd.Parameters.AddWithValue("@name", tableName);

        using var reader = cmd.ExecuteReader();
        if (reader.Read())
        {
            var displayName = reader.IsDBNull(0) ? tableName : reader.GetString(0);
            var schemaJson = reader.GetString(1);
            var columns = JsonSerializer.Deserialize<List<ColumnSchema>>(schemaJson) ?? new();

            return new TableSchema
            {
                Name = tableName,
                DisplayName = displayName,
                Columns = new ObservableCollection<ColumnSchema>(columns)
            };
        }
        return null;
    }

    private void UpdateSchemaMetadata(SqliteConnection conn, SqliteTransaction transaction, TableSchema schema)
    {
        var schemaJson = JsonSerializer.Serialize(schema.Columns.ToList());
        using var cmd = conn.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = $@"
            UPDATE {MetaTableName}
            SET SchemaJson = @schema, DisplayName = @displayName, UpdatedAt = CURRENT_TIMESTAMP
            WHERE TableName = @name";
        cmd.Parameters.AddWithValue("@schema", schemaJson);
        cmd.Parameters.AddWithValue("@displayName", schema.DisplayName);
        cmd.Parameters.AddWithValue("@name", schema.Name);
        cmd.ExecuteNonQuery();
    }

    private string BuildCreateTableSql(TableSchema schema)
    {
        var sb = new StringBuilder();
        sb.Append($"CREATE TABLE [{schema.Name}] (");

        var columnDefs = new List<string>();
        var foreignKeys = new List<string>();

        foreach (var col in schema.Columns)
        {
            var def = new StringBuilder();
            def.Append($"[{col.Name}] {GetSqlType(col.Type)}");

            if (col.IsPrimaryKey)
            {
                def.Append(" PRIMARY KEY");
                if (col.IsAutoIncrement)
                    def.Append(" AUTOINCREMENT");
            }

            if (col.IsRequired && !col.IsPrimaryKey)
                def.Append(" NOT NULL");

            if (!string.IsNullOrEmpty(col.DefaultValue))
                def.Append($" DEFAULT {GetDefaultValueSql(col)}");

            columnDefs.Add(def.ToString());

            if (col.IsForeignKey)
            {
                foreignKeys.Add($"FOREIGN KEY ([{col.Name}]) REFERENCES [{col.ForeignKeyTable}]([{col.ForeignKeyColumn}]) ON DELETE SET NULL");
            }
        }

        sb.Append(string.Join(", ", columnDefs));

        if (foreignKeys.Count > 0)
        {
            sb.Append(", ");
            sb.Append(string.Join(", ", foreignKeys));
        }

        sb.Append(')');
        return sb.ToString();
    }

    private static string GetSqlType(ColumnType type) => type switch
    {
        ColumnType.Integer => "INTEGER",
        ColumnType.Real => "REAL",
        ColumnType.Boolean => "INTEGER",
        ColumnType.DateTime => "TEXT",
        ColumnType.Json => "TEXT",
        _ => "TEXT"
    };

    private static string GetDefaultValueSql(ColumnSchema col)
    {
        if (string.IsNullOrEmpty(col.DefaultValue))
            return "NULL";

        return col.Type switch
        {
            ColumnType.Integer or ColumnType.Real or ColumnType.Boolean => col.DefaultValue,
            _ => $"'{col.DefaultValue.Replace("'", "''")}'"
        };
    }

    #endregion

    #region Data Operations

    /// <summary>
    /// Get all rows from a table
    /// </summary>
    public List<DataRow> GetTableData(string tableName)
    {
        var rows = new List<DataRow>();
        var schema = GetTableSchema(tableName);
        if (schema == null) return rows;

        using var conn = GetConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT * FROM [{tableName}]";

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var row = new DataRow();
            foreach (var col in schema.Columns)
            {
                var ordinal = reader.GetOrdinal(col.Name);
                if (!reader.IsDBNull(ordinal))
                {
                    row[col.Name] = col.Type switch
                    {
                        ColumnType.Integer => reader.GetInt64(ordinal),
                        ColumnType.Real => reader.GetDouble(ordinal),
                        ColumnType.Boolean => reader.GetInt64(ordinal) != 0,
                        _ => reader.GetString(ordinal)
                    };
                }
                else
                {
                    row[col.Name] = null;
                }
            }
            rows.Add(row);
        }

        return rows;
    }

    /// <summary>
    /// Insert a new row
    /// </summary>
    public bool InsertRow(string tableName, DataRow row)
    {
        try
        {
            var schema = GetTableSchema(tableName);
            if (schema == null) return false;

            using var conn = GetConnection();
            using var cmd = conn.CreateCommand();

            var columns = new List<string>();
            var parameters = new List<string>();

            foreach (var col in schema.Columns)
            {
                if (col.IsAutoIncrement) continue;
                if (!row.TryGetValue(col.Name, out var value) || value == null) continue;

                columns.Add($"[{col.Name}]");
                parameters.Add($"@{col.Name}");
                cmd.Parameters.AddWithValue($"@{col.Name}", ConvertToDbValue(value, col.Type));
            }

            cmd.CommandText = $"INSERT INTO [{tableName}] ({string.Join(", ", columns)}) VALUES ({string.Join(", ", parameters)})";
            cmd.ExecuteNonQuery();
            return true;
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, ex.Message);
            return false;
        }
    }

    /// <summary>
    /// Update an existing row
    /// </summary>
    public bool UpdateRow(string tableName, DataRow row, string primaryKeyColumn, object primaryKeyValue)
    {
        try
        {
            var schema = GetTableSchema(tableName);
            if (schema == null) return false;

            using var conn = GetConnection();
            using var cmd = conn.CreateCommand();

            var setClauses = new List<string>();

            foreach (var col in schema.Columns)
            {
                if (col.IsPrimaryKey) continue;
                if (!row.TryGetValue(col.Name, out var value)) continue;

                setClauses.Add($"[{col.Name}] = @{col.Name}");
                cmd.Parameters.AddWithValue($"@{col.Name}", value == null ? DBNull.Value : ConvertToDbValue(value, col.Type));
            }

            cmd.Parameters.AddWithValue("@pkValue", primaryKeyValue);
            cmd.CommandText = $"UPDATE [{tableName}] SET {string.Join(", ", setClauses)} WHERE [{primaryKeyColumn}] = @pkValue";
            cmd.ExecuteNonQuery();
            return true;
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, ex.Message);
            return false;
        }
    }

    /// <summary>
    /// Delete a row
    /// </summary>
    public bool DeleteRow(string tableName, string primaryKeyColumn, object primaryKeyValue)
    {
        try
        {
            using var conn = GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"DELETE FROM [{tableName}] WHERE [{primaryKeyColumn}] = @pkValue";
            cmd.Parameters.AddWithValue("@pkValue", primaryKeyValue);
            cmd.ExecuteNonQuery();
            return true;
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, ex.Message);
            return false;
        }
    }

    /// <summary>
    /// Get lookup data for foreign key columns
    /// </summary>
    public List<LookupItem> GetLookupData(string tableName, string idColumn, string? displayColumn = null)
    {
        var result = new List<LookupItem>();
        displayColumn ??= idColumn;

        using var conn = GetConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT [{idColumn}], [{displayColumn}] FROM [{tableName}]";

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var id = reader.GetValue(0);
            var display = reader.IsDBNull(1) ? id.ToString() ?? "" : reader.GetString(1);
            result.Add(new LookupItem { Id = id, Display = display });
        }

        return result;
    }

    private static object ConvertToDbValue(object value, ColumnType type)
    {
        return type switch
        {
            ColumnType.Boolean => value is bool b ? (b ? 1 : 0) : value,
            ColumnType.Integer => Convert.ToInt64(value),
            ColumnType.Real => Convert.ToDouble(value),
            _ => value.ToString() ?? ""
        };
    }

    #endregion
}
