using LyoCrystal.InstanceManagement;
using Xunit;

namespace Base05.Tests;

public sealed class ServiceInstanceProfileTests
{
    [Fact]
    public void 测试实例档案_原子保存重载且只保存秘密引用()
    {
        string root = Path.Combine(Path.GetTempPath(), "LEG09-profile-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "runtime"));
        File.WriteAllText(Path.Combine(root, "runtime", "server.exe"), "fixture");
        var profile = CreateValid(root);
        profile.SecretReference = "secret://test-server";
        var store = new ServiceInstanceProfileStore(root);
        try
        {
            store.Save(profile);
            profile.ServerId = "server-two";
            store.Save(profile);
            ServiceInstanceProfile loaded = store.Load("test-one");
            string json = File.ReadAllText(Path.Combine(root, "instances", "test-one.json"));

            Assert.Equal("server-two", loaded.ServerId);
            Assert.Equal(profile.Components[0].BasePort, loaded.Components[0].BasePort);
            Assert.Contains("secret://test-server", json, StringComparison.Ordinal);
            Assert.DoesNotContain("password", json, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(new[] { "test-one" }, store.ListInstanceIds());
            Assert.Empty(Directory.EnumerateFiles(Path.Combine(root, "instances"), "*.tmp-*"));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void 重复端口路径越界与依赖环_返回稳定诊断并阻断保存()
    {
        string root = Path.Combine(Path.GetTempPath(), "LEG09-invalid-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var profile = CreateValid(root);
        profile.LoginBasePort = 7000;
        profile.Components[0].BasePort = 7000;
        profile.Components[0].ExecutablePath = "..\\escape.exe";
        profile.Components[0].DependsOn.Add("micro");
        profile.Components.Add(new ServiceComponentProfile
        {
            Id = "micro",
            Role = ServiceComponentRole.MicroGateway,
            ExecutablePath = "runtime/micro.exe",
            WorkingDirectory = "runtime",
            BasePort = 8080,
            DependsOn = ["server"]
        });
        try
        {
            IReadOnlyList<InstanceDiagnostic> diagnostics = ServiceInstanceProfileValidator.Validate(profile, inspectFileSystem: false);

            Assert.Contains(diagnostics, item => item.Code == "LEG09-PROFILE-PORT-002");
            Assert.Contains(diagnostics, item => item.Code == "LEG09-PROFILE-PATH-003");
            Assert.Contains(diagnostics, item => item.Code == "LEG09-PROFILE-DEPENDENCY-002");
            Assert.Throws<InvalidDataException>(() => new ServiceInstanceProfileStore(root).Save(profile));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void 正式实例内嵌秘密或缺少秘密引用_失败关闭()
    {
        var profile = CreateValid(Path.GetTempPath());
        profile.Environment = ServiceEnvironmentKind.Production;
        profile.SecretReference = "plain-password";

        IReadOnlyList<InstanceDiagnostic> diagnostics = ServiceInstanceProfileValidator.Validate(profile, inspectFileSystem: false);

        Assert.Contains(diagnostics, item => item.Code == "LEG09-PROFILE-SECRET-001");
    }

    private static ServiceInstanceProfile CreateValid(string root) => new()
    {
        InstanceId = "test-one",
        Environment = ServiceEnvironmentKind.Test,
        ServerId = "server-one",
        PortOffset = 100,
        RootDirectory = root,
        LoginAddress = "127.0.0.1",
        LoginBasePort = 7000,
        Components =
        [
            new ServiceComponentProfile
            {
                Id = "server",
                Role = ServiceComponentRole.GameServer,
                DependencyMode = ServiceDependencyMode.Exclusive,
                ExecutablePath = "runtime/server.exe",
                WorkingDirectory = "runtime",
                BasePort = 7100,
                LogPath = "logs/server.log",
                ExpectedVersion = "1.0.0"
            }
        ]
    };
}
