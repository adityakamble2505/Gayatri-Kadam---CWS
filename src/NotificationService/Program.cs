using GreenCarWash.NotificationService;
using GreenCarWash.NotificationService.Data;
using GreenCarWash.BuildingBlocks.Auth;
using GreenCarWash.BuildingBlocks.Hosting;
using GreenCarWash.BuildingBlocks.Http;
using GreenCarWash.BuildingBlocks.Logging;

var builder = WebApplication.CreateBuilder(args);

builder.AddGreenCarWashLogging();
builder.Services.AddGreenCarWashApi("NotificationService", typeof(Program).Assembly);
builder.Services.AddJwtAuth(builder.Configuration);
builder.Services.AddInternalAuth(builder.Configuration);
builder.Services.AddInternalServiceClients(builder.Configuration);
builder.Services.AddNotificationData(builder.Configuration);
builder.Services.AddNotificationApplication(builder.Configuration);

var app = builder.Build();

app.UseGreenCarWashPipeline();

app.Run();

public partial class Program;
