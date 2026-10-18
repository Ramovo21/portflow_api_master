using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PortFlow.Shared.Config;
using RabbitMQ.Client;

namespace PortFlow.Shared.Messaging;

/// <summary>
/// Một kết nối RabbitMQ cho cả tiến trình, tạo lười (broker chưa chạy thì app vẫn khởi động được).
/// Mỗi lần tạo kết nối sẽ khai báo lại topology (= RabbitAdmin của Spring); client tự phục hồi kết nối sau đó.
/// </summary>
public sealed class RabbitConnectionProvider(
    IOptions<PortFlowOptions> options,
    IEnumerable<ConsumerQueue> queues,
    ILogger<RabbitConnectionProvider> log) : IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IConnection? _connection;

    public bool IsOpen => _connection?.IsOpen == true;

    public async Task<IConnection> GetConnectionAsync(CancellationToken ct)
    {
        if (_connection is { IsOpen: true } open)
        {
            return open;
        }
        await _gate.WaitAsync(ct);
        try
        {
            if (_connection is { IsOpen: true } existing)
            {
                return existing;
            }
            if (_connection is not null)
            {
                await _connection.DisposeAsync();
                _connection = null;
            }
            var rabbit = options.Value.Rabbit;
            var factory = new ConnectionFactory
            {
                HostName = rabbit.Host,
                Port = rabbit.Port,
                UserName = rabbit.Username,
                Password = rabbit.Password,
                ClientProvidedName = "portflow",
                AutomaticRecoveryEnabled = true,
                TopologyRecoveryEnabled = true,
            };
            var connection = await factory.CreateConnectionAsync(ct);
            await using (var channel = await connection.CreateChannelAsync(cancellationToken: ct))
            {
                await RabbitTopology.DeclareAsync(channel, queues, ct);
            }
            log.LogInformation("Connected to RabbitMQ {Host}:{Port}", rabbit.Host, rabbit.Port);
            _connection = connection;
            return connection;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection is not null)
        {
            await _connection.DisposeAsync();
        }
        _gate.Dispose();
    }
}
