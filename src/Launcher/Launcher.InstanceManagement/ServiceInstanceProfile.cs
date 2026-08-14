using System.Text.Json.Serialization;

namespace LyoCrystal.InstanceManagement;

public enum ServiceEnvironmentKind
{
    Development,
    Test,
    Production
}

public enum ServiceComponentRole
{
    GameServer,
    MicroGateway,
    Auxiliary
}

public enum ServiceDependencyMode
{
    Exclusive,
    Shared
}

public enum ServiceHealthProbeKind
{
    Tcp,
    Http
}

public sealed class ServiceInstanceProfile
{
    public const string CurrentFormat = "lyocrystal-service-instance-v1";

    public string Format { get; set; } = CurrentFormat;
    public string InstanceId { get; set; } = string.Empty;
    public ServiceEnvironmentKind Environment { get; set; } = ServiceEnvironmentKind.Test;
    public string ServerId { get; set; } = string.Empty;
    public int PortOffset { get; set; }
    public string RootDirectory { get; set; } = string.Empty;
    public string LoginAddress { get; set; } = "127.0.0.1";
    public int LoginBasePort { get; set; } = 7000;
    public string SecretReference { get; set; } = string.Empty;
    public int ExpectedSchemaVersion { get; set; }
    public string ExpectedScriptRevision { get; set; } = string.Empty;
    public List<ServiceComponentProfile> Components { get; set; } = [];
}

public sealed class ServiceComponentProfile
{
    public string Id { get; set; } = string.Empty;
    public ServiceComponentRole Role { get; set; }
    public ServiceDependencyMode DependencyMode { get; set; }
    public string SharedGroup { get; set; } = string.Empty;
    public string ExecutablePath { get; set; } = string.Empty;
    public string WorkingDirectory { get; set; } = ".";
    public List<string> Arguments { get; set; } = [];
    public int BasePort { get; set; }
    public ServiceHealthProbeKind HealthProbe { get; set; } = ServiceHealthProbeKind.Tcp;
    public string HealthPath { get; set; } = "/api/health";
    public string StopPath { get; set; } = string.Empty;
    public int StartTimeoutSeconds { get; set; } = 30;
    public int StopTimeoutSeconds { get; set; } = 15;
    public string LogPath { get; set; } = string.Empty;
    public string ExpectedVersion { get; set; } = string.Empty;
    public List<string> DependsOn { get; set; } = [];
}

[JsonSourceGenerationOptions(WriteIndented = true, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, UseStringEnumConverter = true)]
[JsonSerializable(typeof(ServiceInstanceProfile))]
internal sealed partial class ServiceInstanceProfileJsonContext : JsonSerializerContext;
