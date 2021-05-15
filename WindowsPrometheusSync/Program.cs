using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

[assembly: InternalsVisibleTo("WindowsPrometheusSync.Test")]

namespace WindowsPrometheusSync
{ 
    public class Program
    {
        public static async Task Main(string[] args)
        {
            try
            {
                await CreateHostBuilder(args).RunConsoleAsync(consoleOptions => { });
            }
            catch (Exception ex)
            {
                var test = ex;
                throw;
            }
        }

        internal static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .ConfigureLogging(loggingBuilder =>
                {
                    loggingBuilder.ClearProviders();
                    loggingBuilder.AddConsole().SetMinimumLevel(LogLevel.Debug);
                })
                .ConfigureServices((context, services) =>
                {
                    services.AddSingleton<ReadinessHealthCheck>();
                    services.AddSingleton<LivenessHealthCheck>();
                    
                    services.AddSingleton<IKubernetesClientFactory, KubernetesClientFactory>();
                    services.AddSingleton<IKubernetesClientWrapper, KubernetesClientWrapper>();
                    services.AddSingleton<SyncService>();
                    services.AddHostedService<SyncService>();

                    services.AddHealthChecks()
                        .AddCheck<LivenessHealthCheck>("Liveness", HealthStatus.Degraded, new List<string>(){"Liveness"})
                        .AddCheck<ReadinessHealthCheck>("Readiness", HealthStatus.Degraded, new List<string>{ "Readiness" });
                    
                });
    }

    internal class LivenessHealthCheck : IHealthCheck
    {
        public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default(CancellationToken))
        {
            // Some Liveness check
            Console.WriteLine("LivenessHealthCheck executed.");
            return Task.FromResult(HealthCheckResult.Healthy());
        }
    }

    internal class ReadinessHealthCheck : IHealthCheck
    {
        public bool StartupTaskCompleted { get; set; } = false;
        public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default(CancellationToken))
        {
            // Some Readiness check
            Console.WriteLine("Readiness health check executed.");
            if (StartupTaskCompleted)
            {
                return Task.FromResult(
                    HealthCheckResult.Healthy("The startup task is finished."));
            }
            return Task.FromResult(
                HealthCheckResult.Unhealthy("The startup task is still running."));
        }
    }
}
