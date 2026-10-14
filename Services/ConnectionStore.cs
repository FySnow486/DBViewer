using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
namespace DBViewer.Services;
public record ConnectionProfile(string? Id, string Name, string Provider, string ConnectionString);
public record StoredConnection(string Id, string Name, string Provider, string ProtectedConnectionString);
public record ConnectionState(string? ActiveId, List<StoredConnection> Items);
public sealed class ConnectionStore
{
    readonly IDataProtector protector;
    readonly string path;
    readonly object gate = new();
    public ConnectionStore(IDataProtectionProvider protection, IConfiguration configuration, IWebHostEnvironment environment)
    {
        protector = protection.CreateProtector("DBViewer.ConnectionProfiles.v1");
        path = Path.Combine(configuration["DBVIEWER_DATA_DIR"] ?? Path.Combine(environment.ContentRootPath,"Data"),"connections.json");
    }
    ConnectionState Read() => File.Exists(path) ? JsonSerializer.Deserialize<ConnectionState>(File.ReadAllText(path)) ?? new(null,[]) : new(null,[]);
    void Write(ConnectionState state)
    {
        var temp=path+".tmp";
        File.WriteAllText(temp,JsonSerializer.Serialize(state));
        if(!OperatingSystem.IsWindows()) File.SetUnixFileMode(temp,UnixFileMode.UserRead|UnixFileMode.UserWrite);
        File.Move(temp,path,true);
    }
    public object List() { lock(gate){var state=Read();return new {state.ActiveId, Items=state.Items.Select(c=>new {c.Id,c.Name,c.Provider}).ToList()};} }
    public ConnectionProfile? Get(string id) { lock(gate){var c=Read().Items.Find(c=>c.Id==id);return c==null?null:new(c.Id,c.Name,c.Provider,protector.Unprotect(c.ProtectedConnectionString));} }
    public string Save(ConnectionProfile profile)
    {
        if(string.IsNullOrWhiteSpace(profile.Name)||profile.Name.Length>80||string.IsNullOrWhiteSpace(profile.ConnectionString)||profile.ConnectionString.Length>16000||!new[]{"postgresql","mysql","sqlite","sqlserver"}.Contains(profile.Provider)) throw new ArgumentException();
        lock(gate){
            var state=Read();var id=profile.Id;
            if(id!=null&&!state.Items.Any(c=>c.Id==id))throw new ArgumentException();
            id ??= Guid.NewGuid().ToString("N");
            state.Items.RemoveAll(c=>c.Id==id);
            state.Items.Add(new(id,profile.Name.Trim(),profile.Provider,protector.Protect(profile.ConnectionString)));
            Write(state with {ActiveId=id});return id;
        }
    }
    public bool Delete(string id) {lock(gate){var state=Read();if(state.Items.RemoveAll(c=>c.Id==id)==0)return false;Write(state with {ActiveId=state.ActiveId==id?null:state.ActiveId});return true;}}
    public bool Activate(string id) {lock(gate){var state=Read();if(!state.Items.Any(c=>c.Id==id))return false;Write(state with {ActiveId=id});return true;}}
}
