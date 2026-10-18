using Microsoft.Extensions.DependencyInjection;
using RabbitMQ.Client;

namespace PortFlow.Shared.Messaging;

/// <summary>
/// Topology chung: topic exchange <c>portflow.events</c> + dead-letter exchange <c>portflow.dlx</c>.
/// Module tiêu thụ khai báo queue của mình bằng <see cref="AddConsumerQueue"/>; mỗi queue có <c>&lt;queue&gt;.dlq</c>.
/// </summary>
public static class RabbitTopology
{
    public const string EventsExchange = "portflow.events";
    public const string DeadLetterExchange = "portflow.dlx";

    public static IServiceCollection AddConsumerQueue(this IServiceCollection services, string queueName,
        params string[] bindingPatterns)
    {
        services.AddSingleton(new ConsumerQueue(queueName, bindingPatterns));
        return services;
    }

    internal static async Task DeclareAsync(IChannel channel, IEnumerable<ConsumerQueue> queues, CancellationToken ct)
    {
        await channel.ExchangeDeclareAsync(EventsExchange, ExchangeType.Topic, durable: true, autoDelete: false,
            cancellationToken: ct);
        await channel.ExchangeDeclareAsync(DeadLetterExchange, ExchangeType.Direct, durable: true, autoDelete: false,
            cancellationToken: ct);
        foreach (var queue in queues)
        {
            var dlqName = queue.Name + ".dlq";
            await channel.QueueDeclareAsync(queue.Name, durable: true, exclusive: false, autoDelete: false,
                arguments: new Dictionary<string, object?>
                {
                    ["x-dead-letter-exchange"] = DeadLetterExchange,
                    ["x-dead-letter-routing-key"] = dlqName,
                }, cancellationToken: ct);
            await channel.QueueDeclareAsync(dlqName, durable: true, exclusive: false, autoDelete: false,
                cancellationToken: ct);
            await channel.QueueBindAsync(dlqName, DeadLetterExchange, dlqName, cancellationToken: ct);
            foreach (var pattern in queue.BindingPatterns)
            {
                await channel.QueueBindAsync(queue.Name, EventsExchange, pattern, cancellationToken: ct);
            }
        }
    }
}

public sealed record ConsumerQueue(string Name, IReadOnlyList<string> BindingPatterns);
