using System.Text.Json;
using Microsoft.Data.Sqlite;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls("http://127.0.0.1:6000");
builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(o => o.SingleLine = true);
var app = builder.Build();

string contentDir = Path.Combine(app.Environment.ContentRootPath, "content");
string motdPath = Path.Combine(contentDir, "motd.txt");
string dataDir = Path.Combine(app.Environment.ContentRootPath, "data");
Directory.CreateDirectory(dataDir);
string connectionString = "Data Source=" + Path.Combine(dataDir, "bassment.db");

using (SqliteConnection connection = new SqliteConnection(connectionString))
{
    connection.Open();
    using (SqliteCommand command = connection.CreateCommand())
    {
        command.CommandText = @"
        CREATE TABLE IF NOT EXISTS players (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            Platform INTEGER NOT NULL,
            PlatformId INTEGER NOT NULL,
            Name TEXT NOT NULL,
            XP INTEGER NOT NULL DEFAULT 0,
            Level INTEGER NOT NULL DEFAULT 1,
            Reputation INTEGER NOT NULL DEFAULT 0,
            UNIQUE (Platform, PlatformId)
        );
        CREATE TABLE IF NOT EXISTS images (
            PlayerId INTEGER PRIMARY KEY,
            Data BLOB NOT NULL
        );";
        command.ExecuteNonQuery();
    }
}

string LoadMotd()
{
    try
    {
        if (File.Exists(motdPath)) {
            return File.ReadAllText(motdPath);
        }
    }
    catch { }
    return "Yikes, the server couldn't find a MOTD, and if it can't find a MOTD then you're probally BONED\n";
}

const string PlayerColumns = "Id, Platform, PlatformId, Name, XP, Level, Reputation";

Dictionary<string, object> ReadPlayer(SqliteDataReader reader)
{
    return new Dictionary<string, object>
    {
        ["Id"] = reader.GetInt64(0),
        ["Platform"] = reader.GetInt32(1),
        ["PlatformId"] = reader.GetInt64(2),
        ["Name"] = reader.GetString(3),
        ["XP"] = reader.GetInt32(4),
        ["Level"] = reader.GetInt32(5),
        ["Reputation"] = reader.GetInt32(6)
    };
}

void Execute(string sql, Action<SqliteParameterCollection> addParams)
{
    using (SqliteConnection connection = new SqliteConnection(connectionString))
    {
        connection.Open();
        using (SqliteCommand command = connection.CreateCommand())
        {
            command.CommandText = sql;
            addParams(command.Parameters);
            command.ExecuteNonQuery();
        }
    }
}

T Query<T>(string sql, Func<SqliteDataReader, T> read, Action<SqliteParameterCollection> addParams)
{
    using (SqliteConnection connection = new SqliteConnection(connectionString))
    {
        connection.Open();
        using (SqliteCommand command = connection.CreateCommand())
        {
            command.CommandText = sql;
            addParams(command.Parameters);
            using (SqliteDataReader reader = command.ExecuteReader())
            {
                if (reader.Read())
                {
                    return read(reader);
                }
            }
        }
    }
    return default(T);
}

Dictionary<string, object> FindPlayerById(long id)
{
    return Query("SELECT " + PlayerColumns + " FROM players WHERE Id = $id", ReadPlayer, p => p.AddWithValue("$id", id)) ?? new Dictionary<string, object>();
}

Dictionary<string, object> FindPlayerByPlatform(int platform, long platformId)
{
    return Query("SELECT " + PlayerColumns + " FROM players WHERE Platform = $p AND PlatformId = $pid", ReadPlayer, p =>
    {
        p.AddWithValue("$p", platform);
        p.AddWithValue("$pid", platformId);
    }) ?? new Dictionary<string, object>();
}

app.MapGet("/motd", (HttpContext ctx) =>
{
    ctx.Response.Headers.CacheControl = "no-cache";
    return Results.Text(LoadMotd(), "text/plain; charset=utf-8");
});

app.MapGet("/api/config/v1/motd", (HttpContext ctx) =>
{
    ctx.Response.Headers.CacheControl = "no-cache";
    return Results.Text(LoadMotd(), "text/plain; charset=utf-8");
});

app.MapGet("/api/players/v1/{id:long?}", (long? id, HttpRequest req) =>
{
    if (id.HasValue)
    {
        return Results.Json(FindPlayerById(id.Value));
    }
    int platform = int.TryParse(req.Query["p"], out var pl) ? pl : 0;
    long platformId = long.TryParse(req.Query["id"], out var pid) ? pid : 0;
    return Results.Json(FindPlayerByPlatform(platform, platformId));
});

app.MapPost("/api/players/v1/create", async (HttpRequest req) =>
{
    var form = await req.ReadFormAsync();
    int platform = int.TryParse(form["Platform"], out var pl) ? pl : 0;
    long platformId = long.TryParse(form["PlatformId"], out var pid) ? pid : 0;
    string name = form["Name"].ToString();
    if (string.IsNullOrEmpty(name))
    {
        name = "IPhone 4 running IOS 6";
    }
    Execute("INSERT OR IGNORE INTO players (Platform, PlatformId, Name) VALUES ($p, $pid, $name)", p =>
    {
        p.AddWithValue("$p", platform);
        p.AddWithValue("$pid", platformId);
        p.AddWithValue("$name", name);
    });
    return Results.Json(FindPlayerByPlatform(platform, platformId));
});

app.MapPost("/api/players/v1/update/{id:long}", async (long id, HttpRequest req) =>
{
    Dictionary<string, object> profile = FindPlayerById(id);
    if (profile.Count == 0)
    {
        return Results.Json(new Dictionary<string, object>());
    }
    string body;
    using (var reader = new StreamReader(req.Body))
    {
        body = await reader.ReadToEndAsync();
    }
    try
    {
        var patch = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(body);
        if (patch != null)
        {
            if (patch.TryGetValue("Name", out var name) && name.ValueKind == JsonValueKind.String)
            {
                profile["Name"] = name.GetString() ?? "";
            }
            foreach (string key in new string[] { "XP", "Level", "Reputation" })
            {
                if (patch.TryGetValue(key, out var number) && number.ValueKind == JsonValueKind.Number)
                {
                    profile[key] = number.GetInt32();
                }
            }
        }
    }
    catch { }
    Execute("UPDATE players SET Name = $name, XP = $xp, Level = $level, Reputation = $rep WHERE Id = $id", p =>
    {
        p.AddWithValue("$name", profile["Name"]);
        p.AddWithValue("$xp", profile["XP"]);
        p.AddWithValue("$level", profile["Level"]);
        p.AddWithValue("$rep", profile["Reputation"]);
        p.AddWithValue("$id", id);
    });
    return Results.Json(profile);
});

app.MapGet("/api/images/v1/profile/{id:long}", (long id) =>
{
    byte[] image = Query("SELECT Data FROM images WHERE PlayerId = $id", r => (byte[])r["Data"], p => p.AddWithValue("$id", id)) ?? Array.Empty<byte>();
    return Results.Bytes(image, "application/octet-stream");
});

app.MapPost("/api/images/v1/profile/{id:long}", async (long id, HttpRequest req) =>
{
    var form = await req.ReadFormAsync();
    if (form.Files.Count > 0)
    {
        byte[] data;
        using (var ms = new MemoryStream())
        {
            await form.Files[0].CopyToAsync(ms);
            data = ms.ToArray();
        }
        Execute("INSERT OR REPLACE INTO images (PlayerId, Data) VALUES ($id, $data)", p =>
        {
            p.AddWithValue("$id", id);
            p.AddWithValue("$data", data);
        });
    }
    return Results.Ok();
});

app.Run();
