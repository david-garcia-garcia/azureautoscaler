using Microsoft.Extensions.Logging;
using Moq;
using poolautoscaler.configuration;

namespace poolautoscaler.tests
{
    /// <summary>
    /// Tests for <see cref="YamlConfigIncludePreprocessor"/>.
    /// Each test writes real temp files so the preprocessor's File.Exists / File.ReadAllText paths are exercised.
    /// </summary>
    public class YamlConfigIncludePreprocessorTests : IDisposable
    {
        private readonly string tempDir;
        private readonly ILogger logger = new Mock<ILogger>().Object;

        public YamlConfigIncludePreprocessorTests()
        {
            this.tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(this.tempDir);
        }

        public void Dispose()
        {
            Directory.Delete(this.tempDir, recursive: true);
        }

        [Fact]
        public void NoResources_ReturnsTextUnchanged()
        {
            var yaml = "DefaultResourceWhatIf: false\n";
            var path = this.WriteConfig(yaml);

            var result = this.Load(path);

            Assert.Equal(yaml, result);
        }

        [Fact]
        public void ResourcesWithNoInclude_ReturnsTextUnchanged()
        {
            var yaml =
                "DefaultResourceWhatIf: false\n" +
                "Resources:\n" +
                "  - Frequency: 5m\n";
            var path = this.WriteConfig(yaml);

            var result = this.Load(path);

            Assert.Equal(yaml, result);
        }

        [Fact]
        public void SingleInclude_IsExpandedIntoResourcesList()
        {
            this.WriteInclude(
                "aks.yaml",
                "- Frequency: 5m\n" +
                "  Enabled: true\n");

            var path = this.WriteConfig(
                "DefaultResourceWhatIf: false\n" +
                "Resources:\n" +
                "  - $include: aks.yaml\n");

            var result = this.Load(path);

            Assert.Contains("Frequency: 5m", result);
            Assert.Contains("Enabled: true", result);
            Assert.DoesNotContain("$include", result);
        }

        [Fact]
        public void MultipleIncludes_AreExpandedInDeclarationOrder()
        {
            this.WriteInclude("first.yaml", "- Frequency: 1m\n");
            this.WriteInclude("second.yaml", "- Frequency: 2m\n");

            var path = this.WriteConfig(
                "Resources:\n" +
                "  - $include: first.yaml\n" +
                "  - $include: second.yaml\n");

            var result = this.Load(path);

            var idx1 = result.IndexOf("1m", StringComparison.Ordinal);
            var idx2 = result.IndexOf("2m", StringComparison.Ordinal);
            Assert.True(idx1 < idx2, "first.yaml entries should appear before second.yaml entries");
        }

        [Fact]
        public void InlineAndIncludeEntries_AreMixedInDeclarationOrder()
        {
            this.WriteInclude("extra.yaml", "- Frequency: 9m\n");

            var path = this.WriteConfig(
                "Resources:\n" +
                "  - Frequency: 1m\n" +
                "  - $include: extra.yaml\n" +
                "  - Frequency: 3m\n");

            var result = this.Load(path);

            var idx1 = result.IndexOf("1m", StringComparison.Ordinal);
            var idx9 = result.IndexOf("9m", StringComparison.Ordinal);
            var idx3 = result.IndexOf("3m", StringComparison.Ordinal);
            Assert.True(idx1 < idx9, "inline entry before include");
            Assert.True(idx9 < idx3, "include entry before trailing inline entry");
            Assert.DoesNotContain("$include", result);
        }

        [Fact]
        public void IncludePath_IsResolvedRelativeToConfigFileDirectory()
        {
            var subDir = Path.Combine(this.tempDir, "sub");
            Directory.CreateDirectory(subDir);
            File.WriteAllText(Path.Combine(subDir, "res.yaml"), "- Frequency: 7m\n");

            var path = this.WriteConfig(
                "Resources:\n" +
                "  - $include: sub/res.yaml\n");

            var result = this.Load(path);

            Assert.Contains("7m", result);
        }

        [Fact]
        public void EmptyIncludedFile_ContributesNoEntries()
        {
            this.WriteInclude("empty.yaml", string.Empty);

            var path = this.WriteConfig(
                "Resources:\n" +
                "  - $include: empty.yaml\n" +
                "  - Frequency: 4m\n");

            var result = this.Load(path);

            Assert.Contains("4m", result);
        }

        [Fact]
        public void MissingIncludedFile_ThrowsFileNotFoundException()
        {
            var path = this.WriteConfig(
                "Resources:\n" +
                "  - $include: does-not-exist.yaml\n");

            var ex = Assert.Throws<FileNotFoundException>(() => this.Load(path));
            Assert.Contains("does-not-exist.yaml", ex.Message);
        }

        [Fact]
        public void IncludedFileWithMappingRoot_ThrowsInvalidOperationException()
        {
            this.WriteInclude("bad.yaml", "key: value\n");

            var path = this.WriteConfig(
                "Resources:\n" +
                "  - $include: bad.yaml\n");

            var ex = Assert.Throws<InvalidOperationException>(() => this.Load(path));
            Assert.Contains("sequence", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void PathTraversal_ThrowsInvalidOperationException()
        {
            var parentDir = Path.GetDirectoryName(this.tempDir)!;
            var escapedFile = Path.Combine(parentDir, "secret.yaml");
            File.WriteAllText(escapedFile, "- Frequency: 5m\n");

            try
            {
                var path = this.WriteConfig(
                    "Resources:\n" +
                    "  - $include: ../secret.yaml\n");

                var ex = Assert.Throws<InvalidOperationException>(() => this.Load(path));
                Assert.Contains("traversal", ex.Message, StringComparison.OrdinalIgnoreCase);
            }
            finally
            {
                File.Delete(escapedFile);
            }
        }

        [Fact]
        public void EmptyIncludePath_ThrowsInvalidOperationException()
        {
            var path = this.WriteConfig(
                "Resources:\n" +
                "  - $include: ''\n");

            var ex = Assert.Throws<InvalidOperationException>(() => this.Load(path));
            Assert.Contains("empty", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void NestedIncludeInsideIncludedFile_IsNotExpanded()
        {
            this.WriteInclude(
                "outer.yaml",
                "- $include: nonexistent-inner.yaml\n");

            var path = this.WriteConfig(
                "Resources:\n" +
                "  - $include: outer.yaml\n");

            var result = this.Load(path);
            Assert.Contains("$include", result);
        }

        private string WriteConfig(string yaml)
        {
            var path = Path.Combine(this.tempDir, "config.yml");
            File.WriteAllText(path, yaml);
            return path;
        }

        private string WriteInclude(string name, string yaml)
        {
            var path = Path.Combine(this.tempDir, name);
            File.WriteAllText(path, yaml);
            return path;
        }

        private string Load(string configPath) =>
            YamlConfigIncludePreprocessor.LoadConfigWithIncludes(configPath, this.logger);
    }
}
