using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;

namespace WindowsPrometheusSync.Test
{
    [TestFixture(Category = "Unit")]
    public class PrometheusConfigChangeTrackerTests
    {
        private const string ArtifactDirectory = "./test-artifacts/PrometheusConfigChangeTrackerTests/";

        private static readonly NodeInfo DefaultNodeInfo =
            new NodeInfo(
                "nodename",
                new Dictionary<string, string>(new[]
                {
                    new KeyValuePair<string, string>("label_name", "label_value")
                }));

        [Test]
        [TestCase("blank.yaml", "blank-with-scrape-job.yaml", true, TestName = "Initial_StartWithBlank")]
        [TestCase("blank-with-scrape-job.yaml", "blank-with-scrape-job.yaml", false, TestName = "Initial_StartWithBlankIncJob")]
        [TestCase("blank-with-scrape-job-and-default-node.yaml", "blank-with-scrape-job-and-default-node.yaml", false, TestName = "Initial_StartWithBlankIncJobAndNode")]
        [TestCase("default.yaml", "default-with-scrape-job.yaml", true, TestName = "Initial_StartWithDefault")]
        [TestCase("default-with-scrape-job.yaml", "default-with-scrape-job.yaml", false, TestName = "Initial_StartWithDefaultIncJob")]
        [TestCase("default-with-scrape-job-and-default-node.yaml", "default-with-scrape-job-and-default-node.yaml", false, TestName = "Initial_StartWithDefaultIncJobAndNode")]
        public void InitialBehaviorTest(string initialStatePath, string expectedStatePath, bool expectNeedsUpdate)
        {
            // Arrange
            var initialYamlString = File.ReadAllText(ArtifactDirectory + initialStatePath);
            var expectedYamlString = File.ReadAllText(ArtifactDirectory + expectedStatePath);
            var changeTracker = new PrometheusConfigChangeTracker(initialYamlString);

            // Act
            var actualNeedsUpdate = changeTracker.NeedsUpdate;
            var actualYamlString = changeTracker.ToString();

            // Assert
            Assert.AreEqual(expectNeedsUpdate, actualNeedsUpdate);
            Assert.AreEqual(expectedYamlString, actualYamlString);
        }
        
        [Test]
        [TestCase("blank-with-scrape-job.yaml", false, TestName = "List_StartWithBlankIncJob")]
        [TestCase("blank-with-scrape-job-and-default-node.yaml", true, TestName = "List_StartWithBlankIncJobAndNode")]
        [TestCase("default-with-scrape-job.yaml", false, TestName = "List_StartWithDefaultIncJob")]
        [TestCase("default-with-scrape-job-and-default-node.yaml", true, TestName = "List_StartWithDefaultIncJobAndNode")]
        public void ListTest(string initialStatePath, bool expectDefaultNode)
        {
            // Arrange
            var initialYamlString = File.ReadAllText(ArtifactDirectory + initialStatePath);
            var changeTracker = new PrometheusConfigChangeTracker(initialYamlString);

            // Act
            var result = changeTracker.List();
            var actualNeedsUpdate = changeTracker.NeedsUpdate;

            // Assert
            Assert.IsFalse(actualNeedsUpdate);
            if (expectDefaultNode)
            {
                Assert.AreEqual(1, result.Count);
                Assert.AreEqual(DefaultNodeInfo, result[0]);
            }
            else
            {
                Assert.AreEqual(0, result.Count);
            }
        }
        
        [Test]
        [TestCase("blank.yaml", "blank-with-scrape-job-and-default-node.yaml", TestName = "Add_StartWithBlank")]
        [TestCase("blank-with-scrape-job.yaml", "blank-with-scrape-job-and-default-node.yaml", TestName = "Add_StartWithBlankIncJob")]
        [TestCase("default.yaml", "default-with-scrape-job-and-default-node.yaml", TestName = "Add_StartWithDefault")]
        [TestCase("default-with-scrape-job.yaml", "default-with-scrape-job-and-default-node.yaml", TestName = "Add_StartWithDefaultIncJob")]
        public void AddTest(string initialStatePath, string expectedStatePath)
        {
            // Arrange
            var initialYamlString = File.ReadAllText(ArtifactDirectory + initialStatePath);
            var expectedYamlString = File.ReadAllText(ArtifactDirectory + expectedStatePath);
            var changeTracker = new PrometheusConfigChangeTracker(initialYamlString);

            // Act
            changeTracker.Add(DefaultNodeInfo);
            var actualNeedsUpdate = changeTracker.NeedsUpdate;
            var actualYamlString = changeTracker.ToString();

            // Assert
            Assert.IsTrue(actualNeedsUpdate);
            Assert.AreEqual(expectedYamlString, actualYamlString);
        }
        
        [Test]
        [TestCase("blank-with-scrape-job-and-default-node.yaml", TestName = "Add_Ex_StartWithBlankIncJobAndNode")]
        [TestCase("default-with-scrape-job-and-default-node.yaml", TestName = "Add_Ex_StartWithDefaultIncJobAndNode")]
        public void AddTest_ExceptionOnExistingJob(string initialStatePath)
        {
            // Arrange
            var initialYamlString = File.ReadAllText(ArtifactDirectory + initialStatePath);
            var changeTracker = new PrometheusConfigChangeTracker(initialYamlString);

            // Act & Assert
            Assert.That(() => changeTracker.Add(DefaultNodeInfo), Throws.TypeOf<InvalidOperationException>());
            Assert.IsFalse(changeTracker.NeedsUpdate);
        }
        
        [Test]
        [TestCase("blank-with-scrape-job.yaml", "blank-with-scrape-job.yaml", false, TestName = "Remove_StartWithBlankIncJob")]
        [TestCase("blank-with-scrape-job-and-default-node.yaml", "blank-with-scrape-job.yaml", true, TestName = "Remove_StartWithBlankIncJobAndNode")]
        [TestCase("default-with-scrape-job.yaml", "default-with-scrape-job.yaml", false, TestName = "Remove_StartWithDefaultIncJob")]
        [TestCase("default-with-scrape-job-and-default-node.yaml", "default-with-scrape-job.yaml", true, TestName = "Remove_StartWithDefaultIncJobAndNode")]
        public void RemoveTest(string initialStatePath, string expectedStatePath, bool expectNeedsUpdate)
        {
            // Arrange
            var initialYamlString = File.ReadAllText(ArtifactDirectory + initialStatePath);
            var expectedYamlString = File.ReadAllText(ArtifactDirectory + expectedStatePath);
            var changeTracker = new PrometheusConfigChangeTracker(initialYamlString);

            // Act
            changeTracker.Remove(DefaultNodeInfo);
            var actualNeedsUpdate = changeTracker.NeedsUpdate;
            var actualYamlString = changeTracker.ToString();

            // Assert
            Assert.AreEqual(expectNeedsUpdate, actualNeedsUpdate);
            Assert.AreEqual(expectedYamlString, actualYamlString);
        }
    }
}
