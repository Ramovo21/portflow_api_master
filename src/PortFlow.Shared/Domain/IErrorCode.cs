using System.Net;

namespace PortFlow.Shared.Domain;

/// <summary>Mã lỗi nghiệp vụ. Mỗi module khai báo một lớp mã lỗi riêng với tiền tố của mình (SYS_, CUS_, CTR_...).</summary>
public interface IErrorCode
{
    string Code { get; }

    HttpStatusCode Status { get; }

    string Title => Code;
}
