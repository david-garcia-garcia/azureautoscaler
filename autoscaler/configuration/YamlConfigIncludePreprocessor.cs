using Microsoft.Extensions.Logging;
using YamlDotNet.RepresentationModel;

namespace poolautoscaler.configuration
{
    /// <summary>
    /// Expands <c>$include</c> directives in the top-level <c>Resources</c> sequence of a YAML config file.
    /// Each <c>- $include: relative/path.yaml</c> entry is replaced with the resource entries from the referenced file.
    /// </summary>
    internal static class YamlConfigIncludePreprocessor
    {
        /// <summary>
        /// Reads <paramref name="configFilePath"/>, expands any <c>$include</c> items in the
        /// top-level <c>Resources</c> sequence, and returns the merged YAML text ready for
        /// <see cref="Microsoft.Extensions.Configuration.IConfigurationBuilder"/>.
        /// </summary>
        /// <param name="configFilePath">Path to the main <c>config.yml</c> file.</param>
        /// <param name="logger">Logger for include diagnostics (each included file is logged at Information level).</param>
        /// <returns>YAML text after processing (unchanged if there is nothing to expand).</returns>
        public static string LoadConfigWithIncludes(string configFilePath, ILogger logger)
        {
            var text = File.ReadAllText(configFilePath);
            var yaml = new YamlStream();
            yaml.Load(new StringReader(text));

            if (yaml.Documents.Count == 0)
            {
                return text;
            }

            if (yaml.Documents[0].RootNode is not YamlMappingNode rootMapping)
            {
                return text;
            }

            YamlSequenceNode? resourcesSeq = null;
            foreach (var pair in rootMapping.Children)
            {
                if (pair.Key is YamlScalarNode keyScalar && keyScalar.Value == "Resources")
                {
                    resourcesSeq = pair.Value as YamlSequenceNode;
                    break;
                }
            }

            if (resourcesSeq == null || !ResourcesSequenceContainsInclude(resourcesSeq))
            {
                return text;
            }

            var baseDir = Path.GetDirectoryName(Path.GetFullPath(configFilePath));
            if (string.IsNullOrEmpty(baseDir))
            {
                baseDir = Directory.GetCurrentDirectory();
            }

            ExpandResourcesIncludes(resourcesSeq, baseDir, configFilePath, logger);

            using var writer = new StringWriter();
            yaml.Save(writer, assignAnchors: false);
            return writer.ToString();
        }

        /// <summary>
        /// Returns <c>true</c> and sets <paramref name="path"/> when <paramref name="node"/> is a
        /// single-key mapping whose key is <c>$include</c>.
        /// </summary>
        /// <param name="node">The YAML node to inspect.</param>
        /// <param name="path">The include path scalar value, or <c>null</c> when returning <c>false</c>.</param>
        /// <returns><c>true</c> if the node is a <c>$include</c> directive; otherwise <c>false</c>.</returns>
        internal static bool TryGetIncludePath(YamlNode node, out string? path)
        {
            path = null;
            if (node is not YamlMappingNode map || map.Children.Count != 1)
            {
                return false;
            }

            foreach (var pair in map.Children)
            {
                if (pair.Key is YamlScalarNode keyNode && keyNode.Value == "$include")
                {
                    if (pair.Value is YamlScalarNode valueNode)
                    {
                        path = valueNode.Value;
                        return true;
                    }

                    throw new InvalidOperationException("$include value must be a string path (YAML scalar).");
                }
            }

            return false;
        }

        private static bool ResourcesSequenceContainsInclude(YamlSequenceNode resourcesSeq)
        {
            foreach (var child in resourcesSeq.Children)
            {
                if (TryGetIncludePath(child, out _))
                {
                    return true;
                }
            }

            return false;
        }

        private static void ExpandResourcesIncludes(
            YamlSequenceNode resourcesSeq,
            string configDirectory,
            string mainConfigPath,
            ILogger logger)
        {
            var newChildren = new List<YamlNode>();
            foreach (var child in resourcesSeq.Children)
            {
                if (TryGetIncludePath(child, out var relativePath))
                {
                    if (string.IsNullOrWhiteSpace(relativePath))
                    {
                        throw new InvalidOperationException(
                            $"$include path is empty or whitespace in Resources of '{mainConfigPath}'.");
                    }

                    var resolvedPath = Path.GetFullPath(Path.Combine(configDirectory, relativePath));

                    var normalizedConfigDir = configDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                        + Path.DirectorySeparatorChar;
                    if (!resolvedPath.StartsWith(normalizedConfigDir, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidOperationException(
                            $"$include path '{relativePath}' resolves outside the configuration directory '{configDirectory}'. Path traversal is not allowed.");
                    }

                    if (!File.Exists(resolvedPath))
                    {
                        throw new FileNotFoundException(
                            $"Configuration $include file was not found: '{resolvedPath}'. Referenced from Resources in '{mainConfigPath}'.",
                            resolvedPath);
                    }

                    logger.LogInformation("Loading Resources $include: {IncludedConfigPath}", resolvedPath);

                    var includedYaml = new YamlStream();
                    includedYaml.Load(new StringReader(File.ReadAllText(resolvedPath)));

                    if (includedYaml.Documents.Count == 0)
                    {
                        continue;
                    }

                    var includedRoot = includedYaml.Documents[0].RootNode;
                    if (includedRoot is not YamlSequenceNode includedSeq)
                    {
                        throw new InvalidOperationException(
                            $"Included file '{resolvedPath}' must have a YAML sequence (list) at the root, with one resource entry per list item (same shape as entries under Resources in the main config). Root node is {DescribeYamlKind(includedRoot)}.");
                    }

                    foreach (var includedItem in includedSeq.Children)
                    {
                        newChildren.Add(CloneYamlNode(includedItem));
                    }
                }
                else
                {
                    newChildren.Add(child);
                }
            }

            resourcesSeq.Children.Clear();
            foreach (var n in newChildren)
            {
                resourcesSeq.Children.Add(n);
            }
        }

        private static YamlNode CloneYamlNode(YamlNode node)
        {
            if (node.NodeType == YamlNodeType.Alias)
            {
                throw new InvalidOperationException(
                    "YAML anchors and aliases are not supported when expanding configuration $include.");
            }

            return node switch
            {
                YamlScalarNode scalar => new YamlScalarNode(scalar.Value)
                {
                    Style = scalar.Style,
                    Tag = scalar.Tag,
                },
                YamlSequenceNode seq => CloneSequence(seq),
                YamlMappingNode map => CloneMapping(map),
                _ => throw new NotSupportedException($"Unsupported YAML node type: {node.GetType().Name}"),
            };
        }

        private static YamlSequenceNode CloneSequence(YamlSequenceNode seq)
        {
            var clone = new YamlSequenceNode { Style = seq.Style, Tag = seq.Tag };
            foreach (var child in seq.Children)
            {
                clone.Add(CloneYamlNode(child));
            }

            return clone;
        }

        private static YamlMappingNode CloneMapping(YamlMappingNode map)
        {
            var clone = new YamlMappingNode { Style = map.Style, Tag = map.Tag };
            foreach (var pair in map.Children)
            {
                clone.Add(CloneYamlNode(pair.Key), CloneYamlNode(pair.Value));
            }

            return clone;
        }

        private static string DescribeYamlKind(YamlNode node)
        {
            return node switch
            {
                YamlMappingNode => "a mapping",
                YamlSequenceNode => "a sequence",
                YamlScalarNode s => $"a scalar ({s.Value ?? string.Empty})",
                _ => node.GetType().Name,
            };
        }
    }
}
