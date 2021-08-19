using System;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using k8s.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace WindowsPrometheusSync
{
    /// <summary>
    /// Keeps a prometheus scrape job in sync with the windows nodes to get the windows node exporter metrics pulled into prometheus
    /// </summary>
    internal class SyncService : IHostedService, IDisposable
    {
        // Labels names in the static_configs can only contain alphanumeric or underscore characters, we'll replace the others with underscore which is what prom does by default
        private static readonly Regex InvalidLabelCharacters = new(@"[^\w_]", RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        private static readonly TimeSpan PollDelay = TimeSpan.FromMinutes(1);

        private const string SecretDataPropertyName = "additional-scrape-configs.yaml";

        private bool _disposed;
        private CancellationTokenSource _cts;
        private Task _syncTask = Task.CompletedTask;

        private readonly IKubernetesClientWrapper _kubernetesClientWrapper;
        private readonly ILogger<SyncService> _logger;
        private readonly IHost _host;

        /// <summary>
        /// CTOR
        /// </summary>
        public SyncService(IKubernetesClientWrapper kubernetesClientWrapper, ILogger<SyncService> logger, IHost host)
        {
            _kubernetesClientWrapper = kubernetesClientWrapper;
            _logger = logger;
            _host = host;
        }

        /// <summary>
        /// Starts the service which will continue to poll for changes and update as necessary
        /// </summary>
        public async Task StartAsync(CancellationToken cancellationToken)
        {
            // Make sure we stop any previous work first
            _cts?.Cancel();
            await _syncTask;
            _cts?.Dispose();

            _cts = new CancellationTokenSource();
            _syncTask = LoopSyncAsync(_cts.Token);
        }

        /// <summary>
        /// Stops the service
        /// </summary>
        public Task StopAsync(CancellationToken cancellationToken)
        {
            _cts?.Cancel();
            return _syncTask;
        }

        /// <summary>
        /// Dispose
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        // Protected implementation of Dispose pattern.
        private void Dispose(bool disposing)
        {
            if (_disposed)
                return;

            if (disposing)
            {
                _cts?.Cancel();
                _cts?.Dispose();
            }

            _disposed = true;
        }

        /// <summary>
        /// Polling function
        /// </summary>
        private async Task LoopSyncAsync(CancellationToken cancellationToken)
        {
            var readinessCheck = _host.Services.GetRequiredService<ReadinessHealthCheck>();
            
            do
            {
                try
                {
                    await SyncWithWindowsNodesAsync(cancellationToken);
                    await Task.Delay(PollDelay, cancellationToken);
                    // if the first sync job succeeds without exception, mark the service as ready
                    readinessCheck.ServiceIsReady = true;
                }
                catch (OperationCanceledException)
                {
                    // ignore
                }
                catch (Exception ex)
                {
                    _logger.LogCritical(ex, $"Unexpected exception in {nameof(LoopSyncAsync)}");
                    throw;
                }
            } while (!cancellationToken.IsCancellationRequested);
        }

        /// <summary>
        /// Keeps the scrape job in sync with the windows nodes
        /// </summary>
        private async Task SyncWithWindowsNodesAsync(CancellationToken cancellationToken)
        {
            var windowsNodes = (await _kubernetesClientWrapper.GetWindowsNodesAsync(cancellationToken))
                .Select(CreateNodeInfoFromK8SNode)
                .ToList();
                //.ToDictionary(x => x.Name, x => x);
            _logger.LogDebug($"Discovered windows nodes: {Environment.NewLine + "\t" + string.Join(Environment.NewLine + "\t", windowsNodes)}");

            var prometheusServerConfigMap = await _kubernetesClientWrapper.GetPrometheusScrapeConfigSecretAsync(cancellationToken);
            
            var tracker = new PrometheusConfigChangeTracker(Encoding.UTF8.GetString(prometheusServerConfigMap.Data[SecretDataPropertyName]));

            var windowsNodeScrapeConfigs = tracker.List();

            // Remove configs that no longer have a matching node (including label changes)
            foreach (var node in windowsNodeScrapeConfigs.Where(x => !windowsNodes.Contains(x)).ToList())
            {
                tracker.Remove(node);
                windowsNodeScrapeConfigs.Remove(node);
            }

            // Add missing windows nodes
            foreach (var node in windowsNodes.Where(x => !windowsNodeScrapeConfigs.Contains(x)))
            {
                tracker.Add(node);
            }

            if (tracker.NeedsUpdate)
            {
                prometheusServerConfigMap.Data[SecretDataPropertyName] = Encoding.UTF8.GetBytes(tracker.ToString());
                await _kubernetesClientWrapper
                    .UpdatePrometheusScrapeConfigSecretAsync(prometheusServerConfigMap, cancellationToken);
            }
        }
        
        /// <summary>
        /// Utility for converting a <see cref="V1Node"/> into <see cref="NodeInfo"/>
        /// </summary>
        private NodeInfo CreateNodeInfoFromK8SNode(V1Node node)
        {
            var name = node.Name();
            var labels = node.Labels()
                .ToDictionary(x=> InvalidLabelCharacters.Replace(x.Key, "_"), x=> x.Value);

            return new NodeInfo(name, labels);
        }
    }
}