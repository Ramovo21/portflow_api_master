namespace PortFlow.Tests.Support;

/// <summary>Tương đương Awaitility: chờ điều kiện đúng (until) hoặc giữ đúng suốt một khoảng (during).</summary>
public static class Eventually
{
    private static readonly TimeSpan Poll = TimeSpan.FromMilliseconds(100);

    public static async Task UntilAsync(Func<Task<bool>> condition, TimeSpan atMost, TimeSpan? pollDelay = null)
    {
        if (pollDelay is { } delay)
        {
            await Task.Delay(delay);
        }
        var deadline = DateTime.UtcNow + atMost;
        while (true)
        {
            if (await condition())
            {
                return;
            }
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException($"Condition not met within {atMost}");
            }
            await Task.Delay(Poll);
        }
    }

    public static async Task DuringAsync(Func<Task<bool>> condition, TimeSpan during)
    {
        var until = DateTime.UtcNow + during;
        while (DateTime.UtcNow < until)
        {
            if (!await condition())
            {
                throw new InvalidOperationException($"Condition did not hold during {during}");
            }
            await Task.Delay(Poll);
        }
    }
}
