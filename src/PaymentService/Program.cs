using GreenCarWash.PaymentService;
using GreenCarWash.PaymentService.Data;
using GreenCarWash.BuildingBlocks.Auth;
using GreenCarWash.BuildingBlocks.Hosting;
using GreenCarWash.BuildingBlocks.Http;
using GreenCarWash.BuildingBlocks.Logging;

var builder = WebApplication.CreateBuilder(args);

builder.AddGreenCarWashLogging();
builder.Services.AddGreenCarWashApi("PaymentService", typeof(Program).Assembly);
builder.Services.AddJwtAuth(builder.Configuration);
builder.Services.AddInternalAuth(builder.Configuration);
builder.Services.AddInternalServiceClients(builder.Configuration);
builder.Services.AddPaymentData(builder.Configuration);
builder.Services.AddPaymentApplication(builder.Configuration);

var app = builder.Build();

app.UseGreenCarWashPipeline();

app.Run();

public partial class Program;
