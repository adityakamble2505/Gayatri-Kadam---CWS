using GreenCarWash.WasherService;
using GreenCarWash.WasherService.Data;
using GreenCarWash.BuildingBlocks.Auth;
using GreenCarWash.BuildingBlocks.Hosting;
using GreenCarWash.BuildingBlocks.Http;
using GreenCarWash.BuildingBlocks.Logging;
using Microsoft.Extensions.FileProviders;

var builder = WebApplication.CreateBuilder(args);

builder.AddGreenCarWashLogging();
builder.Services.AddGreenCarWashApi("WasherService", typeof(Program).Assembly);
builder.Services.AddJwtAuth(builder.Configuration);
builder.Services.AddInternalAuth(builder.Configuration);
builder.Services.AddInternalServiceClients(builder.Configuration);
builder.Services.AddWasherData(builder.Configuration);
builder.Services.AddWasherApplication(builder.Configuration);

var app = builder.Build();

var uploadsRoot = Path.Combine(app.Environment.ContentRootPath, "uploads");
Directory.CreateDirectory(Path.Combine(uploadsRoot, "washers"));
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(uploadsRoot),
    RequestPath = "/uploads"
});

app.UseGreenCarWashPipeline();

app.Run();

public partial class Program;
