namespace PortFlow.Identity.Api;

/// <summary>
/// Danh mục permission (F01 §8.2). Hằng chuỗi để dùng trong <c>[RequirePermission(Permission.X)]</c>;
/// quyền được gán cho role trong bảng <c>identity.role</c>.
/// </summary>
public static class Permission
{
    public const string CUSTOMER_READ = "Customer.Read";
    public const string CUSTOMER_CREATE = "Customer.Create";
    public const string CUSTOMER_UPDATE = "Customer.Update";
    public const string CUSTOMER_SUSPEND = "Customer.Suspend";
    public const string CONTRACT_READ = "Contract.Read";
    public const string CONTRACT_CREATE = "Contract.Create";
    public const string CONTRACT_UPDATE = "Contract.Update";
    public const string CONTRACT_SUBMIT = "Contract.Submit";
    public const string CONTRACT_CANCEL = "Contract.Cancel";
    public const string CONTRACT_CLONE = "Contract.Clone";
    public const string ADDENDUM_CREATE = "Addendum.Create";
    public const string ADDENDUM_SUBMIT = "Addendum.Submit";
    public const string SERVICE_CATALOG_MANAGE = "ServiceCatalog.Manage";
    public const string PRICE_LIST_READ = "PriceList.Read";
    public const string PRICE_LIST_CREATE = "PriceList.Create";
    public const string PRICE_LIST_SUBMIT = "PriceList.Submit";
    public const string VOLUME_READ = "Volume.Read";
    public const string VOLUME_RECORD = "Volume.Record";
    public const string VOLUME_LOCK = "Volume.Lock";
    public const string VOLUME_REOPEN_PERIOD = "Volume.ReopenPeriod";
    public const string VOLUME_IMPORT = "Volume.Import";
    public const string PAYMENT_SHEET_READ = "PaymentSheet.Read";
    public const string PAYMENT_SHEET_CREATE = "PaymentSheet.Create";
    public const string PAYMENT_SHEET_RECONCILE = "PaymentSheet.Reconcile";
    public const string PAYMENT_SHEET_SUBMIT = "PaymentSheet.Submit";
    public const string PAYMENT_SHEET_SEND_ESIGN = "PaymentSheet.SendESign";
    public const string PAYMENT_SHEET_PUBLISH = "PaymentSheet.Publish";
    public const string PAYMENT_SHEET_ADJUST = "PaymentSheet.Adjust";
    public const string WORKFLOW_ACT = "Workflow.Act";
    public const string WORKFLOW_ADMIN = "Workflow.Admin";
    public const string ESIGN_READ = "ESign.Read";
    public const string ESIGN_CANCEL = "ESign.Cancel";
    public const string ESIGN_RETRY = "ESign.Retry";
    public const string AUDIT_READ = "Audit.Read";
    public const string AUDIT_READ_ALL = "Audit.ReadAll";
    public const string AUDIT_EXPORT = "Audit.Export";
    public const string USER_ADMIN = "User.Admin";
    public const string ROLE_ADMIN = "Role.Admin";
    public const string DOCUMENT_TYPE_ADMIN = "DocumentType.Admin";
    public const string DELEGATION_MANAGE = "Delegation.Manage";
    public const string AI_USE = "AI.Use";
    public const string SYSTEM_ADMIN = "System.Admin";

    /// <summary>Mọi permission theo thứ tự khai báo (= <c>Permission.values()</c>).</summary>
    public static readonly IReadOnlyList<string> All =
    [
        CUSTOMER_READ, CUSTOMER_CREATE, CUSTOMER_UPDATE, CUSTOMER_SUSPEND,
        CONTRACT_READ, CONTRACT_CREATE, CONTRACT_UPDATE, CONTRACT_SUBMIT, CONTRACT_CANCEL, CONTRACT_CLONE,
        ADDENDUM_CREATE, ADDENDUM_SUBMIT, SERVICE_CATALOG_MANAGE,
        PRICE_LIST_READ, PRICE_LIST_CREATE, PRICE_LIST_SUBMIT,
        VOLUME_READ, VOLUME_RECORD, VOLUME_LOCK, VOLUME_REOPEN_PERIOD, VOLUME_IMPORT,
        PAYMENT_SHEET_READ, PAYMENT_SHEET_CREATE, PAYMENT_SHEET_RECONCILE, PAYMENT_SHEET_SUBMIT,
        PAYMENT_SHEET_SEND_ESIGN, PAYMENT_SHEET_PUBLISH, PAYMENT_SHEET_ADJUST,
        WORKFLOW_ACT, WORKFLOW_ADMIN, ESIGN_READ, ESIGN_CANCEL, ESIGN_RETRY,
        AUDIT_READ, AUDIT_READ_ALL, AUDIT_EXPORT,
        USER_ADMIN, ROLE_ADMIN, DOCUMENT_TYPE_ADMIN, DELEGATION_MANAGE, AI_USE, SYSTEM_ADMIN,
    ];

    private static readonly HashSet<string> Known = [.. All];

    public static bool IsKnown(string code) => Known.Contains(code);
}
