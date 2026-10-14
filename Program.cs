using Microsoft.AspNetCore.DataProtection;
using DBViewer.Models;
using DBViewer.Services;
var root=File.Exists(Path.Combine(AppContext.BaseDirectory,"wwwroot","index.html"))?AppContext.BaseDirectory:Directory.GetCurrentDirectory();
var builder = WebApplication.CreateBuilder(new WebApplicationOptions {Args=args,ContentRootPath=root});
builder.Services.AddSingleton<SchemaReader>();
var dataDirectory=builder.Configuration["DBVIEWER_DATA_DIR"] ?? Path.Combine(builder.Environment.ContentRootPath,"Data");
Directory.CreateDirectory(dataDirectory);
if(!OperatingSystem.IsWindows()) File.SetUnixFileMode(dataDirectory,UnixFileMode.UserRead|UnixFileMode.UserWrite|UnixFileMode.UserExecute);
builder.Services.AddDataProtection().SetApplicationName("DBViewer").PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(dataDirectory,"keys")));
builder.Services.AddSingleton<ConnectionStore>();
var app = builder.Build();
app.Use(async (context, next) => {
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    if (context.Request.Path.StartsWithSegments("/api")) {
        context.Response.Headers.CacheControl = "no-store";
        if (context.Request.Headers.Origin.FirstOrDefault() is string origin &&
            (!Uri.TryCreate(origin, UriKind.Absolute, out var uri) || uri.Authority != context.Request.Host.Value)) {
            context.Response.StatusCode = 403; return;
        }
    }
    await next();
});
app.UseDefaultFiles();
app.UseStaticFiles(new StaticFileOptions { OnPrepareResponse = context => context.Context.Response.Headers.CacheControl="no-cache" });
var startedAt=DateTimeOffset.UtcNow;
app.MapGet("/api/health",()=>Results.Ok(new {status="online",startedAt,version="1.0"}));
app.Lifetime.ApplicationStarted.Register(()=>{
    if(OperatingSystem.IsWindows()&&!args.Contains("--no-startup-dialog")){
        _=Task.Run(()=>{
            var localUrl=app.Urls.FirstOrDefault()??"http://localhost:5080";
            localUrl=localUrl.Replace("0.0.0.0","localhost").Replace("[::]","localhost");
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(localUrl) {UseShellExecute=true}); }
            catch { /* The status dialog still provides the address if no browser is configured. */ }
            DBViewer.Services.WindowsStatus.Show("DBViewer 服务已在线\n\n访问地址："+localUrl+"\n\n关闭此提示框后服务继续运行；关闭控制台窗口可停止服务。", "DBViewer · 服务在线");
        });
    }
});
app.MapGet("/api/connections", (ConnectionStore store) => Results.Ok(store.List()));
app.MapGet("/api/connections/{id}", (string id, ConnectionStore store) => {
    try { var profile=store.Get(id);return profile==null?Results.NotFound():Results.Ok(profile); }
    catch(System.Security.Cryptography.CryptographicException){return Results.Problem("无法解密保存的连接，请检查本机密钥目录是否完整。",statusCode:409);}
});
app.MapPost("/api/connections", (ConnectionProfile profile, ConnectionStore store) => {
    try{return Results.Ok(new {id=store.Save(profile)});}catch(ArgumentException){return Results.BadRequest(new {error="连接配置无效，请检查名称和数据库类型。"});}
});
app.MapPost("/api/connections/{id}/activate", (string id,ConnectionStore store)=>store.Activate(id)?Results.NoContent():Results.NotFound());
app.MapDelete("/api/connections/{id}",(string id,ConnectionStore store)=>store.Delete(id)?Results.NoContent():Results.NotFound());
app.MapGet("/api/demo", () => DemoSchema.Create());
app.MapPost("/api/export/excel", (DatabaseSchema schema) => {
    try { return Results.File(ExcelExporter.Create(schema), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "database-schema.xlsx"); }
    catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
});
app.MapPost("/api/schema", async (ConnectionRequest request, SchemaReader reader, CancellationToken ct) => {
    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
    timeout.CancelAfter(TimeSpan.FromSeconds(30));
    try { return Results.Ok(await reader.ReadAsync(request, timeout.Token)); }
    catch (Microsoft.Data.SqlClient.SqlException ex) { return Results.BadRequest(new { error = SqlServerErrors.Describe(ex) }); }
    catch (ArgumentException) { return Results.BadRequest(new { error = "数据库类型或连接字符串无效，请检查配置。" }); }
    catch (OperationCanceledException) { return Results.Json(new { error = "连接超时或请求已取消，请检查服务器地址与网络。" }, statusCode: 408); }
    catch (Exception ex) when (ex is System.Data.Common.DbException || ex is InvalidOperationException) {
        return Results.Json(new { error = "无法读取数据库结构，请检查连接信息、TLS 配置及元数据读取权限。" }, statusCode: 400);
    }
});
app.Run();
