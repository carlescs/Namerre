using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using Xunit;

namespace Namerre.Api.Tests;

public sealed class TelemetryTests
{
    [Fact]
    public void TelemetryIsDisabledWithoutAnEndpoint()
    {
        using var factory = new AuthWebApplicationFactory();

        Assert.Null(factory.Services.GetService<TracerProvider>());
        Assert.Null(factory.Services.GetService<MeterProvider>());
    }

    [Theory]
    [InlineData("Grpc", "http://otel-collector:4317")]
    [InlineData("HttpProtobuf", "http://otel-collector:4318")]
    public void ConfiguredEndpointEnablesTracesAndMetrics(string protocol, string endpoint)
    {
        using var factory = WithTelemetry(endpoint, protocol);

        Assert.NotNull(factory.Services.GetService<TracerProvider>());
        Assert.NotNull(factory.Services.GetService<MeterProvider>());
    }

    [Theory]
    [InlineData("otel-collector:4317", "Grpc")]
    [InlineData("ftp://otel-collector", "Grpc")]
    [InlineData("http://otel-collector:4317", "Zipkin")]
    public void InvalidTelemetrySettingsFailStartup(string endpoint, string protocol)
    {
        using var factory = WithTelemetry(endpoint, protocol);

        Assert.Throws<InvalidOperationException>(() => factory.Services);
    }

    private static Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> WithTelemetry(
        string endpoint, string protocol)
        // UseSetting is applied before Program reads builder.Configuration; app configuration is not.
        => new AuthWebApplicationFactory().WithWebHostBuilder(builder => builder
            .UseSetting("Namerre:Telemetry:OtlpEndpoint", endpoint)
            .UseSetting("Namerre:Telemetry:Protocol", protocol));
}
