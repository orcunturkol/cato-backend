namespace Cato.Infrastructure.Redis;

public class RedisSettings
{
    public string ConnectionString { get; set; } = "localhost:6379";
    public string InstanceName { get; set; } = "cato";
}
