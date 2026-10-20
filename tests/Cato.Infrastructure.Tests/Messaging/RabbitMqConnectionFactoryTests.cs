using System.Net.Security;
using Cato.Infrastructure.Messaging;

namespace Cato.Infrastructure.Tests.Messaging;

public class RabbitMqConnectionFactoryTests
{
    [Fact]
    public void Without_a_uri_the_host_settings_are_used_without_tls()
    {
        var factory = RabbitMqConsumerService.CreateConnectionFactory(new RabbitMqSettings
        {
            HostName = "rabbitmq",
            Port = 5672,
            UserName = "cato_user",
            Password = "secret"
        });

        Assert.Equal("rabbitmq", factory.HostName);
        Assert.Equal(5672, factory.Port);
        Assert.Equal("cato_user", factory.UserName);
        Assert.Equal("secret", factory.Password);
        Assert.False(factory.Ssl.Enabled);
    }

    [Fact]
    public void An_amqps_uri_overrides_the_host_settings_and_turns_tls_on()
    {
        var factory = RabbitMqConsumerService.CreateConnectionFactory(new RabbitMqSettings
        {
            HostName = "rabbitmq",
            Port = 5672,
            UserName = "cato_user",
            Password = "secret",
            Uri = "amqps://mq_user:p%40ss%2Fword@b-1234.mq.eu-central-1.on.aws:5671/"
        });

        Assert.Equal("b-1234.mq.eu-central-1.on.aws", factory.HostName);
        Assert.Equal(5671, factory.Port);
        Assert.Equal("mq_user", factory.UserName);
        Assert.Equal("p@ss/word", factory.Password);
        Assert.True(factory.Ssl.Enabled);
        Assert.Equal("b-1234.mq.eu-central-1.on.aws", factory.Ssl.ServerName);
        Assert.Equal(SslPolicyErrors.None, factory.Ssl.AcceptablePolicyErrors);
    }

    [Fact]
    public void An_amqps_uri_without_a_port_uses_the_tls_port()
    {
        var factory = RabbitMqConsumerService.CreateConnectionFactory(new RabbitMqSettings
        {
            Uri = "amqps://mq_user:secret@broker.example"
        });

        Assert.Equal(5671, factory.Port);
        Assert.True(factory.Ssl.Enabled);
    }

    [Fact]
    public void A_uri_without_credentials_keeps_the_user_and_password_settings()
    {
        var factory = RabbitMqConsumerService.CreateConnectionFactory(new RabbitMqSettings
        {
            UserName = "cato_user",
            Password = "secret",
            Uri = "amqp://rabbitmq:5673/"
        });

        Assert.Equal("rabbitmq", factory.HostName);
        Assert.Equal(5673, factory.Port);
        Assert.Equal("cato_user", factory.UserName);
        Assert.Equal("secret", factory.Password);
        Assert.False(factory.Ssl.Enabled);
    }

    [Fact]
    public void Recovery_is_on_and_the_connection_carries_the_client_name()
    {
        var factory = RabbitMqConsumerService.CreateConnectionFactory(new RabbitMqSettings
        {
            ClientProvidedName = "cato-worker"
        });

        Assert.True(factory.AutomaticRecoveryEnabled);
        Assert.True(factory.TopologyRecoveryEnabled);
        Assert.Equal("cato-worker", factory.ClientProvidedName);
    }

    [Fact]
    public void The_client_name_defaults_to_cato_backend() =>
        Assert.Equal("cato-backend", new RabbitMqSettings().ClientProvidedName);
}
