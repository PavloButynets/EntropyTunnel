using System.Text.Json.Serialization;
using EntropyTunnel.Server;
using EntropyTunnel.Server.Endpoints;
using EntropyTunnel.Server.Handlers;
using EntropyTunnel.Server.Sse;
using EntropyTunnel.Server.State;

var builder = WebApplication.CreateBuilder(args);

var allowedOrigins = builder.Configuration.GetSection("AllowedOrigins").Get<string[]>();

builder.Services.AddSingleton<AgentStateStore>();
builder.Services.AddSingleton<SseConnectionManager>();
builder.Services.AddSingleton<TunnelHub>();

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

app.UseWebSockets();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();

app.MapTunnel();
app.MapAgentApi();
app.MapHttpProxy();

app.Run();
