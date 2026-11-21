using GreenCarWash.CatalogService;
using GreenCarWash.CatalogService.Data;
using GreenCarWash.CatalogService.Services;
using GreenCarWash.BuildingBlocks.Auth;
using GreenCarWash.BuildingBlocks.Hosting;
using GreenCarWash.BuildingBlocks.Http;
using GreenCarWash.BuildingBlocks.Logging;

var builder = WebApplication.CreateBuilder(args);

builder.AddGreenCarWashLogging();
builder.Services.AddGreenCarWashApi("CatalogService", typeof(Program).Assembly);
builder.Services.AddJwtAuth(builder.Configuration);
builder.Services.AddInternalAuth(builder.Configuration);
builder.Services.AddInternalServiceClients(builder.Configuration);
builder.Services.AddCatalogData(builder.Configuration);
builder.Services.AddCatalogApplication();

var app = builder.Build();

await CatalogSeeder.SeedAsync(app.Services);

app.UseGreenCarWashPipeline();

app.Run();

public partial class Program;
