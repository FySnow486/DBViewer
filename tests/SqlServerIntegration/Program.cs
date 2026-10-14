using DBViewer.Models;
using DBViewer.Services;
using Microsoft.Data.SqlClient;
using System.Net.Http.Json;

// Read-only audit of the active saved connection; never print credentials.
if(args.Contains("--audit")){
    using var http=new HttpClient {BaseAddress=new Uri("http://127.0.0.1:5080"),Timeout=TimeSpan.FromSeconds(40)};
    var state=await http.GetFromJsonAsync<System.Text.Json.Nodes.JsonObject>("/api/connections");
    var id=state?["activeId"]?.ToString();
    if(id==null){Console.WriteLine("No active saved connection.");return 1;}
    var profile=await http.GetFromJsonAsync<ConnectionProfile>("/api/connections/"+id);
    if(profile?.Provider!="sqlserver"){Console.WriteLine("Active connection is not SQL Server.");return 1;}
    var response=await http.PostAsJsonAsync("/api/schema",new ConnectionRequest(profile.Provider,profile.ConnectionString));
    response.EnsureSuccessStatusCode();
    var schema=(await response.Content.ReadFromJsonAsync<DatabaseSchema>())!;
    await using var db=new SqlConnection(profile.ConnectionString);await db.OpenAsync();
    var actual=new HashSet<Relation>();
    await using(var command=new SqlCommand("""
        SELECT OBJECT_SCHEMA_NAME(f.parent_object_id),OBJECT_NAME(f.parent_object_id),COL_NAME(f.parent_object_id,f.parent_column_id),
        OBJECT_SCHEMA_NAME(f.referenced_object_id),OBJECT_NAME(f.referenced_object_id),COL_NAME(f.referenced_object_id,f.referenced_column_id)
        FROM sys.foreign_key_columns f
        """,db)){
        await using var reader=await command.ExecuteReaderAsync();
        while(await reader.ReadAsync())actual.Add(new(reader.GetString(0),reader.GetString(1),reader.GetString(2),reader.GetString(3),reader.GetString(4),reader.GetString(5)));
    }
    await using var counts=new SqlCommand("SELECT (SELECT COUNT(*) FROM sys.tables),(SELECT COUNT(*) FROM sys.foreign_keys),(SELECT COUNT(*) FROM sys.foreign_key_columns)",db);
    await using var countReader=await counts.ExecuteReaderAsync();await countReader.ReadAsync();
    var returned=schema.Relations.ToHashSet();
    var missing=actual.Except(returned).ToList();var extra=returned.Except(actual).ToList();
    var tables=schema.Tables.Select(t=>(t.Schema,t.Name)).ToHashSet();
    var unavailable=returned.Where(r=>!tables.Contains((r.FromSchema,r.FromTable))||!tables.Contains((r.ToSchema,r.ToTable))).ToList();
    Console.WriteLine($"Database: {schema.Name}; tables database/API: {countReader.GetInt32(0)}/{schema.Tables.Count}; FK constraints: {countReader.GetInt32(1)}; FK column pairs database/API: {countReader.GetInt32(2)}/{schema.Relations.Count}");
    Console.WriteLine($"Missing: {missing.Count}; extra: {extra.Count}; relations with absent endpoint table: {unavailable.Count}");
    foreach(var r in returned)Console.WriteLine($"{r.FromSchema}.{r.FromTable}.{r.FromColumn} -> {r.ToSchema}.{r.ToTable}.{r.ToColumn}");
    var involved=returned.SelectMany(r=>new[]{(r.FromSchema,r.FromTable),(r.ToSchema,r.ToTable)}).ToHashSet();
    Console.WriteLine($"Tables involved in declared FK: {involved.Count}; tables without declared FK: {tables.Count-involved.Count}");
    await File.WriteAllTextAsync("/tmp/dbviewer-audit-schema.json",System.Text.Json.JsonSerializer.Serialize(schema,new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)));
    return missing.Count+extra.Count+unavailable.Count==0?0:1;
}

// Password comes from stdin, never a command argument or a checked-in file.
var password = await Console.In.ReadToEndAsync();
if (string.IsNullOrEmpty(password)) { Console.Error.WriteLine("Missing test instance password on stdin."); return 2; }
var cs = new SqlConnectionStringBuilder {
    DataSource = Environment.GetEnvironmentVariable("DBVIEWER_SQLSERVER") ?? "localhost,1433",
    UserID = Environment.GetEnvironmentVariable("DBVIEWER_SQLUSER") ?? "sa",
    Password = password, InitialCatalog = "master", Encrypt = true,
    // Only this local test connection uses the container's self-signed certificate.
    TrustServerCertificate = true, ConnectTimeout = 10, Pooling = false
};
if(args.Contains("--save-local")){
    var targetDatabase=Environment.GetEnvironmentVariable("DBVIEWER_TEST_DATABASE");
    if(string.IsNullOrWhiteSpace(targetDatabase)){Console.Error.WriteLine("Set DBVIEWER_TEST_DATABASE before --save-local.");return 2;}
    cs.InitialCatalog=targetDatabase;
    using var http=new HttpClient {BaseAddress=new Uri("http://127.0.0.1:5080"),Timeout=TimeSpan.FromSeconds(40)};
    var schemaResponse=await http.PostAsJsonAsync("/api/schema",new ConnectionRequest("sqlserver",cs.ConnectionString));
    if(!schemaResponse.IsSuccessStatusCode){Console.WriteLine("Local database validation failed; no profile saved.");return 1;}
    var state=await http.GetFromJsonAsync<System.Text.Json.Nodes.JsonObject>("/api/connections");
    if(state?["items"]?.AsArray().Count>0){Console.WriteLine("Existing saved profiles preserved; skipped automatic save.");return 0;}
    var saved=await http.PostAsJsonAsync("/api/connections",new ConnectionProfile(null,"Local SQL Server test","sqlserver",cs.ConnectionString));
    Console.WriteLine(saved.IsSuccessStatusCode?"PASS: local database verified and encrypted profile saved; credentials omitted.":"FAIL: profile save");
    return saved.IsSuccessStatusCode?0:1;
}
if (args.Contains("--probe")) {
    var probeFailures = 0;
    using var http = new HttpClient { BaseAddress = new Uri(Environment.GetEnvironmentVariable("DBVIEWER_URL") ?? "http://127.0.0.1:5080"), Timeout = TimeSpan.FromSeconds(40) };
    foreach (var trust in new[] {false, true}) {
        cs.TrustServerCertificate = trust;
        try {
            await using var db = new SqlConnection(cs.ConnectionString); await db.OpenAsync();
            Console.WriteLine($"Direct connection / trust local certificate={trust}: PASS");
        } catch (SqlException ex) {
            Console.WriteLine($"Direct connection / trust local certificate={trust}: FAIL / SqlException {ex.Number} / TLS certificate hint={ex.ToString().Contains("certificate", StringComparison.OrdinalIgnoreCase)}");
        }
        var response = await http.PostAsJsonAsync("/api/schema",new ConnectionRequest("sqlserver",cs.ConnectionString));
        if(response.IsSuccessStatusCode) {
            var schema = await response.Content.ReadFromJsonAsync<DatabaseSchema>();
            Console.WriteLine($"HTTP / trust local certificate={trust}: {(int)response.StatusCode}, tables={schema?.Tables.Count}");
            if (!trust) probeFailures++;
        } else {
            var data = await response.Content.ReadFromJsonAsync<System.Text.Json.Nodes.JsonObject>();
            Console.WriteLine($"HTTP / trust local certificate={trust}: {(int)response.StatusCode} / {data?["error"]}");
            if (trust || data?["error"]?.ToString().Contains("证书验证失败") != true) probeFailures++;
        }
    }
    cs.TrustServerCertificate = true;
    foreach (var scenario in new[] {"login", "database"}) {
        var invalid = new SqlConnectionStringBuilder(cs.ConnectionString);
        if (scenario == "login") invalid.Password = Guid.NewGuid().ToString("N");
        else invalid.InitialCatalog = "DBViewer_Missing_" + Guid.NewGuid().ToString("N");
        var response = await http.PostAsJsonAsync("/api/schema",new ConnectionRequest("sqlserver",invalid.ConnectionString));
        var data = await response.Content.ReadFromJsonAsync<System.Text.Json.Nodes.JsonObject>();
        var message = data?["error"]?.ToString() ?? "";
        var passed = (int)response.StatusCode == 400 && message.Contains(scenario == "login" ? "登录失败" : "无法打开指定数据库") && !message.Contains(invalid.Password);
        Console.WriteLine($"{(passed?"PASS":"FAIL")}: {scenario} diagnosis / {message}");
        if (!passed) probeFailures++;
    }
    Console.WriteLine($"Read-only diagnosis: {probeFailures} failed check(s).");
    return probeFailures == 0 ? 0 : 1;
}
var databaseName = "DBViewer_Test_" + Guid.NewGuid().ToString("N");
var created = false;
var failures = new List<string>();
void Check(bool condition, string name) { Console.WriteLine($"{(condition ? "PASS" : "FAIL")}: {name}"); if (!condition) failures.Add(name); }
async Task Execute(SqlConnection db, string sql) { await using var command = new SqlCommand(sql, db) { CommandTimeout = 30 }; await command.ExecuteNonQueryAsync(); }
try {
    await using var master = new SqlConnection(cs.ConnectionString);
    await master.OpenAsync();
    await using (var version = new SqlCommand("SELECT CAST(SERVERPROPERTY('ProductVersion') AS nvarchar(128)), CAST(SERVERPROPERTY('Edition') AS nvarchar(128))", master)) {
        await using var reader = await version.ExecuteReaderAsync(); await reader.ReadAsync();
        Console.WriteLine($"Engine: {reader.GetString(0)} / {reader.GetString(1)}");
    }
    await Execute(master, $"CREATE DATABASE [{databaseName}]"); created = true;
    cs.InitialCatalog = databaseName;
    await using (var test = new SqlConnection(cs.ConnectionString)) {
        await test.OpenAsync();
        await Execute(test, "CREATE SCHEMA sales");
        await Execute(test, "CREATE TYPE dbo.ShortLabel FROM nvarchar(35) NULL");
        await Execute(test, "CREATE TYPE dbo.Amount FROM decimal(15,3) NULL");
        await Execute(test, """
        CREATE TABLE dbo.Parent (
          TenantId int NOT NULL, Id bigint NOT NULL,
          Title nvarchar(80) NOT NULL DEFAULT N'默认标题', Code varchar(25) NULL,
          Description nvarchar(max) NULL, Payload varbinary(max) NULL,
          Amount decimal(18,4) NOT NULL DEFAULT 0, CreatedAt datetime2(3) NULL,
          Clock time(4) NULL, OffsetAt datetimeoffset(2) NULL,
          AliasLabel dbo.ShortLabel NULL, AliasAmount dbo.Amount NULL,
          CONSTRAINT PK_Parent PRIMARY KEY (TenantId,Id));
        CREATE TABLE sales.Parent (Id int NOT NULL PRIMARY KEY, Name nchar(12) NULL);
        CREATE TABLE sales.Child (
          Id int IDENTITY(1,1) PRIMARY KEY, TenantId int NULL, ParentId bigint NULL,
          PeerId int NULL, ParentChildId int NULL,
          Calculated AS (Id + 1),
          CONSTRAINT FK_Composite FOREIGN KEY (TenantId,ParentId) REFERENCES dbo.Parent(TenantId,Id),
          CONSTRAINT FK_CrossSchema FOREIGN KEY (PeerId) REFERENCES sales.Parent(Id),
          CONSTRAINT FK_Self FOREIGN KEY (ParentChildId) REFERENCES sales.Child(Id));
        CREATE TABLE dbo.[特殊'表] ([特殊'列] varchar(19) NULL);
        EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'父表中文注释',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'Parent';
        EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'标题中文注释 <script> & 引号',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'Parent',@level2type=N'COLUMN',@level2name=N'Title';
        """);
    }
    var schema = await new SchemaReader().ReadAsync(new("sqlserver",cs.ConnectionString), CancellationToken.None);
    var parent = schema.Tables.Single(t => t.Schema == "dbo" && t.Name == "Parent");
    var cols = parent.Columns.ToDictionary(c => c.Name);
    Check(schema.Name == databaseName && schema.Provider == "sqlserver", "database identity");
    Check(schema.Tables.Count == 4 && schema.Tables.Count(t => t.Name == "Parent") == 2, "schemas with duplicate table names");
    Check(cols["Title"].Length == "80" && cols["Code"].Length == "25", "Unicode and non-Unicode lengths");
    Check(cols["Description"].Length == "max" && cols["Payload"].Length == "max", "nvarchar(max) and varbinary(max)");
    Check(cols["Amount"].Length == "18,4", "decimal precision and scale");
    Check(cols["CreatedAt"].Length == "3" && cols["Clock"].Length == "4" && cols["OffsetAt"].Length == "2", "temporal fractional precision");
    Check(cols["AliasLabel"].Type == "ShortLabel" && cols["AliasLabel"].Length == "35", "alias type retains name and Unicode length");
    Check(cols["AliasAmount"].Type == "Amount" && cols["AliasAmount"].Length == "15,3", "alias decimal precision and scale");
    Check(cols["TenantId"].PrimaryKey && cols["Id"].PrimaryKey && !cols["Title"].PrimaryKey, "composite primary key");
    Check(!cols["Title"].Nullable && cols["Description"].Nullable, "column nullability");
    Check(cols["Title"].DefaultValue?.Contains("默认标题") == true && cols["Amount"].DefaultValue?.Contains('0') == true, "default constraints");
    Check(parent.Comment == "父表中文注释" && cols["Title"].Comment == "标题中文注释 <script> & 引号", "table and column extended properties");
    Check(schema.Tables.Single(t => t.Name == "特殊'表").Columns.Single().Name == "特殊'列", "quoted identifiers");
    Check(schema.Tables.Single(t => t.Schema == "sales" && t.Name == "Parent").Columns.Single(c => c.Name == "Name").Length == "12", "nchar character length");
    Check(schema.Tables.Single(t => t.Name == "Child").Columns.Any(c => c.Name == "Calculated"), "computed column included");
    Check(schema.Relations.Count == 4, "foreign key count including composite members");
    Check(schema.Relations.Any(r => r.FromColumn == "TenantId" && r.ToColumn == "TenantId" && r.ToSchema == "dbo") && schema.Relations.Any(r => r.FromColumn == "ParentId" && r.ToColumn == "Id"), "composite foreign key pairing");
    Check(schema.Relations.Any(r => r.FromSchema == "sales" && r.ToSchema == "sales" && r.FromColumn == "PeerId" && r.ToTable == "Parent"), "schema-qualified foreign key");
    Check(schema.Relations.Any(r => r.FromTable == "Child" && r.ToTable == "Child" && r.FromColumn == "ParentChildId"), "self reference");
    if (Environment.GetEnvironmentVariable("DBVIEWER_URL") is string url) {
        using var http = new HttpClient { BaseAddress = new Uri(url), Timeout = TimeSpan.FromSeconds(40) };
        var response = await http.PostAsJsonAsync("/api/schema", new ConnectionRequest("sqlserver",cs.ConnectionString));
        var api = response.IsSuccessStatusCode ? await response.Content.ReadFromJsonAsync<DatabaseSchema>() : null;
        Check(api?.Tables.Count == schema.Tables.Count && api?.Relations.Count == 4, "HTTP API reads SQL Server metadata");
        Check(api?.Tables.Single(t=>t.Schema=="dbo" && t.Name=="Parent").Columns.Single(c=>c.Name=="AliasLabel").Length == "35", "HTTP API includes alias length fix");
        var invalid = await http.PostAsJsonAsync("/api/schema",new ConnectionRequest("sqlserver","invalid-key=redacted"));
        Check((int)invalid.StatusCode == 400 && !(await invalid.Content.ReadAsStringAsync()).Contains("redacted"), "invalid SQL Server connection sanitized");
    }
    Console.WriteLine($"Result: {failures.Count} failed check(s).");
} catch (Exception e) { Console.Error.WriteLine($"Integration test failed ({e.GetType().Name}); credentials and server details omitted."); return 2; }
finally {
    if (created) {
        cs.InitialCatalog = "master";
        await using var cleanup = new SqlConnection(cs.ConnectionString);
        await cleanup.OpenAsync();
        await Execute(cleanup, $"ALTER DATABASE [{databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{databaseName}]");
        Console.WriteLine("Removed only the temporary DBViewer test database.");
    }
}
return failures.Count == 0 ? 0 : 1;
