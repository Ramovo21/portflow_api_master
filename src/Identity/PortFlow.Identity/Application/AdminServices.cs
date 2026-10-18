using Microsoft.EntityFrameworkCore;
using PortFlow.Identity.Api;
using PortFlow.Identity.Domain;
using PortFlow.Identity.Infrastructure.Persistence;
using PortFlow.Identity.Infrastructure.Security;
using PortFlow.Shared.Domain;
using PortFlow.Shared.Id;
using PortFlow.Shared.Persistence;
using PortFlow.Shared.Security;
using PortFlow.Shared.Time;

namespace PortFlow.Identity.Application;

public sealed record DepartmentCommand(string Code, string Name, Guid? HeadUserId, Guid? ParentId);

public sealed class DepartmentService(
    IdentityDbContext db,
    ITransactionRunner tx,
    ICurrentUserProvider currentUser,
    IBusinessClock clock)
{
    public Task<List<Department>> FindAllAsync(CancellationToken ct) =>
        db.Departments.OrderBy(d => d.Code).ToListAsync(ct);

    public async Task<Department> GetAsync(Guid id, CancellationToken ct) =>
        await db.Departments.SingleOrDefaultAsync(d => d.Id == id, ct)
        ?? throw new DomainException(SysErrorCode.SYS_NOT_FOUND, "Department not found");

    public Task<Department> CreateAsync(DepartmentCommand command, CancellationToken ct) =>
        tx.ExecuteAsync(async token =>
        {
            await ValidateAsync(command, null, token);
            var department = new Department(UuidV7.Next(), command.Code.Trim(), command.Name.Trim(), command.HeadUserId,
                command.ParentId, clock.Now(), currentUser.Current().Id);
            db.Departments.Add(department);
            await db.SaveChangesAsync(token);
            return department;
        }, ct);

    public Task<Department> UpdateAsync(Guid id, DepartmentCommand command, CancellationToken ct) =>
        tx.ExecuteAsync(async token =>
        {
            var department = await GetAsync(id, token);
            await ValidateAsync(command, id, token);
            department.Update(command.Code.Trim(), command.Name.Trim(), command.HeadUserId, command.ParentId,
                clock.Now(), currentUser.Current().Id);
            await db.SaveChangesAsync(token);
            return department;
        }, ct);

    private async Task ValidateAsync(DepartmentCommand command, Guid? selfId, CancellationToken ct)
    {
        if (command.HeadUserId is { } head && !await db.Users.AnyAsync(u => u.Id == head, ct))
        {
            throw new DomainException(SysErrorCode.SYS_VALIDATION, "Department head does not exist");
        }
        if (command.ParentId is { } parent)
        {
            if (parent == selfId)
            {
                throw new DomainException(SysErrorCode.SYS_VALIDATION, "A department cannot be its own parent");
            }
            await GetAsync(parent, ct);
        }
    }
}

public sealed class DocumentTypeService(IdentityDbContext db, ITransactionRunner tx) : IDocumentTypeQuery
{
    public Task<List<DocumentType>> FindAllAsync(CancellationToken ct) =>
        db.DocumentTypes.OrderBy(t => t.Code).ToListAsync(ct);

    public async Task<DocumentTypeInfo> GetAsync(string code, CancellationToken ct = default)
    {
        var type = await FindAsync(code, ct);
        return new DocumentTypeInfo(type.Code, type.Name, type.RequiresEsign, type.WorkflowKey, type.Active);
    }

    public async Task<DocumentType> FindAsync(string code, CancellationToken ct) =>
        await db.DocumentTypes.SingleOrDefaultAsync(t => t.Code == code, ct)
        ?? throw new DomainException(SysErrorCode.SYS_NOT_FOUND, "Document type not found " + code);

    public Task<bool> ExistsAsync(string code, CancellationToken ct) => db.DocumentTypes.AnyAsync(t => t.Code == code, ct);

    public Task<DocumentType> UpdateAsync(string code, string name, bool requiresESign, string workflowKey, bool active,
        string submitRequirementsJson, CancellationToken ct) =>
        tx.ExecuteAsync(async token =>
        {
            var type = await FindAsync(code, token);
            type.Update(name, requiresESign, workflowKey, active, submitRequirementsJson);
            await db.SaveChangesAsync(token);
            return type;
        }, ct);
}

public sealed class RoleAdminService(
    IdentityDbContext db,
    ITransactionRunner tx,
    IRolePermissions cache,
    CurrentUserCache currentUsers)
{
    public Task<List<Role>> FindAllAsync(CancellationToken ct) => db.Roles.OrderBy(r => r.Code).ToListAsync(ct);

    public async Task<Role> GetAsync(string code, CancellationToken ct) =>
        await db.Roles.SingleOrDefaultAsync(r => r.Code == code, ct)
        ?? throw new DomainException(SysErrorCode.SYS_NOT_FOUND, "Role not found " + code);

    public Task<Role> UpdateAsync(string code, string name, IReadOnlySet<string> permissions, DataScope dataScope,
        CancellationToken ct) =>
        tx.ExecuteAsync(async token =>
        {
            var role = await GetAsync(code, token);
            var unknown = permissions.FirstOrDefault(p => !Permission.IsKnown(p));
            if (unknown is not null)
            {
                throw new DomainException(SysErrorCode.SYS_VALIDATION, "Invalid permission: " + unknown);
            }
            role.Update(name, permissions, dataScope);
            await db.SaveChangesAsync(token);
            cache.EvictAll();
            currentUsers.EvictAll();
            return role;
        }, ct);
}
