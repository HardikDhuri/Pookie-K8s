using PookieApi.Options;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;
using Serilog.Settings.Configuration;
using Serilog.Sinks.OpenTelemetry;

namespace PookieApi.Extensions;

public static class ObservabilityRegistration
{
    public static WebApplicationBuilder AddObservability(this WebApplicationBuilder builder)
    {
        var configuration = builder.Configuration;

        ObservabilityOptions observabilityOptions = new();

        configuration
            .GetRequiredSection(nameof(ObservabilityOptions))
            .Bind(observabilityOptions);

        builder.AddSerilog(observabilityOptions);
        builder.Services
            .AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(observabilityOptions.ServiceName))
            .AddMetrics(observabilityOptions)
            .AddTracing(observabilityOptions);

        return builder;
    }
    private static OpenTelemetryBuilder AddTracing(this OpenTelemetryBuilder builder, ObservabilityOptions observabilityOptions)
    {
        if (!observabilityOptions.EnabledTracing) return builder;

        builder.WithTracing(tracing =>
        {
            tracing
                .AddSource(observabilityOptions.ServiceName)
                .SetErrorStatusOnException()
                .SetSampler(new AlwaysOnSampler())
                .AddHttpClientInstrumentation()
                .AddAspNetCoreInstrumentation(options =>
                {
                    options.RecordException = true;
                });

            tracing
                .AddOtlpExporter(_ =>
                {
                    _.Endpoint = observabilityOptions.CollectorUri;
                    _.ExportProcessorType = ExportProcessorType.Batch;
                    _.Protocol = OpenTelemetry.Exporter.OtlpExportProtocol.Grpc;
                });
        });

        return builder;
    }

    private static OpenTelemetryBuilder AddMetrics(this OpenTelemetryBuilder builder, ObservabilityOptions observabilityOptions)
    {
        builder.WithMetrics(metrics =>
        {
            metrics
                .AddMeter(observabilityOptions.ServiceName)
                .AddHttpClientInstrumentation()
                .AddAspNetCoreInstrumentation();

            metrics
                .AddOtlpExporter(_ =>
                {
                    _.Endpoint = observabilityOptions.CollectorUri;
                    _.ExportProcessorType = ExportProcessorType.Batch;
                    _.Protocol = OpenTelemetry.Exporter.OtlpExportProtocol.Grpc;
                });
        });

        return builder;
    }

    private static WebApplicationBuilder AddSerilog(this WebApplicationBuilder builder, ObservabilityOptions observabilityOptions)
    {
        var services = builder.Services;
        var configuration = builder.Configuration;

        services.AddSerilog((sp, serilog) =>
        {
            serilog
                .ReadFrom.Configuration(configuration, new ConfigurationReaderOptions
                {
                    SectionName = $"{nameof(ObservabilityOptions)}:{nameof(Serilog)}"
                })
                .ReadFrom.Services(sp)
                .Enrich.FromLogContext()
                .Enrich.WithProperty("ApplicationName", observabilityOptions.ServiceName)
                .WriteTo.Console();

            serilog
                .WriteTo.OpenTelemetry(c =>
                {
                    c.Endpoint = observabilityOptions.CollectorUrl;
                    c.Protocol = OtlpProtocol.Grpc;
                    c.IncludedData = IncludedData.TraceIdField | IncludedData.SpanIdField | IncludedData.SourceContextAttribute;
                    c.ResourceAttributes = new Dictionary<string, object>
                                                    {
                                                        {"service.name", observabilityOptions.ServiceName},
                                                        {"index", 10},
                                                        {"flag", true},
                                                        {"value", 3.14}
                                                    };
                });
        });

        return builder;
    }
}