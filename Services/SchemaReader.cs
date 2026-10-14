using System.Data.Common;
using DBViewer.Models;
using Microsoft.Data.Sqlite;
using Microsoft.Data.SqlClient;
using MySqlConnector;
using Npgsql;
namespace DBViewer.Services;
public sealed class SchemaReader
{
    public async Task<DatabaseSchema> ReadAsync(ConnectionRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.ConnectionString)) throw new ArgumentException();
        await using DbConnection connection = request.Provider switch {
            "postgresql" => new NpgsqlConnection(request.ConnectionString),
            "mysql" => new MySqlConnection(request.ConnectionString),
            "sqlserver" => new SqlConnection(request.ConnectionString),
            "sqlite" => new SqliteConnection(new SqliteConnectionStringBuilder(request.ConnectionString) { Mode = SqliteOpenMode.ReadOnly }.ToString()),
            _ => throw new ArgumentException()
        };
        await connection.OpenAsync(ct);
        return request.Provider == "sqlite" ? await ReadSqlite(connection, ct) : await ReadServer(connection, request.Provider, ct);
    }
    static async Task<List<Dictionary<string, string>>> Query(DbConnection connection, string sql, CancellationToken ct, string? name = null)
    {
        await using var cmd = connection.CreateCommand(); cmd.CommandText = sql; cmd.CommandTimeout = 20;
        if (name != null) { var p = cmd.CreateParameter(); p.ParameterName = "@name"; p.Value = name; cmd.Parameters.Add(p); }
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var rows = new List<Dictionary<string, string>>();
        while (await reader.ReadAsync(ct)) {
            var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < reader.FieldCount; i++) row[reader.GetName(i)] = reader.IsDBNull(i) ? "" : Convert.ToString(reader.GetValue(i), System.Globalization.CultureInfo.InvariantCulture) ?? "";
            rows.Add(row);
        }
        return rows;
    }
    static async Task<DatabaseSchema> ReadSqlite(DbConnection db, CancellationToken ct)
    {
        var tables = new List<TableSchema>(); var relations = new List<Relation>();
        foreach (var t in await Query(db, "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%' ORDER BY name", ct)) {
            var name = t["name"];
            var columns = (await Query(db, "SELECT * FROM pragma_table_xinfo(@name) ORDER BY cid", ct, name)).Select(c => {
                var type = c["type"]; var start = type.IndexOf('('); var end = type.LastIndexOf(')');
                return new ColumnSchema(c["name"], start < 0 ? type : type[..start], start >= 0 && end > start ? type[(start+1)..end] : null,
                    c["notnull"] == "0" && c["pk"] == "0", c["pk"] != "0", c["dflt_value"], "");
            }).ToList();
            tables.Add(new("main", name, "", columns));
            foreach (var f in await Query(db, "SELECT * FROM pragma_foreign_key_list(@name)", ct, name)) {
                var targetColumn = f["to"];
                if (targetColumn == "") {
                    var pk = (await Query(db, "SELECT name, pk FROM pragma_table_info(@name) WHERE pk > 0 ORDER BY pk", ct, f["table"]));
                    if (int.TryParse(f["seq"], out var seq) && seq < pk.Count) targetColumn = pk[seq]["name"];
                }
                relations.Add(new("main", name, f["from"], "main", f["table"], targetColumn));
            }
        }
        return new(Path.GetFileName(db.DataSource), "sqlite", tables, relations);
    }
    static async Task<DatabaseSchema> ReadServer(DbConnection db, string provider, CancellationToken ct)
    {
        var sql = provider switch {
            "postgresql" => PgColumns,
            "mysql" => MyColumns,
            _ => MsColumns
        };
        var rows = await Query(db, sql, ct);
        var tables = rows.GroupBy(r => (Schema: r["schema_name"], Name: r["table_name"])).Select(g => new TableSchema(g.Key.Schema, g.Key.Name, g.First()["table_comment"],
            g.Select(c => new ColumnSchema(c["column_name"], c["data_type"], Empty(c["length"]), c["nullable"] == "1", c["is_pk"] == "1", Empty(c["default_value"]), c["comment"])).ToList())).ToList();
        var refs = await Query(db, provider switch { "postgresql" => PgRelations, "mysql" => MyRelations, _ => MsRelations }, ct);
        return new(db.Database, provider, tables, refs.Select(r => new Relation(r["from_schema"], r["from_table"], r["from_column"], r["to_schema"], r["to_table"], r["to_column"])).ToList());
    }
    static string? Empty(string s) => s.Length == 0 ? null : s;
    const string PgColumns = """
    SELECT n.nspname schema_name, t.relname table_name, COALESCE(obj_description(t.oid),'') table_comment,
      a.attname column_name, typ.typname data_type,
      CASE WHEN typ.typname IN ('varchar','bpchar') AND a.atttypmod > 4 THEN (a.atttypmod-4)::text
           WHEN typ.typname='numeric' AND a.atttypmod>=4 THEN (((a.atttypmod-4)>>16)&65535)::text || ',' || ((a.atttypmod-4)&65535)::text ELSE '' END length,
      CASE WHEN a.attnotnull THEN '0' ELSE '1' END nullable,
      CASE WHEN EXISTS(SELECT 1 FROM pg_index i WHERE i.indrelid=t.oid AND i.indisprimary AND a.attnum=ANY(i.indkey)) THEN '1' ELSE '0' END is_pk,
      COALESCE(pg_get_expr(d.adbin,d.adrelid),'') default_value, COALESCE(col_description(t.oid,a.attnum),'') comment
    FROM pg_class t JOIN pg_namespace n ON n.oid=t.relnamespace JOIN pg_attribute a ON a.attrelid=t.oid
      JOIN pg_type typ ON typ.oid=a.atttypid LEFT JOIN pg_attrdef d ON d.adrelid=t.oid AND d.adnum=a.attnum
    WHERE t.relkind IN ('r','p') AND n.nspname NOT IN ('pg_catalog','information_schema') AND n.nspname NOT LIKE 'pg_toast%'
      AND a.attnum>0 AND NOT a.attisdropped ORDER BY n.nspname,t.relname,a.attnum
    """;
    const string PgRelations = """
    SELECT sn.nspname from_schema, st.relname from_table, sa.attname from_column,
      tn.nspname to_schema, tt.relname to_table, ta.attname to_column
    FROM pg_constraint c JOIN pg_class st ON st.oid=c.conrelid JOIN pg_namespace sn ON sn.oid=st.relnamespace
      JOIN pg_class tt ON tt.oid=c.confrelid JOIN pg_namespace tn ON tn.oid=tt.relnamespace
      JOIN LATERAL unnest(c.conkey,c.confkey) AS k(src,dst) ON true
      JOIN pg_attribute sa ON sa.attrelid=st.oid AND sa.attnum=k.src JOIN pg_attribute ta ON ta.attrelid=tt.oid AND ta.attnum=k.dst
    WHERE c.contype='f'
    """;
    const string MyColumns = """
    SELECT c.TABLE_SCHEMA schema_name,c.TABLE_NAME table_name,t.TABLE_COMMENT table_comment,c.COLUMN_NAME column_name,c.DATA_TYPE data_type,
      COALESCE(CAST(c.CHARACTER_MAXIMUM_LENGTH AS CHAR),CASE WHEN c.NUMERIC_PRECISION IS NOT NULL THEN CONCAT(c.NUMERIC_PRECISION,',',COALESCE(c.NUMERIC_SCALE,0)) END,'') length,
      CASE WHEN c.IS_NULLABLE='YES' THEN '1' ELSE '0' END nullable,CASE WHEN c.COLUMN_KEY='PRI' THEN '1' ELSE '0' END is_pk,
      COALESCE(c.COLUMN_DEFAULT,'') default_value,c.COLUMN_COMMENT comment
    FROM information_schema.COLUMNS c JOIN information_schema.TABLES t ON t.TABLE_SCHEMA=c.TABLE_SCHEMA AND t.TABLE_NAME=c.TABLE_NAME
    WHERE c.TABLE_SCHEMA=DATABASE() AND t.TABLE_TYPE='BASE TABLE' ORDER BY c.TABLE_NAME,c.ORDINAL_POSITION
    """;
    const string MyRelations = """
    SELECT TABLE_SCHEMA from_schema,TABLE_NAME from_table,COLUMN_NAME from_column,REFERENCED_TABLE_SCHEMA to_schema,
      REFERENCED_TABLE_NAME to_table,REFERENCED_COLUMN_NAME to_column FROM information_schema.KEY_COLUMN_USAGE
    WHERE TABLE_SCHEMA=DATABASE() AND REFERENCED_TABLE_NAME IS NOT NULL
    """;
    const string MsColumns = """
    SELECT s.name schema_name,t.name table_name,COALESCE(CONVERT(nvarchar(max),te.value),'') table_comment,c.name column_name,ty.name data_type,
      CASE WHEN bt.name IN ('nvarchar','nchar') THEN CASE WHEN c.max_length=-1 THEN 'max' ELSE CAST(c.max_length/2 AS varchar) END
           WHEN bt.name IN ('varchar','char','binary','varbinary') THEN CASE WHEN c.max_length=-1 THEN 'max' ELSE CAST(c.max_length AS varchar) END
           WHEN bt.name IN ('decimal','numeric') THEN CONCAT(c.precision,',',c.scale)
           WHEN bt.name IN ('datetime2','datetimeoffset','time') THEN CAST(c.scale AS varchar) ELSE '' END length,
      CASE WHEN c.is_nullable=1 THEN '1' ELSE '0' END nullable,
      CASE WHEN EXISTS(SELECT 1 FROM sys.index_columns ic JOIN sys.indexes i ON i.object_id=ic.object_id AND i.index_id=ic.index_id WHERE ic.object_id=t.object_id AND ic.column_id=c.column_id AND i.is_primary_key=1) THEN '1' ELSE '0' END is_pk,
      COALESCE(dc.definition,'') default_value,COALESCE(CONVERT(nvarchar(max),ce.value),'') comment
    FROM sys.tables t JOIN sys.schemas s ON s.schema_id=t.schema_id JOIN sys.columns c ON c.object_id=t.object_id
      JOIN sys.types ty ON ty.user_type_id=c.user_type_id
      LEFT JOIN sys.types bt ON bt.user_type_id=c.system_type_id
      LEFT JOIN sys.default_constraints dc ON dc.object_id=c.default_object_id
      LEFT JOIN sys.extended_properties te ON te.major_id=t.object_id AND te.minor_id=0 AND te.name='MS_Description' AND te.class=1
      LEFT JOIN sys.extended_properties ce ON ce.major_id=t.object_id AND ce.minor_id=c.column_id AND ce.name='MS_Description' AND ce.class=1
    ORDER BY s.name,t.name,c.column_id
    """;
    const string MsRelations = """
    SELECT ss.name from_schema,st.name from_table,sc.name from_column,ts.name to_schema,tt.name to_table,tc.name to_column
    FROM sys.foreign_key_columns f JOIN sys.tables st ON st.object_id=f.parent_object_id JOIN sys.schemas ss ON ss.schema_id=st.schema_id
    JOIN sys.columns sc ON sc.object_id=st.object_id AND sc.column_id=f.parent_column_id
    JOIN sys.tables tt ON tt.object_id=f.referenced_object_id JOIN sys.schemas ts ON ts.schema_id=tt.schema_id
    JOIN sys.columns tc ON tc.object_id=tt.object_id AND tc.column_id=f.referenced_column_id
    """;
}
