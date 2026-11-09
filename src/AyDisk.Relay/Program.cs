using AyDisk.Relay;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSignalR(o =>
{
    o.MaximumReceiveMessageSize = 8 * 1024 * 1024; // screen frames can be large
    o.EnableDetailedErrors = builder.Environment.IsDevelopment();
}).AddMessagePackProtocol();

var app = builder.Build();

app.MapGet("/", () => "AyDisk relay is running");
app.MapHub<RelayHub>("/relay");

app.Run();
