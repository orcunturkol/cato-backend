using Amazon;
using Amazon.SecretsManager;
using Amazon.SecretsManager.Model;

namespace Cato.Infrastructure.Secrets;

/// <summary>The "Secrets" section: where credentials that the worker rotates are kept.</summary>
public sealed class SecretStoreSettings
{
    public const string SectionName = "Secrets";

    /// <summary>"aws" (AWS Secrets Manager, the server) or "file" (a local folder, development).</summary>
    public string Backend { get; set; } = "file";

    public string AwsRegion { get; set; } = "eu-central-1";

    /// <summary>File backend: one file per secret id, below this folder.</summary>
    public string FileDirectory { get; set; } = Path.Combine(AppContext.BaseDirectory, ".secrets");
}

/// <summary>
/// Credentials by id ("cato/steam/steamkit-auth"). A read of a secret with no value gives null.
/// Errors other than "no value" throw: the caller decides what an unreachable store means.
/// </summary>
public interface ISecretStore
{
    Task<string?> GetAsync(string id, CancellationToken ct);

    Task PutAsync(string id, string value, CancellationToken ct);
}

public static class SecretIds
{
    public const string SteamKitAuth = "cato/steam/steamkit-auth";
}

/// <summary>
/// AWS Secrets Manager through the default credential chain (the EC2 instance role). The
/// secrets must exist (infra/phase1 creates them): the role may read and write values only.
/// Each put keeps the previous value as AWSPREVIOUS, so a bad write does not lose the old one.
/// </summary>
public sealed class AwsSecretStore : ISecretStore
{
    private readonly IAmazonSecretsManager _client;

    public AwsSecretStore(IAmazonSecretsManager client) => _client = client;

    public static AwsSecretStore Create(SecretStoreSettings settings) =>
        new(new AmazonSecretsManagerClient(RegionEndpoint.GetBySystemName(settings.AwsRegion)));

    public async Task<string?> GetAsync(string id, CancellationToken ct)
    {
        try
        {
            var response = await _client.GetSecretValueAsync(new GetSecretValueRequest { SecretId = id }, ct);
            return string.IsNullOrEmpty(response.SecretString) ? null : response.SecretString;
        }
        catch (ResourceNotFoundException)
        {
            // A secret created by Terraform has no value until the first put.
            return null;
        }
    }

    public Task PutAsync(string id, string value, CancellationToken ct) =>
        _client.PutSecretValueAsync(new PutSecretValueRequest { SecretId = id, SecretString = value }, ct);
}

/// <summary>Local development: a file per secret id, readable by the owner only.</summary>
public sealed class FileSecretStore : ISecretStore
{
    private readonly string _directory;

    public FileSecretStore(string directory) => _directory = directory;

    private string PathOf(string id) => Path.Combine(_directory, id.Replace('/', '_') + ".secret");

    public async Task<string?> GetAsync(string id, CancellationToken ct)
    {
        var path = PathOf(id);
        if (!File.Exists(path))
            return null;
        var value = await File.ReadAllTextAsync(path, ct);
        return value.Length == 0 ? null : value;
    }

    public async Task PutAsync(string id, string value, CancellationToken ct)
    {
        Directory.CreateDirectory(_directory);
        var path = PathOf(id);
        var temp = path + ".tmp";
        await File.WriteAllTextAsync(temp, value, ct);
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(temp, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        File.Move(temp, path, overwrite: true);
    }
}
