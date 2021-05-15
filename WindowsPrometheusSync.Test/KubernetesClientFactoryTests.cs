using NUnit.Framework;

namespace WindowsPrometheusSync.Test
{
    [TestFixture(Category = "Unit")]
    public class KubernetesClientFactoryTests
    {
        /// <summary>
        /// CYA test to hopefully keep us from checking this in with the local config enabled
        /// </summary>
        [Test]
        public void EnsureWereNotUsingLocalK8SConfig()
        {
            Assert.IsFalse(new KubernetesClientFactory().UseLocalConfig);
        }
    }
}
