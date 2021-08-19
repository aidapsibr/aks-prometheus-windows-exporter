using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using k8s;
using k8s.Models;
using Microsoft.Extensions.Logging;

namespace WindowsPrometheusSync
{
    /// <summary>
    /// Wraps up the I/O needed for the sync process
    /// </summary>
    public interface IKubernetesClientWrapper
    {
        /// <summary>
        /// Get some basic info on all windows nodes currently in the cluster
        /// </summary>
        Task<IList<V1Node>> GetWindowsNodesAsync(CancellationToken cancellationToken);

        /// <summary>
        /// Gets the prometheus scrape config secret so we can maintain the list of scrape jobs for each windows node
        /// </summary>
        Task<V1Secret> GetPrometheusScrapeConfigSecretAsync(CancellationToken cancellationToken);

        /// <summary>
        /// Updates the secret
        /// </summary>
        Task<V1Secret> UpdatePrometheusScrapeConfigSecretAsync(V1Secret secret,
            CancellationToken cancellationToken);
    }

    public class KubernetesClientWrapper : IKubernetesClientWrapper, IDisposable
    {
        private bool _initialized;
        private Kubernetes _client;
        private bool _disposed;

        private const string SecretNamespace = "sumologic";
        private const string SecretName = "collection-kube-prometheus-prometheus-scrape-confg";

        private readonly IKubernetesClientFactory _kubernetesClientFactory;
        private readonly ILogger<KubernetesClientWrapper> _logger;

        public KubernetesClientWrapper(IKubernetesClientFactory kubernetesClientFactory, ILogger<KubernetesClientWrapper> logger)
        {
            _kubernetesClientFactory = kubernetesClientFactory;
            _logger = logger;
        }

        private void Init()
        {
            if (_initialized)
            {
                return;
            }

            _client = _kubernetesClientFactory.Create();
            _initialized = true;
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
                result = await _client.ReadNamespacedSecretAsync(SecretName, SecretNamespace,
                    cancellationToken: cancellationToken);
            }
            catch (Microsoft.Rest.HttpOperationException ex)
                when (ex.Response.StatusCode == HttpStatusCode.NotFound)
            {
                try
                {
                    // Check for prometheus server config in the default namespace as a failover
                    result = await _client.ReadNamespacedSecretAsync(SecretName, "default",
                        cancellationToken: cancellationToken);
                }
                catch (Microsoft.Rest.HttpOperationException ex2)
                    when (ex.Response.StatusCode == HttpStatusCode.NotFound)
                {
                    _logger.LogWarning(ex2, "Unable to access scrape config secret");
                }
            }
            catch (Microsoft.Rest.HttpOperationException ex)
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

        public Task<V1Secret> UpdatePrometheusScrapeConfigSecretAsync(V1Secret secret, CancellationToken cancellationToken)
        {
            Init();

            return _client.ReplaceNamespacedSecretAsync(
                secret, 
                secret.Name(), 
                secret.Namespace(),
                cancellationToken: cancellationToken);
        }

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
                _client?.Dispose();
            }

            _disposed = true;
        }
    }
}