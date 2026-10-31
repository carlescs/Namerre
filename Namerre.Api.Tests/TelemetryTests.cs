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

    [Fact]
    public void ConfiguredEndpointCollectsTheRenameBacklogGauges()
    {
        using var factory = WithTelemetry("http://otel-collector:4317", "Grpc");

        Assert.NotNull(factory.Services.GetService<MeterProvider>());
        Assert.NotNull(factory.Services.GetRequiredService<Namerre.Infrastructure.Diagnostics.RenameStateMetrics>());
    }

    [Fact]
    public void LibraryRequestsAreLeftOutOfHttpTraces()
    {
        using var traced = new HttpRequestMessage(HttpMethod.Get, "https://api.themoviedb.org/3/movie/1");
        using var untraced = new HttpRequestMessage(HttpMethod.Post, "https://hooks.example/secret");
        untraced.Options.Set(Namerre.Infrastructure.Diagnostics.NamerreTelemetry.Untraced, true);

        Assert.True(Namerre.Telemetry.TelemetrySetup.IsTraced(traced));
        Assert.False(Namerre.Telemetry.TelemetrySetup.IsTraced(untraced));
    }

    [Fact]
    public void DatabaseCommandsAreTracedOnlyInsideAnotherSpan()
    {
        var previous = System.Diagnostics.Activity.Current;
        try
        {
            System.Diagnostics.Activity.Current = null;
            Assert.False(Namerre.Telemetry.TelemetrySetup.HasParent());
            using var source = new System.Diagnostics.ActivitySource("OpenTelemetry.Instrumentation.EntityFrameworkCore");
            using var listener = new System.Diagnostics.ActivityListener
            {
                ShouldListenTo = candidate => candidate == source,
                Sample = (ref System.Diagnostics.ActivityCreationOptions<System.Diagnostics.ActivityContext> _)
                    => System.Diagnostics.ActivitySamplingResult.AllData
            };
            System.Diagnostics.ActivitySource.AddActivityListener(listener);
            using (source.StartActivity("poll"))
                Assert.False(Namerre.Telemetry.TelemetrySetup.HasParent());
            using var job = new System.Diagnostics.Activity("job").Start();
            using (source.StartActivity("command"))
                Assert.True(Namerre.Telemetry.TelemetrySetup.HasParent());
        }
        finally { System.Diagnostics.Activity.Current = previous; }
    }

    private static Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> WithTelemetry(
        string endpoint, string protocol)
        // UseSetting is applied before Program reads builder.Configuration; app configuration is not.
        => new AuthWebApplicationFactory().WithWebHostBuilder(builder => builder
            .UseSetting("Namerre:Telemetry:OtlpEndpoint", endpoint)
            .UseSetting("Namerre:Telemetry:Protocol", protocol));
}
