namespace PortFlow.Shared.Security;

public interface ICurrentUserProvider
{
    ICurrentUser Current();
}

/// <summary>Mặc định khi chưa có module identity (F01) đăng ký <see cref="ICurrentUserProvider"/> của mình.</summary>
public sealed class SystemCurrentUserProvider : ICurrentUserProvider
{
    public ICurrentUser Current() => SystemUser.Instance;
}
