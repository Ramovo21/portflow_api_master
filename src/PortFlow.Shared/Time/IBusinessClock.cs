using NodaTime;

namespace PortFlow.Shared.Time;

/// <summary>Đồng hồ nghiệp vụ. Không dùng <c>DateTime.Now</c>/<c>SystemClock</c> trực tiếp trong nghiệp vụ.</summary>
public interface IBusinessClock
{
    static readonly DateTimeZone DefaultZone = DateTimeZoneProviders.Tzdb["Asia/Ho_Chi_Minh"];

    Instant Now();

    DateTimeZone Zone { get; }

    LocalDate Today() => Now().InZone(Zone).Date;
}
