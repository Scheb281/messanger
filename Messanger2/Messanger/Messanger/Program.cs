using Messanger.Classes;
using Messanger.Tabels;
using Microsoft.AspNetCore.CookiePolicy;
using Microsoft.AspNetCore.Hosting.Builder;
using Microsoft.AspNetCore.Identity.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Npgsql;
using System.Collections.Concurrent;
using System.Net;
using System.Net.WebSockets;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Web;
using System.Xml.Linq;
using static Microsoft.EntityFrameworkCore.DbLoggerCategory;

var builder = WebApplication.CreateBuilder(args);


var ConnectionString =
    builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Строка подключения 'DefaultConnection' не найдена в конфигурации.");

var databaseConnectionString = CreateConnectionString(ConnectionString);

builder.Services.AddDbContext<Db>(options =>
    options.UseNpgsql(
        databaseConnectionString,
        npgsqlOptions => npgsqlOptions.EnableRetryOnFailure()));

builder.Services.AddRazorPages();

var app = builder.Build();
var sockets = new ConcurrentDictionary<Guid, WebSocket>();
app.MapRazorPages();

User? User = null;
string Nik;

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<Db>();

    await db.Database.EnsureCreatedAsync();
    await CreateTables(db);

}

app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(Path.Combine(builder.Environment.ContentRootPath, "js")),
    RequestPath = "/js"
});
app.UseWebSockets(new WebSocketOptions
{
    KeepAliveInterval = TimeSpan.FromMinutes(5)
});
async Task HandleWebSocketConnection(WebSocket webSocket)
{
    var buffer = new byte[1024 * 4];
    while (webSocket.State == WebSocketState.Open)
    {
        var result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
        if (result.MessageType == WebSocketMessageType.Close) break;

        foreach (var client in sockets.Values.Where(s => s.State == WebSocketState.Open))
        {
            await client.SendAsync(new ArraySegment<byte>(buffer, 0, result.Count), result.MessageType, true, CancellationToken.None);
        }
    }
}

app.MapGet("/", async (HttpContext context, Db db) =>
{
    if (context.Request.Cookies.TryGetValue("Session_id", out string value))
    {
        string[] values = value.Split(':');
        var us = await db.Users.FirstOrDefaultAsync(u => u.Login == values[0] && u.Password == values[1]);
        SetUser(us);
        context.Response.Redirect("/chats");
    }

    string html = File.ReadAllText($"HTML\\MainMenu.html");
    return Results.Content(html, "text/html; charset=utf-8");
});

app.MapPost("/", async (HttpContext context, Db db) => 
{
    DeleteCookie(context);
    context.Response.Redirect("/");

});

app.MapGet("/enter", () => 
{
    string html = File.ReadAllText($"HTML\\Enter.html");
    return Results.Content(html, "text/html; charset=utf-8");
});
app.MapPost("/enter", async (HttpContext context, Db db) => 
{
    string html = "";
    var form = await context.Request.ReadFormAsync();

    string login = form["login"];
    string password = GetHash(form["password"]);

    var user = await db.Users.FirstOrDefaultAsync(u => u.Login == login && u.Password == password);

    if (user is null)
    {
        html = File.ReadAllText($"HTML\\EnterError.html");
        return Results.Content(html, "text/html; charset=utf-8");
    }

    SetUser(user);
    CreateCookie(login, password, context);

    html = File.ReadAllText($"HTML\\EnterS.html");
    return Results.Content(html, "text/html; charset=utf-8");
});

app.MapGet("/reg", () =>
{
    string html = File.ReadAllText($"HTML\\Reg.html");
    return Results.Content(html, "text/html; charset=utf-8");
});
app.MapPost("/reg", async (HttpContext context, Db db) =>
{
    var form = await context.Request.ReadFormAsync();

    string login = form["login"];
    string password = form["password"];
    string nik = form["nik"];

    string html = File.ReadAllText($"HTML\\RegS.html");

    var us = await db.Users.FirstOrDefaultAsync(u => u.Login == login || u.Nik == nik);
    if (us is not null || password.Length < 6)
    {
        html = File.ReadAllText($"HTML\\RegError.html");
        return Results.Content(html, "text/html; charset=utf-8");
    }

    var user = new User
    {
        Login = login,
        Password = GetHash(password),
        Nik = nik
    };

    await db.Users.AddAsync(user);

    db.SaveChanges();

    SetUser(user);
    CreateCookie(user.Login, user.Password, context);
    return Results.Content(html, "text/html; charset=utf-8");
});

app.MapGet("/chats", () =>
{
    int i = 0;
    bool Check = true;
    string line;
    string html = """<html>""";
    using (StreamReader sr = new StreamReader($"HTML\\Chats.html"))
    {
        while ((line = sr.ReadLine()) != null)
        {
            if (i == 278)
            {
                i = 0;
            }
            if (line.Trim() == "<div class=\"username-glow cursor-blink\">UserName</div>")
            {
                line = $"""<div class="username-glow cursor-blink">{User.Nik}</div>""";
            }
            if (line.Trim() == "<div class=\"section-title\">📡 КОНТАКТЫ:</div>")
            {
                if (User.Chats != string.Empty)
                {
                    html += line;
                    string[] lines = User.Chats.Split('\n');
                    foreach (string l in lines)
                    {
                        if (l != "")
                        {
                            if (l[0] == '!')
                            {
                                line = $"""
                                <h2 class="contact-name">{l.Remove(0, 1)}</h2>
                                <div class="contact-actions">
                                    <form method="get" action="/Chat/{l.Remove(0, 1)}">
                                        <button class="matrix-btn" type="submit">💬 Написать</button>
                                    </form>
                                    <form method="post" action="/DelContact/{l.Remove(0, 1)}">
                                        <button class="matrix-btn" type="submit">🗑 Удалить</button>
                                    </form>
                                </div>
                                """;
                                html += line;
                                Check = false;
                            }
                        }
                    }
                }
            }
            if (Check)
            {
                html += line;
                i++;
            }
            Check = true;
        }
    }
    html += """</html>""";

    return Results.Content(html, "text/html; charset=utf-8");
});

app.MapPost("/DelContact/{name}", async (string name, HttpContext context, Db db) =>
{
    bool del = false;
    string NewChat = string.Empty;
    string[] lines = User.Chats.Split('\n');
    var user = await db.Users.FirstOrDefaultAsync(u => u.Login == User.Login && u.Password == User.Password);
    foreach (string l in lines)
    {
        if (l != "")
        {
            if (del && l[0] == '!')
            {
                del = false;
            }
            if (!del)
            {
                if (l[0] == '!')
                {
                    if (l.Remove(0, 1) == name)
                    {
                        del = true;
                    }
                    else
                    {
                        NewChat += l + '\n';
                    }
                }
                else
                {
                    NewChat += l;
                }
            }
        }
    }
    user.Chats = NewChat;
    SetUser(user);
    await db.SaveChangesAsync();
    context.Response.Redirect("/chats");
}); 

app.MapGet("/AddContact", () =>
{
    string html = File.ReadAllText("HTML\\AddContact.html");
    return Results.Content(html, "text/html; charset=utf-8");
});
app.MapPost("/AddContact", async (HttpContext context, Db db) =>
{
    string html = File.ReadAllText("HTML\\AddS.html");

    var form = await context.Request.ReadFormAsync();

    string nik = form["nik"];

    var user = await db.Users.FirstOrDefaultAsync(u => u.Nik == nik);

    if (user is null)
    {
        html = File.ReadAllText("HTML\\AddError.html");
        return Results.Content(html, "text/html; charset=utf-8");
    }

    string[] lines = User.Chats.Split('\n');
    foreach (string l in lines)
    {
        if (l != "")
        {
            if (l[0] == '!')
            {
                if (l.Remove(0,1) == nik)
                {
                    html = File.ReadAllText("HTML\\AddError2.html");
                    return Results.Content(html, "text/html; charset=utf-8");
                }
            }
        }
    }

    user = await db.Users.FirstOrDefaultAsync(u => u.Login == User.Login && u.Password == User.Password);
    user.Chats += $"!{nik}\n";
    SetUser(user);
    await db.SaveChangesAsync();

    return Results.Content(html, "text/html; charset=utf-8");
});

app.MapGet("/Chat/{name}", async (string name, HttpContext context, Db db) => 
{
    Nik = name;
    var html = string.Empty;
    string line, messages = string.Empty;
    string[] chat = User.Chats.Split("\n");

    if (User.Chats != "")
    {
        foreach(string l in chat)
        {
            if (l != "")
            {
                if (l[0] == '!' && l.Remove(0, 1) == name)
                {
                    foreach (string s in chat)
                    {
                        if (s == l)
                        {
                            messages += s + '\n';
                        }
                    }
                    using (StreamReader sr = new StreamReader($"HTML\\Socket.html"))
                    {
                        while ((line = sr.ReadLine()) != null)
                        {
                            if (line.Trim() == "<textarea readonly class=\"placehold form-control m-5 rows-5 h-100 text-white bg-dark\" id=\"all_message\" placeholder=\"Написанные сообщения\"></textarea>")
                            {
                                line = $"<textarea readonly class=\"placehold form-control m-5 rows-5 h-100 text-white bg-dark\" id=\"all_message\" placeholder=\"Написанные сообщения\">{messages}</textarea>";
                            }
                            html += line;
                        }
                    }
                }
            }
        }
    }
    if (messages == string.Empty)
    {
        html = File.ReadAllText($"HTML\\Reg.html");
    }

    return Results.Content(html, "text/html; charset=utf-8");
});

app.Map("/ws", async context =>
{
    if (!context.WebSockets.IsWebSocketRequest)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        await context.Response.WriteAsync("Expected a WebSocket request.");
        return;
    }

    using var webSocket = await context.WebSockets.AcceptWebSocketAsync();
    var id = Guid.NewGuid();
    sockets.TryAdd(id, webSocket);
    await HandleWebSocketConnection(webSocket);
    sockets.TryRemove(id, out _);
});

await app.RunAsync();

static string GetHash(string input)
{
    using var sha = SHA256.Create();
    byte[] bytes = Encoding.UTF8.GetBytes(input);
    byte[] hashBytes = sha.ComputeHash(bytes);

    var sb = new StringBuilder();
    foreach (byte b in hashBytes)
        sb.Append(b.ToString("x2"));

    return sb.ToString();
}
static async Task CreateTables(Db db) 
{
    await db.Database.ExecuteSqlRawAsync("""
        CREATE TABLE IF NOT EXISTS "Users" (
            "Id" integer GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY,
            "Nik" text NOT NULL,
            "Login" text NOT NULL,
            "Password" text NOT NULL,
            "Chats" NVARCHAR(MAX)
        );
        """);
}

static void CreateCookie(string login, string password, HttpContext context)
{
    context.Response.Cookies.Append("Session_id", $"{login}:{password}", new CookieOptions
    {
        Expires = DateTimeOffset.Now.AddDays(30),
        Secure = true,
        HttpOnly = true
    });
}
static void DeleteCookie(HttpContext context)
{
    context.Response.Cookies.Delete("Session_id");
}

static string CreateConnectionString(string ConnectionString)
{
    if (!ConnectionString.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase) &&
        !ConnectionString.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase))
    {
        return ConnectionString;
    }

    var databaseUri = new Uri(ConnectionString);

    var userInfoParts = databaseUri.UserInfo.Split(':', 2, StringSplitOptions.None);

    if (userInfoParts.Length != 2)
    {
        throw new InvalidOperationException("Не удалось разобрать логин и пароль из URI-строки подключения PostgreSQL.");
    }

    var connectionStringBuilder = new NpgsqlConnectionStringBuilder
    {
        Host = databaseUri.Host,

        Port = databaseUri.IsDefaultPort ? 5432 : databaseUri.Port,

        Database = databaseUri.AbsolutePath.Trim('/'),

        Username = Uri.UnescapeDataString(userInfoParts[0]),

        Password = Uri.UnescapeDataString(userInfoParts[1])
    };

    foreach (var queryPart in databaseUri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
    {
        var queryPartPieces = queryPart.Split('=', 2, StringSplitOptions.None);

        var key = Uri.UnescapeDataString(queryPartPieces[0]);

        var value = queryPartPieces.Length > 1
            ? Uri.UnescapeDataString(queryPartPieces[1])
            : string.Empty;

        if (key.Equals("sslmode", StringComparison.OrdinalIgnoreCase))
        {
            connectionStringBuilder["SSL Mode"] = value;

            continue;
        }

        if (key.Equals("channel_binding", StringComparison.OrdinalIgnoreCase))
        {
            connectionStringBuilder["Channel Binding"] = value;

            continue;
        }

        connectionStringBuilder[key] = value;
    }

    return connectionStringBuilder.ConnectionString;
}
void SetUser(User us)
{
    User = us;
}
User RetUser()
{
    return User;
}

void DelUser()
{
    User = null;
}

async Task AddMessage(string m, Db db)
{
    string[] chat = User.Chats.Split("\n");
    bool check = false;
    string chatnew = string.Empty; 

    foreach (string l in chat)
    {
        if (l[0] == '!' && l.Remove(0, 1) == Nik)
        {
            check = true;
        }
        if (l[0] == '!' && check)
        {
            chatnew += m + '\n';
            check = false;
        }
        chatnew += l;
    }

    User.Chats = chatnew;
    var user = await db.Users.FirstOrDefaultAsync(u => u.Nik == User.Nik);
    user.Chats = chatnew;
    await db.SaveChangesAsync();
}



