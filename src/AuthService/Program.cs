using GreenCarWash.AuthService;
using GreenCarWash.AuthService.Data;
using GreenCarWash.AuthService.Services;
using GreenCarWash.BuildingBlocks.Auth;
using GreenCarWash.BuildingBlocks.Hosting;
using GreenCarWash.BuildingBlocks.Http;
using GreenCarWash.BuildingBlocks.Logging;

var builder = WebApplication.CreateBuilder(args);

builder.AddGreenCarWashLogging();
builder.Services.AddGreenCarWashApi("AuthService", typeof(Program).Assembly);
builder.Services.AddJwtAuth(builder.Configuration);
builder.Services.AddInternalAuth(builder.Configuration);
builder.Services.AddInternalServiceClients(builder.Configuration);
builder.Services.AddAuthData(builder.Configuration);
builder.Services.AddAuthApplication();

var app = builder.Build();

await AuthSeeder.SeedAsync(app.Services);

app.UseGreenCarWashPipeline();

app.Run();

public partial class Program;
