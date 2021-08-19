using NUnit.Framework;

namespace WindowsPrometheusSync.Test
{
    [TestFixture(Category = "Unit")]
    public class ProgramTests
    {
        [Test]
        [Category("Unit")]
        public void DependencyInjectionTest()
        {
            // Arrange
            var host = Program.CreateHostBuilder(null).Build();

            // Act
            var test = host.Services.GetService(typeof(IKubernetesClientWrapper));
            var scraperService = host.Services.GetService(typeof(SyncService));

            // Assert
            Assert.IsNotNull(scraperService);
        }
    }
}