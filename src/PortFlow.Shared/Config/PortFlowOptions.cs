namespace PortFlow.Shared.Config;

/// <summary>Toàn bộ khóa <c>portflow.*</c> + <c>spring.datasource/rabbitmq</c> của bản Java, gom dưới section <c>Portflow</c>.</summary>
public sealed class PortFlowOptions
{
    public const string Section = "Portflow";

    public string BusinessZone { get; set; } = "Asia/Ho_Chi_Minh";

    public ApiOptions Api { get; set; } = new();

    public JobsOptions Jobs { get; set; } = new();

    public ServerOptions Server { get; set; } = new();

    public CorsOptions Cors { get; set; } = new();

    public DatasourceOptions Datasource { get; set; } = new();

    public RabbitOptions Rabbit { get; set; } = new();

    public StorageOptions Storage { get; set; } = new();

    public IdempotencyOptions Idempotency { get; set; } = new();

    public InboxOptions Inbox { get; set; } = new();

    public OutboxOptions Outbox { get; set; } = new();

    public sealed class ApiOptions
    {
        /// <summary>false ở profile worker: chỉ phục vụ /actuator.</summary>
        public bool Enabled { get; set; } = true;
    }

    public sealed class JobsOptions
    {
        public bool Enabled { get; set; }
    }

    public sealed class ServerOptions
    {
        public int Port { get; set; } = 8080;

        public int ManagementPort { get; set; } = 8081;
    }

    public sealed class CorsOptions
    {
        public string AllowedOrigins { get; set; } = "http://localhost:5173,http://localhost:4173";
    }

    public sealed class DatasourceOptions
    {
        /// <summary>Nhận cả URL JDBC (<c>jdbc:postgresql://host:5432/db</c>) lẫn connection string Npgsql.</summary>
        public string Url { get; set; } = "jdbc:postgresql://localhost:5432/portflow";

        public string Username { get; set; } = "portflow";

        public string Password { get; set; } = "portflow";

        public int MaximumPoolSize { get; set; } = 20;
    }

    public sealed class RabbitOptions
    {
        public string Host { get; set; } = "localhost";

        public int Port { get; set; } = 5672;

        public string Username { get; set; } = "guest";

        public string Password { get; set; } = "guest";

        /// <summary>Chờ broker xác nhận (publisher confirm) khi đẩy event.</summary>
        public TimeSpan ConfirmTimeout { get; set; } = TimeSpan.FromSeconds(10);

        public ListenerOptions Listener { get; set; } = new();
    }

    public sealed class ListenerOptions
    {
        /// <summary>false ở profile api: không tiêu thụ queue.</summary>
        public bool AutoStartup { get; set; } = true;

        public ushort Prefetch { get; set; } = 250;

        public int MaxAttempts { get; set; } = 4;

        public TimeSpan InitialInterval { get; set; } = TimeSpan.FromSeconds(1);

        public double Multiplier { get; set; } = 3.0;

        public TimeSpan MaxInterval { get; set; } = TimeSpan.FromSeconds(60);
    }

    public sealed class StorageOptions
    {
        public string Endpoint { get; set; } = "http://localhost:9000";

        public string AccessKey { get; set; } = "portflow";

        public string SecretKey { get; set; } = "portflow-secret";

        public string Bucket { get; set; } = "portflow";

        public TimeSpan PresignTtl { get; set; } = TimeSpan.FromMinutes(5);
    }

    public sealed class IdempotencyOptions
    {
        public TimeSpan Retention { get; set; } = TimeSpan.FromHours(48);

        public string CleanupCron { get; set; } = "0 15 * * * *";
    }

    public sealed class InboxOptions
    {
        public TimeSpan Retention { get; set; } = TimeSpan.FromDays(14);

        public string CleanupCron { get; set; } = "0 30 3 * * *";
    }

    public sealed class OutboxOptions
    {
        public TimeSpan ResubmitMinAge { get; set; } = TimeSpan.FromSeconds(30);

        public TimeSpan ResubmitInterval { get; set; } = TimeSpan.FromMinutes(1);

        public TimeSpan CompletedRetention { get; set; } = TimeSpan.FromDays(7);

        public string CleanupCron { get; set; } = "0 45 3 * * *";
    }
}
