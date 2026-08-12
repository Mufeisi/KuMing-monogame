using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Xunit;

namespace Base05.Tests;

public sealed class ResourceManifestTests
{
    [Fact]
    public void Resource_manifest_declares_source_and_phase_contracts()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "resources.manifest.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;

        Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("3e96959ff0cdbf2618144746423ad7aeb0fdafbf",
            root.GetProperty("repositoryResourceSourceRevision").GetString());
        Assert.Equal("Tools/ResourceBaseline.ps1",
            root.GetProperty("contract").GetProperty("acquire").GetProperty("script").GetString());
        Assert.Equal("Acquire", root.GetProperty("contract").GetProperty("acquire").GetProperty("action").GetString());
        Assert.Equal("Validate", root.GetProperty("contract").GetProperty("validate").GetProperty("action").GetString());

        var ids = new HashSet<string>(StringComparer.Ordinal);
        Assert.NotEmpty(root.GetProperty("resources").EnumerateArray());
        foreach (var resource in root.GetProperty("resources").EnumerateArray())
        {
            var id = resource.GetProperty("id").GetString();
            Assert.False(string.IsNullOrWhiteSpace(id));
            Assert.True(ids.Add(id!));

            var source = resource.GetProperty("source");
            var kind = source.GetProperty("type").GetString();
            Assert.False(string.IsNullOrWhiteSpace(source.GetProperty("id").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(source.GetProperty("locator").GetString()));
            Assert.True(source.TryGetProperty("acquisition", out var acquisition));
            Assert.False(string.IsNullOrWhiteSpace(acquisition.GetProperty("method").GetString()));

            if (kind is "none")
            {
                Assert.False(resource.GetProperty("required").GetBoolean());
                Assert.Equal(source.GetProperty("version").GetString(), resource.GetProperty("version").GetString());
                Assert.Equal("5ad38304b535c2987dbd24657c1a11b884984ff600d9f389deb0d4e634fee792",
                    source.GetProperty("versionSha256").GetString());
                Assert.Equal("assert-absent", source.GetProperty("validation").GetProperty("method").GetString());
                Assert.Equal("final", source.GetProperty("validation").GetProperty("phase").GetString());
                Assert.Equal("target-absent", source.GetProperty("validation").GetProperty("scope").GetString());
                continue;
            }

            Assert.False(string.IsNullOrWhiteSpace(source.GetProperty("version").GetString()));
            Assert.Equal(source.GetProperty("version").GetString(), resource.GetProperty("version").GetString());
            AssertSha256(source.GetProperty("versionSha256").GetString(), $"{id}.source.versionSha256");
            AssertSha256(source.GetProperty("sha256").GetString(), $"{id}.source.sha256");
            var versionText = source.GetProperty("version").GetString()!.Trim().Normalize(NormalizationForm.FormC);
            Assert.Equal(HashText(versionText), source.GetProperty("versionSha256").GetString());
            Assert.Equal("SHA256", source.GetProperty("validation").GetProperty("algorithm").GetString());

            var final = resource.GetProperty("final");
            AssertSha256(final.GetProperty("sha256").GetString(), $"{id}.final.sha256");
            Assert.True(final.GetProperty("fileCount").GetInt64() >= 0);
            Assert.True(final.GetProperty("bytes").GetInt64() >= 0);
            var phase = kind == "local-authorized" ? "source" : "final";
            Assert.Equal(phase, source.GetProperty("validation").GetProperty("phase").GetString());
            var phaseSpec = kind == "local-authorized" ? source : final;
            Assert.Equal(phaseSpec.GetProperty("fileCount").GetInt64(), source.GetProperty("fileCount").GetInt64());
            Assert.Equal(phaseSpec.GetProperty("bytes").GetInt64(), source.GetProperty("bytes").GetInt64());
            Assert.Equal(phaseSpec.GetProperty("sha256").GetString(), source.GetProperty("sha256").GetString());
            if (kind == "local-authorized")
            {
                Assert.True(resource.TryGetProperty("acquired", out var acquired));
                AssertSha256(acquired.GetProperty("sha256").GetString(), $"{id}.acquired.sha256");
                Assert.Equal(source.GetProperty("locator").GetString(),
                    acquisition.GetProperty("externalRootRelativePath").GetString());
            }
            if (id == "pc-runtime-assets")
            {
                Assert.Equal(6671, source.GetProperty("fileCount").GetInt64());
                Assert.Equal(6672, final.GetProperty("fileCount").GetInt64());
            }
        }
    }

    [Fact]
    public void Acquire_allows_only_exact_repository_overlay_and_rejects_drift_or_extra()
    {
        using var fixture = Fixture.Create(includeRepositoryOverlay: true);

        var success = fixture.Run("Acquire", "All");
        Assert.True(success.ExitCode == 0, success.Output);
        Assert.Equal("new", File.ReadAllText(Path.Combine(fixture.TargetPath, "new.txt")));

        fixture.ResetTarget();
        File.WriteAllText(Path.Combine(fixture.TargetPath, "tracked.txt"), "changed");
        var drift = fixture.Run("Acquire", "All");
        Assert.NotEqual(0, drift.ExitCode);
        Assert.Contains("overlay", drift.Output, StringComparison.OrdinalIgnoreCase);

        fixture.ResetTarget();
        File.WriteAllText(Path.Combine(fixture.TargetPath, "extra.txt"), "extra");
        var extra = fixture.Run("Acquire", "All");
        Assert.NotEqual(0, extra.ExitCode);
        Assert.Contains("overlay", extra.Output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Acquire_requires_existing_overlay_and_rolls_back_on_injected_failure()
    {
        using var missing = Fixture.Create(includeRepositoryOverlay: true);
        Directory.Delete(missing.TargetPath, recursive: true);
        var missingResult = missing.Run("Acquire", "All");
        Assert.NotEqual(0, missingResult.ExitCode);
        Assert.Contains("必须已存在", missingResult.Output, StringComparison.Ordinal);

        using var rollback = Fixture.Create(includeRepositoryOverlay: true);
        var rollbackResult = rollback.Run("Acquire", "All", failAfterReplace: 1);
        Assert.NotEqual(0, rollbackResult.ExitCode);
        Assert.Contains("事务回滚", rollbackResult.Output, StringComparison.Ordinal);
        Assert.Equal("tracked", File.ReadAllText(Path.Combine(rollback.TargetPath, "tracked.txt")));
        Assert.False(File.Exists(Path.Combine(rollback.TargetPath, "new.txt")));
    }

    [Fact]
    public void Acquire_transaction_handles_multi_resource_rollback_and_post_commit_cleanup()
    {
        using (var rollback = TransactionFixture.Create())
        {
            var result = rollback.Run(failAfterReplace: 2);
            Assert.NotEqual(0, result.ExitCode);
            Assert.Contains("事务回滚完成", result.Output, StringComparison.Ordinal);
            rollback.AssertOldTargets();
            Assert.DoesNotContain("保留暂存目录", result.Output, StringComparison.Ordinal);
        }

        using (var cleanupFailure = TransactionFixture.Create())
        {
            var result = cleanupFailure.Run(failBackupCleanup: true);
            Assert.NotEqual(0, result.ExitCode);
            Assert.Contains("提交后备份清理失败", result.Output, StringComparison.Ordinal);
            cleanupFailure.AssertNewTargets();
            var backupPath = TransactionFixture.ExtractBackupPath(result.Output);
            Assert.True(Directory.Exists(backupPath), result.Output);
        }

        using (var rollbackFailure = TransactionFixture.Create())
        {
            var result = rollbackFailure.Run(failAfterReplace: 2, failRollback: true);
            Assert.NotEqual(0, result.ExitCode);
            Assert.Contains("事务回滚未完成", result.Output, StringComparison.Ordinal);
            var backupPath = TransactionFixture.ExtractBackupPath(result.Output);
            Assert.True(Directory.Exists(backupPath), result.Output);
            Assert.Contains("保留暂存目录", result.Output, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Manifest_contract_fails_closed_for_empty_resources_required_none_skip_and_wrong_entrypoint()
    {
        using var fixture = Fixture.Create(includeRepositoryOverlay: false);

        var root = JsonNode.Parse(File.ReadAllText(fixture.ManifestPath))!.AsObject();
        root["resources"] = new JsonArray();
        fixture.WriteManifest(root);
        Assert.NotEqual(0, fixture.Run("Validate", "Repository").ExitCode);

        using var requiredNone = Fixture.Create(includeRepositoryOverlay: false);
        root = JsonNode.Parse(File.ReadAllText(requiredNone.ManifestPath))!.AsObject();
        var resource = root["resources"]![0]!.AsObject();
        resource["source"]!["type"] = "none";
        requiredNone.WriteManifest(root);
        Assert.NotEqual(0, requiredNone.Run("Validate", "Repository").ExitCode);

        using var requiredSkip = Fixture.Create(includeRepositoryOverlay: false);
        root = JsonNode.Parse(File.ReadAllText(requiredSkip.ManifestPath))!.AsObject();
        resource = root["resources"]![0]!.AsObject();
        resource["source"]!["type"] = "skip";
        requiredSkip.WriteManifest(root);
        Assert.NotEqual(0, requiredSkip.Run("Validate", "Repository").ExitCode);

        using var wrongResourceVersion = Fixture.Create(includeRepositoryOverlay: false);
        root = JsonNode.Parse(File.ReadAllText(wrongResourceVersion.ManifestPath))!.AsObject();
        resource = root["resources"]![0]!.AsObject();
        resource["version"] = "fixture-resource-v2";
        wrongResourceVersion.WriteManifest(root);
        Assert.NotEqual(0, wrongResourceVersion.Run("Validate", "Repository").ExitCode);

        using var wrongPhase = Fixture.Create(includeRepositoryOverlay: false);
        root = JsonNode.Parse(File.ReadAllText(wrongPhase.ManifestPath))!.AsObject();
        resource = root["resources"]![0]!.AsObject();
        resource["source"]!["validation"]!["phase"] = "final";
        wrongPhase.WriteManifest(root);
        Assert.NotEqual(0, wrongPhase.Run("Validate", "Repository").ExitCode);

        using var wrongDigest = Fixture.Create(includeRepositoryOverlay: false);
        wrongDigest.ClearTarget();
        wrongDigest.TamperSource();
        var wrongDigestResult = wrongDigest.Run("Acquire", "All");
        Assert.NotEqual(0, wrongDigestResult.ExitCode);
        Assert.Contains("源", wrongDigestResult.Output, StringComparison.Ordinal);

        using var wrongEntrypoint = Fixture.Create(includeRepositoryOverlay: false);
        root = JsonNode.Parse(File.ReadAllText(wrongEntrypoint.ManifestPath))!.AsObject();
        root["contract"]!["acquire"]!["script"] = "Tools/not-resource-baseline.ps1";
        wrongEntrypoint.WriteManifest(root);
        Assert.NotEqual(0, wrongEntrypoint.Run("Validate", "Repository").ExitCode);

        using var wrongVersion = Fixture.Create(includeRepositoryOverlay: false);
        root = JsonNode.Parse(File.ReadAllText(wrongVersion.ManifestPath))!.AsObject();
        resource = root["resources"]![0]!.AsObject();
        resource["source"]!["version"] = "fixture-v2";
        wrongVersion.WriteManifest(root);
        Assert.NotEqual(0, wrongVersion.Run("Validate", "Repository").ExitCode);

        using var wrongLocator = Fixture.Create(includeRepositoryOverlay: false);
        root = JsonNode.Parse(File.ReadAllText(wrongLocator.ManifestPath))!.AsObject();
        resource = root["resources"]![0]!.AsObject();
        resource["source"]!["locator"] = "other-source";
        wrongLocator.WriteManifest(root);
        Assert.NotEqual(0, wrongLocator.Run("Validate", "Repository").ExitCode);
    }

    [Fact]
    public void None_contract_rejects_non_absent_version_and_invalid_scope()
    {
        using (var wrongVersion = Fixture.Create(includeRepositoryOverlay: false))
        {
            var root = JsonNode.Parse(File.ReadAllText(wrongVersion.ManifestPath))!.AsObject();
            ConfigureOptionalNone(root["resources"]![0]!.AsObject(), "fixture-none-v2", "target-absent");
            wrongVersion.WriteManifest(root);
            Assert.NotEqual(0, wrongVersion.Run("Validate", "Repository").ExitCode);
        }

        using (var missingScope = Fixture.Create(includeRepositoryOverlay: false))
        {
            var root = JsonNode.Parse(File.ReadAllText(missingScope.ManifestPath))!.AsObject();
            var resource = root["resources"]![0]!.AsObject();
            ConfigureOptionalNone(resource, "absent", "target-absent");
            resource["source"]!["validation"]!.AsObject().Remove("scope");
            missingScope.WriteManifest(root);
            Assert.NotEqual(0, missingScope.Run("Validate", "Repository").ExitCode);
        }

        using (var wrongScope = Fixture.Create(includeRepositoryOverlay: false))
        {
            var root = JsonNode.Parse(File.ReadAllText(wrongScope.ManifestPath))!.AsObject();
            ConfigureOptionalNone(root["resources"]![0]!.AsObject(), "absent", "directory-tree");
            wrongScope.WriteManifest(root);
            Assert.NotEqual(0, wrongScope.Run("Validate", "Repository").ExitCode);
        }
    }

    [Fact]
    public void Validate_cross_checks_package_index_zip_and_sidecar()
    {
        using var fixture = PackageFixture.Create();
        var valid = fixture.Run();
        Assert.True(valid.ExitCode == 0, valid.Output);

        fixture.TamperIndex();
        var badIndex = fixture.Run();
        Assert.NotEqual(0, badIndex.ExitCode);
        Assert.Contains("索引分包 extra SHA256 不匹配", badIndex.Output, StringComparison.Ordinal);

        fixture.RestoreIndex();
        fixture.TamperSidecar();
        var badSidecar = fixture.Run();
        Assert.NotEqual(0, badSidecar.ExitCode);
        Assert.Contains("索引分包 extra sidecar SHA256 不匹配", badSidecar.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_none_resource_accepts_absence_and_rejects_appearing_directory()
    {
        using var fixture = Fixture.Create(includeRepositoryOverlay: false);
        var root = JsonNode.Parse(File.ReadAllText(fixture.ManifestPath))!.AsObject();
        var resource = root["resources"]![0]!.AsObject();
        resource["id"] = "fixture-absence";
        resource["required"] = false;
        resource["path"] = "AbsentFixture";
        resource.Remove("sourcePath");
        resource["version"] = "absent";
        resource.Remove("acquired");
        resource.Remove("final");
        resource.Remove("repositoryOverlay");
        resource["source"] = new JsonObject
        {
            ["type"] = "none",
            ["id"] = "none:AbsentFixture",
            ["locator"] = "not-present",
            ["version"] = "absent",
            ["versionSha256"] = HashText("absent"),
            ["acquisition"] = new JsonObject { ["method"] = "not-present" },
            ["validation"] = new JsonObject
            {
                ["algorithm"] = "SHA256",
                ["scope"] = "target-absent",
                ["phase"] = "final",
                ["method"] = "assert-absent",
            },
        };
        fixture.WriteManifest(root);
        var absent = fixture.Run("Validate", "Repository");
        Assert.Equal(0, absent.ExitCode);

        Directory.CreateDirectory(Path.Combine(fixture.RootPath, "AbsentFixture"));
        File.WriteAllText(Path.Combine(fixture.RootPath, "AbsentFixture", "appeared.txt"), "unexpected");
        var appeared = fixture.Run("Validate", "Repository");
        Assert.NotEqual(0, appeared.ExitCode);
        Assert.Contains("absence 契约要求目标不存在", appeared.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_pc_digest_counts_bootstrap_and_rejects_generated_outputs()
    {
        using var fixture = PcDigestFixture.Create();
        var result = fixture.Run();
        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("文件计数/大小不匹配", result.Output, StringComparison.Ordinal);
    }

    private static void AssertSha256(string? value, string label)
    {
        Assert.Matches("^[0-9a-f]{64}$", value ?? string.Empty);
    }

    private static string HashText(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static void ConfigureOptionalNone(JsonObject resource, string version, string scope)
    {
        resource["required"] = false;
        resource["path"] = "AbsentFixture";
        resource.Remove("sourcePath");
        resource.Remove("acquired");
        resource.Remove("final");
        resource.Remove("repositoryOverlay");
        resource["version"] = version;
        resource["source"] = new JsonObject
        {
            ["type"] = "none",
            ["id"] = "none:AbsentFixture",
            ["locator"] = "not-present",
            ["version"] = version,
            ["versionSha256"] = HashText(version),
            ["acquisition"] = new JsonObject { ["method"] = "not-present" },
            ["validation"] = new JsonObject
            {
                ["algorithm"] = "SHA256",
                ["scope"] = scope,
                ["phase"] = "final",
                ["method"] = "assert-absent",
            },
        };
    }

    private sealed class Fixture : IDisposable
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
        };

        private readonly string _root;
        private readonly string _cleanupRoot;
        private readonly string _externalRoot;
        private readonly string _scriptPath;
        private readonly bool _includeRepositoryOverlay;
        private readonly string _sourcePath;

        private Fixture(string root, string externalRoot, string cleanupRoot, string scriptPath, bool includeRepositoryOverlay)
        {
            _root = root;
            _cleanupRoot = cleanupRoot;
            _externalRoot = externalRoot;
            _scriptPath = scriptPath;
            _includeRepositoryOverlay = includeRepositoryOverlay;
            _sourcePath = Path.Combine(_externalRoot, "source");
            TargetPath = Path.Combine(_root, "BootstrapAssets");
            ManifestPath = Path.Combine(_root, "resources.manifest.json");
        }

        public string TargetPath { get; }
        public string ManifestPath { get; }
        public string RootPath => _root;

        public static Fixture Create(bool includeRepositoryOverlay)
        {
            var cleanupRoot = Path.Combine(Path.GetTempPath(), "resource-baseline-tests", Guid.NewGuid().ToString("N"));
            var root = Path.Combine(cleanupRoot, "repo");
            var external = Path.Combine(cleanupRoot, "external");
            Directory.CreateDirectory(Path.Combine(root, "BootstrapAssets"));
            Directory.CreateDirectory(Path.Combine(external, "source"));
            File.WriteAllText(Path.Combine(root, "BootstrapAssets", "tracked.txt"), "tracked");
            File.WriteAllText(Path.Combine(external, "source", "tracked.txt"), "source");
            File.WriteAllText(Path.Combine(external, "source", "new.txt"), "new");

            var fixture = new Fixture(root, external, cleanupRoot, FindScriptPath(), includeRepositoryOverlay);
            fixture.WriteManifest(fixture.BuildManifest());
            return fixture;
        }

        public void ResetTarget()
        {
            if (Directory.Exists(TargetPath)) Directory.Delete(TargetPath, recursive: true);
            Directory.CreateDirectory(TargetPath);
            File.WriteAllText(Path.Combine(TargetPath, "tracked.txt"), "tracked");
        }

        public void ClearTarget()
        {
            if (Directory.Exists(TargetPath)) Directory.Delete(TargetPath, recursive: true);
        }

        public void TamperSource()
        {
            File.WriteAllText(Path.Combine(_sourcePath, "tracked.txt"), "tampered");
        }

        public void WriteManifest(JsonObject manifest)
            => File.WriteAllText(ManifestPath, manifest.ToJsonString(JsonOptions), new UTF8Encoding(false));

        public ProcessResult Run(string action, string scope, int? failAfterReplace = null)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "pwsh",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
                CreateNoWindow = true,
            };
            startInfo.ArgumentList.Add("-NoProfile");
            startInfo.ArgumentList.Add("-File");
            startInfo.ArgumentList.Add(_scriptPath);
            startInfo.ArgumentList.Add("-Action");
            startInfo.ArgumentList.Add(action);
            startInfo.ArgumentList.Add("-Scope");
            startInfo.ArgumentList.Add(scope);
            startInfo.ArgumentList.Add("-RepositoryRoot");
            startInfo.ArgumentList.Add(_root);
            startInfo.ArgumentList.Add("-ManifestPath");
            startInfo.ArgumentList.Add("resources.manifest.json");
            startInfo.ArgumentList.Add("-ExternalRoot");
            startInfo.ArgumentList.Add(_externalRoot);
            if (failAfterReplace.HasValue)
            {
                startInfo.Environment["RESOURCE_BASELINE_TEST_FAIL_AFTER_REPLACE"] = failAfterReplace.Value.ToString();
            }

            using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("无法启动 pwsh。");
            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();
            Assert.True(process.WaitForExit(60_000), "ResourceBaseline.ps1 未在 60 秒内结束。");
            Task.WaitAll(outputTask, errorTask);
            return new ProcessResult(process.ExitCode, outputTask.Result + errorTask.Result);
        }

        private JsonObject BuildManifest()
        {
            var sourceDigest = ComputeDigest(_sourcePath, "BootstrapAssets");
            var overlayDigest = ComputeDigest(TargetPath, "BootstrapAssets");
            var version = "fixture-v1";
            var source = new JsonObject
            {
                ["type"] = "local-authorized",
                ["id"] = "fixture-source",
                ["locator"] = "source",
                ["version"] = version,
                ["versionSha256"] = HashText(version),
                ["acquisition"] = new JsonObject
                {
                    ["method"] = "copy-tree-with-overlay",
                    ["script"] = "Tools/ResourceBaseline.ps1",
                    ["action"] = "Acquire",
                    ["scope"] = "All",
                    ["externalRootRelativePath"] = "source",
                },
                ["validation"] = new JsonObject { ["algorithm"] = "SHA256", ["scope"] = "directory-tree", ["phase"] = "source" },
                ["fileCount"] = sourceDigest.FileCount,
                ["bytes"] = sourceDigest.Bytes,
                ["sha256"] = sourceDigest.Sha256,
            };
            var resource = new JsonObject
            {
                ["id"] = "fixture",
                ["required"] = true,
                ["path"] = "BootstrapAssets",
                ["sourcePath"] = "source",
                ["source"] = source,
                ["version"] = version,
                ["acquired"] = DigestNode(sourceDigest),
                ["final"] = DigestNode(sourceDigest),
            };
            if (_includeRepositoryOverlay) resource["repositoryOverlay"] = DigestNode(overlayDigest);

            return new JsonObject
            {
                ["schemaVersion"] = 1,
                ["manifestVersion"] = "fixture",
                ["repositoryResourceSourceRevision"] = new string('0', 40),
                ["contract"] = new JsonObject
                {
                    ["acquire"] = new JsonObject { ["script"] = "Tools/ResourceBaseline.ps1", ["action"] = "Acquire", ["scope"] = "All" },
                    ["validate"] = new JsonObject { ["script"] = "Tools/ResourceBaseline.ps1", ["action"] = "Validate", ["scope"] = "Repository|All" },
                },
                ["resources"] = new JsonArray(resource),
            };
        }

        internal static JsonObject DigestNode(Digest digest)
            => new() { ["fileCount"] = digest.FileCount, ["bytes"] = digest.Bytes, ["sha256"] = digest.Sha256 };

        internal static Digest ComputeDigest(string root, string canonicalPrefix)
        {
            var lines = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                .Select(path =>
                {
                    var relative = Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/');
                    var bytes = new FileInfo(path).Length;
                    var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
                    return $"{canonicalPrefix}/{relative}|{bytes}|{hash}";
                })
                .OrderBy(line => line, StringComparer.Ordinal)
                .ToArray();
            var canonical = string.Join("\n", lines) + "\n";
            var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
            var bytesTotal = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Sum(path => new FileInfo(path).Length);
            return new Digest(lines.LongLength, bytesTotal, digest);
        }

        private static string FindScriptPath()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null)
            {
                var candidate = Path.Combine(directory.FullName, "Tools", "ResourceBaseline.ps1");
                if (File.Exists(candidate)) return candidate;
                directory = directory.Parent;
            }

            throw new DirectoryNotFoundException("无法定位 Tools/ResourceBaseline.ps1。");
        }

        public void Dispose()
        {
            if (Directory.Exists(_cleanupRoot)) Directory.Delete(_cleanupRoot, recursive: true);
        }
    }

    private sealed class TransactionFixture : IDisposable
    {
        private readonly string _cleanupRoot;
        private readonly string _root;
        private readonly string _externalRoot;
        private readonly string _targetA;
        private readonly string _targetB;

        private TransactionFixture(string cleanupRoot, string root, string externalRoot, string targetA, string targetB)
        {
            _cleanupRoot = cleanupRoot;
            _root = root;
            _externalRoot = externalRoot;
            _targetA = targetA;
            _targetB = targetB;
        }

        public static TransactionFixture Create()
        {
            var cleanupRoot = Path.Combine(Path.GetTempPath(), "resource-baseline-transaction-tests", Guid.NewGuid().ToString("N"));
            var root = Path.Combine(cleanupRoot, "repo");
            var externalRoot = Path.Combine(cleanupRoot, "external");
            var sourceA = Path.Combine(externalRoot, "sourceA");
            var sourceB = Path.Combine(externalRoot, "sourceB");
            var targetA = Path.Combine(root, "TargetA");
            var targetB = Path.Combine(root, "TargetB");
            Directory.CreateDirectory(sourceA);
            Directory.CreateDirectory(sourceB);
            Directory.CreateDirectory(targetA);
            Directory.CreateDirectory(targetB);
            File.WriteAllText(Path.Combine(sourceA, "new-a.txt"), "new-a");
            File.WriteAllText(Path.Combine(sourceB, "new-b.txt"), "new-b");
            File.WriteAllText(Path.Combine(targetA, "old-a.txt"), "old-a");
            File.WriteAllText(Path.Combine(targetB, "old-b.txt"), "old-b");

            var resourceA = BuildResource("resource-a", "TargetA", "sourceA", sourceA, targetA);
            var resourceB = BuildResource("resource-b", "TargetB", "sourceB", sourceB, targetB);
            var manifest = new JsonObject
            {
                ["schemaVersion"] = 1,
                ["manifestVersion"] = "fixture",
                ["repositoryResourceSourceRevision"] = new string('0', 40),
                ["contract"] = new JsonObject
                {
                    ["acquire"] = new JsonObject { ["script"] = "Tools/ResourceBaseline.ps1", ["action"] = "Acquire", ["scope"] = "All" },
                    ["validate"] = new JsonObject { ["script"] = "Tools/ResourceBaseline.ps1", ["action"] = "Validate", ["scope"] = "Repository|All" },
                },
                ["resources"] = new JsonArray { resourceA, resourceB },
            };
            Directory.CreateDirectory(root);
            File.WriteAllText(Path.Combine(root, "resources.manifest.json"), manifest.ToJsonString(new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
            return new TransactionFixture(cleanupRoot, root, externalRoot, targetA, targetB);
        }

        private static JsonObject BuildResource(string id, string path, string sourcePath, string source, string target)
        {
            var sourceDigest = Fixture.ComputeDigest(source, path);
            var overlayDigest = Fixture.ComputeDigest(target, path);
            var version = id + "-v1";
            var sourceNode = new JsonObject
            {
                ["type"] = "local-authorized",
                ["id"] = "authorized-local:" + sourcePath,
                ["locator"] = sourcePath,
                ["version"] = version,
                ["versionSha256"] = HashText(version),
                ["acquisition"] = new JsonObject
                {
                    ["method"] = "copy-tree-with-overlay",
                    ["script"] = "Tools/ResourceBaseline.ps1",
                    ["action"] = "Acquire",
                    ["scope"] = "All",
                    ["externalRootRelativePath"] = sourcePath,
                },
                ["validation"] = new JsonObject { ["algorithm"] = "SHA256", ["scope"] = "directory-tree", ["phase"] = "source" },
                ["fileCount"] = sourceDigest.FileCount,
                ["bytes"] = sourceDigest.Bytes,
                ["sha256"] = sourceDigest.Sha256,
            };
            return new JsonObject
            {
                ["id"] = id,
                ["required"] = true,
                ["path"] = path,
                ["sourcePath"] = sourcePath,
                ["source"] = sourceNode,
                ["version"] = version,
                ["acquired"] = Fixture.DigestNode(sourceDigest),
                ["final"] = Fixture.DigestNode(sourceDigest),
                ["repositoryOverlay"] = Fixture.DigestNode(overlayDigest),
            };
        }

        public ProcessResult Run(int? failAfterReplace = null, bool failBackupCleanup = false, bool failRollback = false)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "pwsh",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
                CreateNoWindow = true,
            };
            startInfo.Environment.Remove("RESOURCE_BASELINE_TEST_FAIL_AFTER_REPLACE");
            startInfo.Environment.Remove("RESOURCE_BASELINE_TEST_FAIL_BACKUP_CLEANUP");
            startInfo.Environment.Remove("RESOURCE_BASELINE_TEST_FAIL_ROLLBACK");
            if (failAfterReplace.HasValue) startInfo.Environment["RESOURCE_BASELINE_TEST_FAIL_AFTER_REPLACE"] = failAfterReplace.Value.ToString();
            if (failBackupCleanup) startInfo.Environment["RESOURCE_BASELINE_TEST_FAIL_BACKUP_CLEANUP"] = "1";
            if (failRollback) startInfo.Environment["RESOURCE_BASELINE_TEST_FAIL_ROLLBACK"] = "1";
            startInfo.ArgumentList.Add("-NoProfile");
            startInfo.ArgumentList.Add("-File");
            startInfo.ArgumentList.Add(FindScriptPath());
            startInfo.ArgumentList.Add("-Action");
            startInfo.ArgumentList.Add("Acquire");
            startInfo.ArgumentList.Add("-Scope");
            startInfo.ArgumentList.Add("All");
            startInfo.ArgumentList.Add("-RepositoryRoot");
            startInfo.ArgumentList.Add(_root);
            startInfo.ArgumentList.Add("-ManifestPath");
            startInfo.ArgumentList.Add("resources.manifest.json");
            startInfo.ArgumentList.Add("-ExternalRoot");
            startInfo.ArgumentList.Add(_externalRoot);

            using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("无法启动 pwsh。");
            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();
            Assert.True(process.WaitForExit(60_000), "ResourceBaseline.ps1 未在 60 秒内结束。");
            Task.WaitAll(outputTask, errorTask);
            return new ProcessResult(process.ExitCode, outputTask.Result + errorTask.Result);
        }

        public void AssertOldTargets()
        {
            Assert.Equal("old-a", File.ReadAllText(Path.Combine(_targetA, "old-a.txt")));
            Assert.Equal("old-b", File.ReadAllText(Path.Combine(_targetB, "old-b.txt")));
            Assert.False(File.Exists(Path.Combine(_targetA, "new-a.txt")));
            Assert.False(File.Exists(Path.Combine(_targetB, "new-b.txt")));
        }

        public void AssertNewTargets()
        {
            Assert.Equal("new-a", File.ReadAllText(Path.Combine(_targetA, "new-a.txt")));
            Assert.Equal("new-b", File.ReadAllText(Path.Combine(_targetB, "new-b.txt")));
            Assert.False(File.Exists(Path.Combine(_targetA, "old-a.txt")));
            Assert.False(File.Exists(Path.Combine(_targetB, "old-b.txt")));
        }

        public static string ExtractBackupPath(string output)
        {
            var match = Regex.Match(output, @"备份目录：(?<path>[^\r\n。]+)");
            Assert.True(match.Success, output);
            return match.Groups["path"].Value.Trim();
        }

        private static string FindScriptPath()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null)
            {
                var candidate = Path.Combine(directory.FullName, "Tools", "ResourceBaseline.ps1");
                if (File.Exists(candidate)) return candidate;
                directory = directory.Parent;
            }

            throw new DirectoryNotFoundException("无法定位 Tools/ResourceBaseline.ps1。");
        }

        public void Dispose()
        {
            if (Directory.Exists(_cleanupRoot)) Directory.Delete(_cleanupRoot, recursive: true);
        }
    }

    private sealed class PcDigestFixture : IDisposable
    {
        private readonly string _cleanupRoot;
        private readonly string _root;
        private readonly string _externalRoot;
        private readonly string _manifestPath;

        private PcDigestFixture(string cleanupRoot, string root, string externalRoot, string manifestPath)
        {
            _cleanupRoot = cleanupRoot;
            _root = root;
            _externalRoot = externalRoot;
            _manifestPath = manifestPath;
        }

        public static PcDigestFixture Create()
        {
            var cleanupRoot = Path.Combine(Path.GetTempPath(), "resource-baseline-pc-digest-tests", Guid.NewGuid().ToString("N"));
            var root = Path.Combine(cleanupRoot, "repo");
            var externalRoot = Path.Combine(cleanupRoot, "external");
            var source = Path.Combine(externalRoot, "Client_VorticeDX11");
            var target = Path.Combine(root, "Build", "Client_VorticeDX11");
            var overlay = Path.Combine(externalRoot, "monogame", "Mir2Config.ini");
            Directory.CreateDirectory(Path.Combine(source, "Data"));
            Directory.CreateDirectory(Path.Combine(source, "BootstrapAssets"));
            Directory.CreateDirectory(Path.Combine(source, "bin"));
            Directory.CreateDirectory(Path.Combine(source, "obj"));
            Directory.CreateDirectory(Path.GetDirectoryName(overlay)!);
            Directory.CreateDirectory(target);
            File.WriteAllText(Path.Combine(source, "Language.ini"), "language");
            File.WriteAllText(Path.Combine(source, "Data", "sample.Lib"), "library");
            File.WriteAllText(Path.Combine(source, "BootstrapAssets", "source-bootstrap.txt"), "source-bootstrap");
            File.WriteAllText(Path.Combine(source, "bin", "source.bin"), "source-bin");
            File.WriteAllText(Path.Combine(source, "obj", "source.obj"), "source-obj");
            File.WriteAllText(overlay, "overlay");
            File.Copy(Path.Combine(source, "Language.ini"), Path.Combine(target, "Language.ini"));
            Directory.CreateDirectory(Path.Combine(target, "Data"));
            File.Copy(Path.Combine(source, "Data", "sample.Lib"), Path.Combine(target, "Data", "sample.Lib"));
            Directory.CreateDirectory(Path.Combine(target, "BootstrapAssets"));
            File.Copy(Path.Combine(source, "BootstrapAssets", "source-bootstrap.txt"), Path.Combine(target, "BootstrapAssets", "source-bootstrap.txt"));
            Directory.CreateDirectory(Path.Combine(target, "bin"));
            Directory.CreateDirectory(Path.Combine(target, "obj"));
            File.Copy(Path.Combine(source, "bin", "source.bin"), Path.Combine(target, "bin", "source.bin"));
            File.Copy(Path.Combine(source, "obj", "source.obj"), Path.Combine(target, "obj", "source.obj"));
            File.Copy(overlay, Path.Combine(target, "Mir2Config.ini"));
            var sourceDigest = Fixture.ComputeDigest(source, "Build/Client_VorticeDX11");
            var finalDigest = Fixture.ComputeDigest(target, "Build/Client_VorticeDX11");
            Directory.CreateDirectory(Path.Combine(target, "BootstrapAssets"));
            File.WriteAllText(Path.Combine(target, "BootstrapAssets", "generated.json"), "generated");
            File.WriteAllText(Path.Combine(target, "bin", "generated.dll"), "generated");
            File.WriteAllText(Path.Combine(target, "obj", "generated.o"), "generated");
            var version = "pc-fixture-v1";
            var sourceNode = new JsonObject
            {
                ["type"] = "local-authorized",
                ["id"] = "authorized-local:Client_VorticeDX11",
                ["locator"] = "Client_VorticeDX11",
                ["version"] = version,
                ["versionSha256"] = HashText(version),
                ["acquisition"] = new JsonObject
                {
                    ["method"] = "copy-tree-with-overlay",
                    ["script"] = "Tools/ResourceBaseline.ps1",
                    ["action"] = "Acquire",
                    ["scope"] = "All",
                    ["externalRootRelativePath"] = "Client_VorticeDX11",
                },
                ["validation"] = new JsonObject { ["algorithm"] = "SHA256", ["scope"] = "directory-tree", ["phase"] = "source" },
                ["fileCount"] = sourceDigest.FileCount,
                ["bytes"] = sourceDigest.Bytes,
                ["sha256"] = sourceDigest.Sha256,
            };
            var resource = new JsonObject
            {
                ["id"] = "pc-fixture",
                ["required"] = true,
                ["path"] = "Build/Client_VorticeDX11",
                ["sourcePath"] = "Client_VorticeDX11",
                ["source"] = sourceNode,
                ["version"] = version,
                ["acquired"] = Fixture.DigestNode(finalDigest),
                ["final"] = Fixture.DigestNode(finalDigest),
                ["overlays"] = new JsonArray(new JsonObject
                {
                    ["sourcePath"] = "monogame/Mir2Config.ini",
                    ["target"] = "Mir2Config.ini",
                    ["bytes"] = new FileInfo(overlay).Length,
                    ["sha256"] = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(overlay))).ToLowerInvariant(),
                }),
            };
            var manifest = new JsonObject
            {
                ["schemaVersion"] = 1,
                ["manifestVersion"] = "fixture",
                ["repositoryResourceSourceRevision"] = new string('0', 40),
                ["contract"] = new JsonObject
                {
                    ["acquire"] = new JsonObject { ["script"] = "Tools/ResourceBaseline.ps1", ["action"] = "Acquire", ["scope"] = "All" },
                    ["validate"] = new JsonObject { ["script"] = "Tools/ResourceBaseline.ps1", ["action"] = "Validate", ["scope"] = "Repository|All" },
                },
                ["resources"] = new JsonArray(resource),
            };
            var manifestPath = Path.Combine(root, "resources.manifest.json");
            Directory.CreateDirectory(root);
            File.WriteAllText(manifestPath, manifest.ToJsonString(new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
            return new PcDigestFixture(cleanupRoot, root, externalRoot, manifestPath);
        }

        public ProcessResult Run()
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "pwsh",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
                CreateNoWindow = true,
            };
            startInfo.ArgumentList.Add("-NoProfile");
            startInfo.ArgumentList.Add("-File");
            startInfo.ArgumentList.Add(FindScriptPath());
            startInfo.ArgumentList.Add("-Action");
            startInfo.ArgumentList.Add("Validate");
            startInfo.ArgumentList.Add("-Scope");
            startInfo.ArgumentList.Add("All");
            startInfo.ArgumentList.Add("-RepositoryRoot");
            startInfo.ArgumentList.Add(_root);
            startInfo.ArgumentList.Add("-ManifestPath");
            startInfo.ArgumentList.Add("resources.manifest.json");
            startInfo.ArgumentList.Add("-ExternalRoot");
            startInfo.ArgumentList.Add(_externalRoot);

            using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("无法启动 pwsh。");
            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();
            Assert.True(process.WaitForExit(60_000), "ResourceBaseline.ps1 未在 60 秒内结束。");
            Task.WaitAll(outputTask, errorTask);
            return new ProcessResult(process.ExitCode, outputTask.Result + errorTask.Result);
        }

        private static string FindScriptPath()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null)
            {
                var candidate = Path.Combine(directory.FullName, "Tools", "ResourceBaseline.ps1");
                if (File.Exists(candidate)) return candidate;
                directory = directory.Parent;
            }

            throw new DirectoryNotFoundException("无法定位 Tools/ResourceBaseline.ps1。");
        }

        public void Dispose()
        {
            if (Directory.Exists(_cleanupRoot)) Directory.Delete(_cleanupRoot, recursive: true);
        }
    }

    private sealed class PackageFixture : IDisposable
    {
        private readonly string _cleanupRoot;
        private readonly string _root;
        private readonly string _packagesRoot;
        private readonly string _manifestPath;
        private readonly string _indexPath;
        private readonly string _sidecarPath;
        private readonly string _extraSidecarPath;
        private readonly string _originalIndex;
        private readonly string _originalSidecar;

        private PackageFixture(string cleanupRoot, string root, string packagesRoot, string manifestPath,
            string indexPath, string sidecarPath, string extraSidecarPath, string originalIndex, string originalSidecar)
        {
            _cleanupRoot = cleanupRoot;
            _root = root;
            _packagesRoot = packagesRoot;
            _manifestPath = manifestPath;
            _indexPath = indexPath;
            _sidecarPath = sidecarPath;
            _extraSidecarPath = extraSidecarPath;
            _originalIndex = originalIndex;
            _originalSidecar = originalSidecar;
        }

        public static PackageFixture Create()
        {
            var cleanupRoot = Path.Combine(Path.GetTempPath(), "resource-baseline-package-tests", Guid.NewGuid().ToString("N"));
            var root = Path.Combine(cleanupRoot, "repo");
            var packages = Path.Combine(root, "Build", "Mobile", "BootstrapRepo", "Packages");
            Directory.CreateDirectory(packages);
            Directory.CreateDirectory(Path.Combine(root, "Tools"));
            File.WriteAllText(Path.Combine(root, "Tools", "Mobile-BootstrapPackageRepoExport.ps1"), "# fixture export entrypoint");

            var zipPath = Path.Combine(packages, "core-startup.zip");
            using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes("fixture")))
            {
                var entry = archive.CreateEntry("hello.txt");
                using var entryStream = entry.Open();
                stream.CopyTo(entryStream);
            }

            var zipBytes = File.ReadAllBytes(zipPath);
            var zipHash = Convert.ToHexString(SHA256.HashData(zipBytes)).ToLowerInvariant();
            var extraZipPath = Path.Combine(packages, "extra.zip");
            using (var archive = ZipFile.Open(extraZipPath, ZipArchiveMode.Create))
            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes("extra-fixture")))
            {
                var entry = archive.CreateEntry("extra.txt");
                using var entryStream = entry.Open();
                stream.CopyTo(entryStream);
            }
            var extraZipBytes = File.ReadAllBytes(extraZipPath);
            var extraZipHash = Convert.ToHexString(SHA256.HashData(extraZipBytes)).ToLowerInvariant();
            var index = new JsonObject
            {
                ["GeneratedAtUtc"] = "1970-01-01T00:00:00.0000000Z",
                ["ResourceVersion"] = "fixture",
                ["Packages"] = new JsonArray(new JsonObject
                {
                    ["Name"] = "core-startup",
                    ["Sha256"] = zipHash,
                    ["Size"] = zipBytes.LongLength,
                }, new JsonObject
                {
                    ["Name"] = "extra",
                    ["Sha256"] = extraZipHash,
                    ["Size"] = extraZipBytes.LongLength,
                }),
            };
            var indexPath = Path.Combine(packages, "bootstrap-package-index.json");
            File.WriteAllText(indexPath, index.ToJsonString(new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
            var sidecarPath = zipPath + ".sha256";
            File.WriteAllText(sidecarPath, zipHash, new UTF8Encoding(false));
            var extraSidecarPath = extraZipPath + ".sha256";
            File.WriteAllText(extraSidecarPath, extraZipHash, new UTF8Encoding(false));

            var digest = Fixture.ComputeDigest(Path.Combine(root, "Build", "Mobile", "BootstrapRepo"), "Build/Mobile/BootstrapRepo");
            var sourceVersion = "fixture-package-v1";
            var source = new JsonObject
            {
                ["type"] = "generated",
                ["id"] = "fixture-generated",
                ["locator"] = "Tools/Mobile-BootstrapPackageRepoExport.ps1",
                ["version"] = sourceVersion,
                ["versionSha256"] = HashText(sourceVersion),
                ["acquisition"] = new JsonObject
                {
                    ["method"] = "deterministic-export",
                    ["script"] = "Tools/Mobile-BootstrapPackageRepoExport.ps1",
                    ["action"] = "Export",
                    ["scope"] = "Repository",
                },
                ["validation"] = new JsonObject { ["algorithm"] = "SHA256", ["scope"] = "directory-tree", ["phase"] = "final" },
                ["fileCount"] = digest.FileCount,
                ["bytes"] = digest.Bytes,
                ["sha256"] = digest.Sha256,
            };
            var resource = new JsonObject
            {
                ["id"] = "fixture-generated",
                ["required"] = true,
                ["path"] = "Build/Mobile/BootstrapRepo",
                ["source"] = source,
                ["version"] = sourceVersion,
                ["final"] = Fixture.DigestNode(digest),
            };
            resource["final"]!["artifacts"] = new JsonArray(
                new JsonObject { ["path"] = "Packages/core-startup.zip", ["bytes"] = zipBytes.LongLength, ["sha256"] = zipHash, ["sidecar"] = "Packages/core-startup.zip.sha256" },
                new JsonObject { ["path"] = "Packages/bootstrap-package-index.json", ["bytes"] = new FileInfo(indexPath).Length, ["sha256"] = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(indexPath))).ToLowerInvariant() });

            var manifest = new JsonObject
            {
                ["schemaVersion"] = 1,
                ["manifestVersion"] = "fixture",
                ["repositoryResourceSourceRevision"] = new string('0', 40),
                ["contract"] = new JsonObject
                {
                    ["acquire"] = new JsonObject { ["script"] = "Tools/ResourceBaseline.ps1", ["action"] = "Acquire", ["scope"] = "All" },
                    ["validate"] = new JsonObject { ["script"] = "Tools/ResourceBaseline.ps1", ["action"] = "Validate", ["scope"] = "Repository|All" },
                },
                ["resources"] = new JsonArray(resource),
            };
            var manifestPath = Path.Combine(root, "resources.manifest.json");
            File.WriteAllText(manifestPath, manifest.ToJsonString(new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
            return new PackageFixture(cleanupRoot, root, packages, manifestPath, indexPath, sidecarPath, extraSidecarPath,
                File.ReadAllText(indexPath), File.ReadAllText(sidecarPath));
        }

        public ProcessResult Run()
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "pwsh",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
                CreateNoWindow = true,
            };
            startInfo.ArgumentList.Add("-NoProfile");
            startInfo.ArgumentList.Add("-File");
            startInfo.ArgumentList.Add(FindScriptPath());
            startInfo.ArgumentList.Add("-Action");
            startInfo.ArgumentList.Add("Validate");
            startInfo.ArgumentList.Add("-Scope");
            startInfo.ArgumentList.Add("All");
            startInfo.ArgumentList.Add("-RepositoryRoot");
            startInfo.ArgumentList.Add(_root);
            startInfo.ArgumentList.Add("-ManifestPath");
            startInfo.ArgumentList.Add("resources.manifest.json");

            using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("无法启动 pwsh。");
            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();
            Assert.True(process.WaitForExit(60_000), "ResourceBaseline.ps1 未在 60 秒内结束。");
            Task.WaitAll(outputTask, errorTask);
            return new ProcessResult(process.ExitCode, outputTask.Result + errorTask.Result);
        }

        public void TamperIndex()
        {
            var node = JsonNode.Parse(File.ReadAllText(_indexPath))!.AsObject();
            node["Packages"]![1]!["Sha256"] = new string('0', 64);
            File.WriteAllText(_indexPath, node.ToJsonString(new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
            RefreshManifestDigest();
        }

        public void RestoreIndex()
        {
            File.WriteAllText(_indexPath, _originalIndex, new UTF8Encoding(false));
            RefreshManifestDigest();
        }

        public void TamperSidecar()
        {
            File.WriteAllText(_extraSidecarPath, new string('0', 64), new UTF8Encoding(false));
            RefreshManifestDigest();
        }

        private void RefreshManifestDigest()
        {
            var digest = Fixture.ComputeDigest(Path.Combine(_root, "Build", "Mobile", "BootstrapRepo"), "Build/Mobile/BootstrapRepo");
            var manifest = JsonNode.Parse(File.ReadAllText(_manifestPath))!.AsObject();
            var resource = manifest["resources"]![0]!.AsObject();
            var source = resource["source"]!.AsObject();
            source["fileCount"] = digest.FileCount;
            source["bytes"] = digest.Bytes;
            source["sha256"] = digest.Sha256;
            resource["final"]!["fileCount"] = digest.FileCount;
            resource["final"]!["bytes"] = digest.Bytes;
            resource["final"]!["sha256"] = digest.Sha256;
            var indexBytes = File.ReadAllBytes(_indexPath);
            var indexArtifact = resource["final"]!["artifacts"]!.AsArray()[1]!.AsObject();
            indexArtifact["bytes"] = indexBytes.LongLength;
            indexArtifact["sha256"] = Convert.ToHexString(SHA256.HashData(indexBytes)).ToLowerInvariant();
            File.WriteAllText(_manifestPath, manifest.ToJsonString(new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
        }

        private static string FindScriptPath()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null)
            {
                var candidate = Path.Combine(directory.FullName, "Tools", "ResourceBaseline.ps1");
                if (File.Exists(candidate)) return candidate;
                directory = directory.Parent;
            }

            throw new DirectoryNotFoundException("无法定位 Tools/ResourceBaseline.ps1。");
        }

        public void Dispose()
        {
            if (Directory.Exists(_cleanupRoot)) Directory.Delete(_cleanupRoot, recursive: true);
        }
    }

    private sealed record Digest(long FileCount, long Bytes, string Sha256);
    private sealed record ProcessResult(int ExitCode, string Output);
}
