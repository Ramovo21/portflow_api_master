using System.Net;

namespace PortFlow.Shared.Domain;

/// <summary>Cách khai báo mã lỗi thay cho enum Java implements ErrorCode: <c>public static readonly ErrorCode X = new("X", ...)</c>.</summary>
public sealed record ErrorCode(string Code, HttpStatusCode Status, string Title) : IErrorCode
{
    public override string ToString() => Code;
}
