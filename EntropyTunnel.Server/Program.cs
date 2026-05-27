using System.Text.Json.Serialization;
using EntropyTunnel.Server;
using EntropyTunnel.Server.Data;
using EntropyTunnel.Server.Endpoints;
using EntropyTunnel.Server.Handlers;
using EntropyTunnel.Server.Services;
using EntropyTunnel.Server.Sse;
using EntropyTunnel.Server.State;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

var allowedOrigins = builder.Configuration.GetSection("AllowedOrigins").Get<string[]>();

builder.Services.AddDbContextFactory<AppDbContext>(opts =>
    opts.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection"))
        .UseSnakeCaseNamingConvention());

builder.Services.AddSingleton<AgentStateStore>();
builder.Services.AddSingleton<SseConnectionManager>();
builder.Services.AddSingleton<TunnelHub>();
builder.Services.AddHostedService<LogBatchWriter>();
builder.Services.AddHostedService<LogRetentionJob>();

builder.Services.AddCors(opts =>
    opts.AddDefaultPolicy(p =>
        p.WithOrigins(allowedOrigins)
         .AllowAnyMethod()
         .AllowAnyHeader()));

builder.Services.ConfigureHttpJsonOptions(opts =>
{
    opts.SerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
    opts.SerializerOptions.Converters.Add(new JsonStringEnumConverter(namingPolicy: null, allowIntegerValues: true));
});

builder.Services.AddAuthentication("Cookies")
    .AddCookie("Cookies", opts =>
    {
        opts.Cookie.HttpOnly = true;
        opts.Cookie.SameSite = SameSiteMode.Strict;
        opts.Events.OnRedirectToLogin = ctx => { ctx.Response.StatusCode = 401; return Task.CompletedTask; };
        opts.Events.OnRedirectToAccessDenied = ctx => { ctx.Response.StatusCode = 403; return Task.CompletedTask; };
    });
builder.Services.AddAuthorization();

var app = builder.Build();

// Apply pending migrations on startup
await using (var scope = app.Services.CreateAsyncScope())
    await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();

app.UseWebSockets();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();

app.MapTunnel();
app.MapAgentApi();
app.MapHttpProxy();

app.Run();
