namespace DBViewer.Models;
public record ConnectionRequest(string Provider, string ConnectionString);
public record DatabaseSchema(string Name, string Provider, List<TableSchema> Tables, List<Relation> Relations);
public record TableSchema(string Schema, string Name, string Comment, List<ColumnSchema> Columns);
public record ColumnSchema(string Name, string Type, string? Length, bool Nullable, bool PrimaryKey, string? DefaultValue, string Comment);
public record Relation(string FromSchema, string FromTable, string FromColumn, string ToSchema, string ToTable, string ToColumn);
