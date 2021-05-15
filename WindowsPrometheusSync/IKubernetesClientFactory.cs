using k8s;

namespace WindowsPrometheusSync
{
    public interface IKubernetesClientFactory
    {
        Kubernetes Create();
    }

    internal class KubernetesClientFactory : IKubernetesClientFactory
    {
        /// <summary>
        /// If you leave this true unit tests will fail to remind us to put this back
        /// </summary>
        internal bool UseLocalConfig => false;

        public Kubernetes Create()
        {
            return new Kubernetes(GetConfig());
        }

        private KubernetesClientConfiguration GetConfig()
        {
            if (UseLocalConfig)
            {
                // Use this when debugging locally
                return KubernetesClientConfiguration.BuildConfigFromConfigFile(@"C:\Users\corey.caldwell\.kube\config","sk-sandbox-westus2-aks-admin");
            }

            return KubernetesClientConfiguration.BuildDefaultConfig();
        }
    }
}
