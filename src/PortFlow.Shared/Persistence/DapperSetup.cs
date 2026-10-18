using System.Data;
using Dapper;
using NodaTime;

namespace PortFlow.Shared.Persistence;

/// <summary>Cho Dapper truyền/đọc kiểu NodaTime; Npgsql (đã bật UseNodaTime) lo phần chuyển đổi thật.</summary>
internal static class DapperSetup
{
    private static int _registered;

    public static void Register()
    {
        if (Interlocked.Exchange(ref _registered, 1) == 1)
        {
            return;
        }
        SqlMapper.AddTypeHandler(new PassThroughHandler<Instant>());
        SqlMapper.AddTypeHandler(new PassThroughHandler<LocalDate>());
        SqlMapper.AddTypeHandler(new PassThroughHandler<OffsetDateTime>());
    }

    private sealed class PassThroughHandler<T> : SqlMapper.TypeHandler<T>
    {
        public override void SetValue(IDbDataParameter parameter, T? value) => parameter.Value = value;

        public override T Parse(object value) => (T)value;
    }
}
