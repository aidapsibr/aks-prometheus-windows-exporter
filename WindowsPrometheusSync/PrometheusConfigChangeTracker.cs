using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using YamlDotNet.Core.Events;
using YamlDotNet.RepresentationModel;

namespace WindowsPrometheusSync
{
    /// <summary>
    /// Utility class for making changes to the prometheus config to keep it scraping windows node exporter
    /// </summary>
    internal class PrometheusConfigChangeTracker
    {
        private const string JobNameValue = "prometheus-windows-node";
        private const string NodeJobPort = "9100";
        
        private readonly YamlDocument _prometheusConfigDocument;
        private readonly YamlSequenceNode _scrapeJobStaticConfigs;
        private readonly Dictionary<string, Tuple<YamlMappingNode, NodeInfo>> _nodeConfigs;

        /// <summary>
        /// Indicates if any changes have been made to the yaml that should be pushed up
        /// </summary>
        public bool NeedsUpdate { get; private set; }

        /// <summary>
        /// Helper class for common tag name string and to ward off fat fingers
        /// </summary>
        internal static class NodeTags
        {
            public static string JobName => "job_name";
            public static string StaticConfigs => "static_configs";
            public static string Targets => "targets";
            public static string Labels => "labels";
        }

        /// <summary>
        /// Reads <paramref name="prometheusConfigYaml"/> and ensures the basic config pieces are in place
        /// </summary>
        public PrometheusConfigChangeTracker(string prometheusConfigYaml)
        {
            using (var reader = new StringReader(prometheusConfigYaml))
            {
                var stream = new YamlStream();
                stream.Load(reader);

                _prometheusConfigDocument = stream.Documents.Count == 1 
                    ? stream.Documents[0] 
                    : null;

                if (_prometheusConfigDocument == null)
                {
                    NeedsUpdate = true;
                    _prometheusConfigDocument = new YamlDocument(new YamlMappingNode());
                }
            }

            var root = _prometheusConfigDocument.RootNode as YamlSequenceNode;

            if (root == null)
            {
                NeedsUpdate = true;
                root = new YamlSequenceNode();
                _prometheusConfigDocument = new YamlDocument(root);
            }

            var scrapeJob = root.Children
                .Select(x => x as YamlMappingNode)
                .SingleOrDefault(x =>
                    x != null
                    && x.Children.ContainsKey(NodeTags.JobName)
                    && x.Children[NodeTags.JobName] is YamlScalarNode {Value: JobNameValue});
            if (scrapeJob == null)
            {
                NeedsUpdate = true;
                scrapeJob = new YamlMappingNode(new[]
                {
                    new KeyValuePair<YamlNode, YamlNode>(NodeTags.JobName, new YamlScalarNode(JobNameValue)),
                    new KeyValuePair<YamlNode, YamlNode>(NodeTags.StaticConfigs, new YamlSequenceNode())
                });
                root.Children.Add(scrapeJob);
            }

            _scrapeJobStaticConfigs = scrapeJob.Children.ContainsKey(NodeTags.StaticConfigs)
                ? scrapeJob.Children[NodeTags.StaticConfigs] as YamlSequenceNode
                : null;
            if (_scrapeJobStaticConfigs == null)
            {
                NeedsUpdate = true;
                _scrapeJobStaticConfigs = new YamlSequenceNode();
                scrapeJob.Children.Add(NodeTags.StaticConfigs, _scrapeJobStaticConfigs);
            }
            _scrapeJobStaticConfigs.Style = SequenceStyle.Block;
            
            _nodeConfigs = _scrapeJobStaticConfigs.Children
                .Select(x => x as YamlMappingNode)
                .Where(x => x != null)
                .Select(x => Tuple.Create(x, StaticConfigToNodeInfo(x)))
                .Where(x => x.Item2 != null)
                .ToDictionary(x => x.Item2.Name, x => x);
        }

        /// <summary>
        /// Converts all the static configs into <see cref="NodeInfo"/> for easier comparison
        /// </summary>
        public IList<NodeInfo> List()
        {
            return _nodeConfigs
                .Select(x => x.Value.Item2)
                .ToList();
        }

        /// <summary>
        /// Converts the <paramref name="nodeInfo"/> into yaml config section and adds it to the document
        /// </summary>
        /// <exception cref="InvalidOperationException">Thrown if a config already exists for <paramref name="nodeInfo"/> <see cref="NodeInfo.Name"/></exception>
        public void Add(NodeInfo nodeInfo)
        {
            if (_nodeConfigs.ContainsKey(nodeInfo.Name))
            {
                throw new InvalidOperationException(
                    "A configuration already exists for this node. Only one per node is allowed");
            }

            NeedsUpdate = true;
            var config = NodeInfoToStaticConfig(nodeInfo);
            _nodeConfigs.Add(nodeInfo.Name, Tuple.Create(config, nodeInfo));
            _scrapeJobStaticConfigs.Children.Add(NodeInfoToStaticConfig(nodeInfo));
        }

        /// <summary>
        /// Removes the config section matching the <paramref name="nodeInfo"/> <see cref="NodeInfo.Name"/> in the targets
        /// </summary>
        public void Remove(NodeInfo nodeInfo)
        {
            if (_nodeConfigs.ContainsKey(nodeInfo.Name))
            {
                NeedsUpdate = true;
                var config = _nodeConfigs[nodeInfo.Name].Item1;
                _scrapeJobStaticConfigs.Children.Remove(config);
                _nodeConfigs.Remove(nodeInfo.Name);
            }
        }

        /// <summary>
        /// Outputs the string yaml from the current state of the edits
        /// </summary>
        /// <returns></returns>
        public override string ToString()
        {
            using(var stringSteam = new StringWriter())
            {
                var yamlStream = new YamlStream(_prometheusConfigDocument);
                yamlStream.Save(stringSteam, false);
                return stringSteam.ToString();
            }
        }

        /// <summary>
        /// Creates the scrape job yaml needed for a windows node
        /// </summary>
        private YamlMappingNode NodeInfoToStaticConfig(NodeInfo nodeInfo)
        {
            var nodeNameAndPort = $"{nodeInfo.Name}:{NodeJobPort}";
            var nodeLabels = nodeInfo.Labels
                .Select(x => new KeyValuePair<YamlNode, YamlNode>(x.Key, new YamlScalarNode(x.Value)));

            var result = new YamlMappingNode(new[]
            {
                new KeyValuePair<YamlNode, YamlNode>(NodeTags.Targets, new YamlSequenceNode(new YamlScalarNode(nodeNameAndPort))),
                new KeyValuePair<YamlNode, YamlNode>(NodeTags.Labels, new YamlMappingNode(nodeLabels))
            });

            return result;
        }
        
        /// <summary>
        /// Utility function for parsing a prometheus scrape config job yaml into <see cref="NodeInfo"/>
        /// </summary>
        /// <example>
        /// ...
        /// scrape_configs:
        /// - job_name: prometheus-windows-node
        ///   static_configs:
        ///   - targets:
        ///     - aksd1000000:9100
        ///     labels:
        ///       agentpool: win1
        ///       beta_kubernetes_io_arch: amd64
        ///       beta_kubernetes_io_instance_type: Standard_D8as_v4
        ///       beta_kubernetes_io_os: windows
        ///       failure_domain_beta_kubernetes_io_region: westus2
        ///       failure_domain_beta_kubernetes_io_zone: westus2-1
        ///       kubernetes_azure_com_cluster: test-cluster
        ///       kubernetes_azure_com_node_image_version: AKSWindows-2019-17763.1577.201111
        ///       kubernetes_azure_com_role: agent
        ///       kubernetes_io_arch: amd64
        ///       kubernetes_io_hostname: aksd1000000
        ///       kubernetes_io_os: windows
        ///       kubernetes_io_role: agent
        ///       node_role_kubernetes_io_agent: ''
        ///       node_kubernetes_io_instance_type: Standard_D8as_v4
        ///       node_kubernetes_io_windows_build: 10.0.17763
        ///       storageprofile: managed
        ///       storagetier: Premium_LRS
        ///       topology_kubernetes_io_region: westus2
        ///       topology_kubernetes_io_zone: westus2-1
        ///       # NOTE: labels here should be 1:1 with the target k8s node labels
        /// ...
        /// </example>
        private NodeInfo StaticConfigToNodeInfo(YamlMappingNode scrapeJobStaticConfig)
        {
            var targetsNode = scrapeJobStaticConfig?.Children.ContainsKey(NodeTags.Targets) == true
                ? scrapeJobStaticConfig.Children[NodeTags.Targets] as YamlSequenceNode
                : null;

            var targetNode = targetsNode?.Children.Count == 1
                ? targetsNode.Children[0] as YamlScalarNode
                : null;

            var labelsContainerNode = scrapeJobStaticConfig?.Children.ContainsKey(NodeTags.Labels) == true
                ? scrapeJobStaticConfig.Children[NodeTags.Labels] as YamlMappingNode
                : null;
            
            var name = targetNode?.Value?.Split(':', 2).First();
            var labels = labelsContainerNode?.Children
                .ToDictionary(x => ((YamlScalarNode)x.Key).Value, x => ((YamlScalarNode)x.Value).Value);

            return string.IsNullOrWhiteSpace(name) ? null : new NodeInfo(name, labels);
        }
    }
}