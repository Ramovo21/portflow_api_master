using Cronos;
using Microsoft.Extensions.DependencyInjection;

namespace PortFlow.Shared.Scheduling;

/// <summary>Lịch chạy: cron 6 trường có giây như Spring (<c>"0 15 * * * *"</c>) hoặc fixed delay.</summary>
public sealed record JobSchedule
{
    private JobSchedule(CronExpression? cron, TimeSpan? fixedDelay)
    {
        Cron = cron;
        FixedDelay = fixedDelay;
    }

    public CronExpression? Cron { get; }

    public TimeSpan? FixedDelay { get; }

    public static JobSchedule ByCron(string expression) =>
        new(CronExpression.Parse(expression, CronFormat.IncludeSeconds), null);

    public static JobSchedule WithFixedDelay(TimeSpan delay) => new(null, delay);

    internal TimeSpan DelayFrom(DateTimeOffset now)
    {
        if (FixedDelay is { } delay)
        {
            return delay;
        }
        var next = Cron!.GetNextOccurrence(now, TimeZoneInfo.Local) ?? now.AddYears(1);
        return next - now;
    }
}

/// <summary>Job định kỳ (= <c>@Scheduled</c> + <c>@SchedulerLock</c>): chỉ một instance chạy tại một thời điểm.</summary>
public interface IScheduledJob
{
    /// <summary>Tên khóa trong <c>shared.shedlock</c>.</summary>
    string Name { get; }

    JobSchedule Schedule { get; }

    /// <summary>Chờ trước lần chạy đầu (= initialDelay); null = theo lịch.</summary>
    TimeSpan? InitialDelay => null;

    TimeSpan LockAtMostFor => TimeSpan.FromMinutes(10);

    TimeSpan LockAtLeastFor => TimeSpan.Zero;

    Task RunAsync(CancellationToken cancellationToken);
}

public static class ScheduledJobs
{
    /// <summary>Đăng ký job (scoped, mỗi lần chạy một scope mới). Chỉ chạy khi <c>Portflow:Jobs:Enabled=true</c>.</summary>
    public static IServiceCollection AddScheduledJob<TJob>(this IServiceCollection services)
        where TJob : class, IScheduledJob
    {
        services.AddScoped<TJob>();
        services.AddSingleton(new ScheduledJobRegistration(typeof(TJob)));
        return services;
    }
}

public sealed record ScheduledJobRegistration(Type JobType);
