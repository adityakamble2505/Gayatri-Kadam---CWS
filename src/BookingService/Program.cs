using GreenCarWash.BookingService;
using GreenCarWash.BookingService.Data;
using GreenCarWash.BuildingBlocks.Auth;
using GreenCarWash.BuildingBlocks.Hosting;
using GreenCarWash.BuildingBlocks.Http;
using GreenCarWash.BuildingBlocks.Logging;
using Microsoft.Extensions.FileProviders;

var builder = WebApplication.CreateBuilder(args);

builder.AddGreenCarWashLogging();
builder.Services.AddGreenCarWashApi("BookingService", typeof(Program).Assembly);
builder.Services.AddJwtAuth(builder.Configuration);
builder.Services.AddInternalAuth(builder.Configuration);
builder.Services.AddInternalServiceClients(builder.Configuration);
builder.Services.AddBookingData(builder.Configuration);
builder.Services.AddBookingApplication(builder.Configuration);

var app = builder.Build();

var uploadsRoot = Path.Combine(app.Environment.ContentRootPath, "uploads");
Directory.CreateDirectory(Path.Combine(uploadsRoot, "bookings"));
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(uploadsRoot),
    RequestPath = "/uploads"
});

app.UseGreenCarWashPipeline();

app.Run();

public partial class Program;
