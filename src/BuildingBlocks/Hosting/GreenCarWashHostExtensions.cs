using System.Reflection;
using FluentValidation;
using GreenCarWash.BuildingBlocks.Data;
using GreenCarWash.BuildingBlocks.Middleware;
using GreenCarWash.BuildingBlocks.Time;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.OpenApi.Models;
using Serilog;
using Swashbuckle.AspNetCore.Swagger;
using Swashbuckle.AspNetCore.SwaggerUI;

namespace GreenCarWash.BuildingBlocks.Hosting;

public static class GreenCarWashHostExtensions
{
    public static IServiceCollection AddGreenCarWashApi(
        this IServiceCollection services,
        string serviceName,
        Assembly applicationAssembly)
    {
        services.AddControllers()
            .AddJsonOptions(options =>
            {
                options.JsonSerializerOptions.Converters.Add(new UtcDateTimeJsonConverter());
                options.JsonSerializerOptions.Converters.Add(new NullableUtcDateTimeJsonConverter());
            });
        services.AddEndpointsApiExplorer();
        services.AddProblemDetails(options =>
        {
            options.CustomizeProblemDetails = context =>
            {
                context.ProblemDetails.Extensions.Remove("exception");
                context.ProblemDetails.Extensions.Remove("stackTrace");
            };
        });
        services.AddHealthChecks();
        services.AddHttpContextAccessor();
        services.AddSingleton<IClock, SystemClock>();
        services.AddValidatorsFromAssembly(applicationAssembly, includeInternalTypes: true);

        services.AddSwaggerGen(options =>
        {
            options.SwaggerDoc("v1", new OpenApiInfo
            {
                Title = serviceName,
                Version = "v1"
            });
            options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
            {
                Name = "Authorization",
                Description = "JWT Authorization header using the Bearer scheme.",
                In = ParameterLocation.Header,
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT"
            });
            options.AddSecurityRequirement(new OpenApiSecurityRequirement
            {
                {
                    new OpenApiSecurityScheme
                    {
                        Reference = new OpenApiReference
                        {
                            Type = ReferenceType.SecurityScheme,
                            Id = "Bearer"
                        }
                    },
                    Array.Empty<string>()
                }
            });
        });

        return services;
    }

    public static WebApplication UseGreenCarWashPipeline(this WebApplication app)
    {
        app.UseMiddleware<CorrelationIdMiddleware>();
        app.UseSerilogRequestLogging();
        app.UseMiddleware<GlobalExceptionMiddleware>();

        if (app.Environment.IsDevelopment())
        {
            app.UseSwagger();
            app.UseSwaggerUI(options =>
            {
                options.SwaggerEndpoint("/swagger/v1/swagger.json", "v1");
            });
        }

        app.UseAuthentication();
        app.UseAuthorization();
        app.MapHealthChecks("/health");
        app.MapControllers();
        return app;
    }
}
