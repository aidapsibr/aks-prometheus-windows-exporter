using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using k8s;
using k8s.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Rest;

namespace WindowsPrometheusSync
{
    /// <summary>
    ///     Wraps up the I/O needed for the sync process
    /// </summary>
    public interface IKubernetesClientWrapper
    {
        /// <summary>
        ///     Get some basic info on all windows nodes currently in the cluster
        /// </summary>
        Task<IList<V1Node>> GetWindowsNodesAsync(CancellationToken cancellationToken);

        /// <summary>
        ///     Gets the prometheus scrape config secret so we can maintain the list of scrape jobs for each windows node
        /// </summary>
        Task<V1Secret> GetPrometheusScrapeConfigSecretAsync(CancellationToken cancellationToken);

        /// <summary>
        ///     Updates the secret
        /// </summary>
        Task<V1Secret> UpdatePrometheusScrapeConfigSecretAsync(V1Secret secret,
            CancellationToken cancellationToken);
    }

    public class KubernetesClientWrapper : IKubernetesClientWrapper, IDisposable
    {
        private static string _secretNamespace;
        private static string _secretName;

        private readonly IKubernetesClientFactory _kubernetesClientFactory;
        private readonly ILogger<KubernetesClientWrapper> _logger;
        private Kubernetes _client;
        private bool _disposed;
        private bool _initialized;

        public KubernetesClientWrapper(IKubernetesClientFactory kubernetesClientFactory,
            ILogger<KubernetesClientWrapper> logger, IConfiguration configuration)
        {
            _kubernetesClientFactory = kubernetesClientFactory;
            _logger = logger;
            _secretName = configuration.GetValue<string>("SCRAPE_CONFIG_SECRET_NAME");
            _secretNamespace = configuration.GetValue<string>("MONITORING_NAMESPACE");
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        public async Task<IList<V1Node>> GetWindowsNodesAsync(CancellationToken cancellationToken)
        {
            Init();

            var result = await _client.ListNodeAsync(labelSelector: "kubernetes.io/os=windows",
                cancellationToken: cancellationToken);

            var windowsNodes = result
                .Items
                .Where(x => x.Status.Addresses.Any(y =>
                    y.Type == "InternalIP"
                    && !string.IsNullOrWhiteSpace(y.Address)))
                .ToList();

            return windowsNodes;
        }

        public async Task<V1Secret> GetPrometheusScrapeConfigSecretAsync(CancellationToken cancellationToken)
        {
            Init();

            V1Secret result = null;

            try
            {
                result = await _client.ReadNamespacedSecretAsync(_secretName, _secretNamespace,
                    cancellationToken: cancellationToken);
            }
            catch (HttpOperationException ex)
                when (ex.Response.StatusCode == HttpStatusCode.NotFound)
            {
                try
                {
                    // Check for prometheus server config in the default namespace as a failover
                    result = await _client.ReadNamespacedSecretAsync(_secretName, "default",
                        cancellationToken: cancellationToken);
                }
                catch (HttpOperationException ex2)
                    when (ex.Response.StatusCode == HttpStatusCode.NotFound)
                {
                    _logger.LogWarning(ex2, "Unable to access scrape config secret");
                }
            }
            catch (HttpOperationException ex)
                when (ex.Response.StatusCode == HttpStatusCode.Forbidden)
            {
                _logger.LogCritical(ex, @$"
------
Additional Info
------

Headers:
{ex.Response.Headers}

ReasonPhrase:
{ex.Response.ReasonPhrase}

StatusCode:
{ex.Response.StatusCode}

Content:
{ex.Response.Content}
");
                throw;
            }

            return result;
        }

        public Task<V1Secret> UpdatePrometheusScrapeConfigSecretAsync(V1Secret secret,
            CancellationToken cancellationToken)
        {
            Init();

            return _client.ReplaceNamespacedSecretAsync(
                secret,
                secret.Name(),
                secret.Namespace(),
                cancellationToken: cancellationToken);
        }

        private void Init()
        {
            if (_initialized) return;

            _client = _kubernetesClientFactory.Create();
            _initialized = true;
        }

        // Protected implementation of Dispose pattern.
        private void Dispose(bool disposing)
        {
            if (_disposed)
                return;

            if (disposing) _client?.Dispose();

            _disposed = true;
        }
    }
}