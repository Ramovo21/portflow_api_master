using Microsoft.EntityFrameworkCore;
using PortFlow.Identity.Domain;
using PortFlow.Shared.Persistence;

namespace PortFlow.Identity.Infrastructure.Persistence;

/// <summary>Schema <c>identity</c> (tương đương các JpaRepository của bản Java). Bảng do Flyway tạo.</summary>
public sealed class IdentityDbContext(DbContextOptions<IdentityDbContext> options) : DbContext(options)
{
    public DbSet<AppUser> Users => Set<AppUser>();

    public DbSet<Department> Departments => Set<Department>();

    public DbSet<Role> Roles => Set<Role>();

    public DbSet<DocumentType> DocumentTypes => Set<DocumentType>();

    public DbSet<Delegation> Delegations => Set<Delegation>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("identity");

        modelBuilder.Entity<AppUser>(user =>
        {
            user.ToTable("app_user");
            user.HasKey(u => u.Id);
            user.Property(u => u.Version).IsConcurrencyToken();
            user.Ignore(u => u.RoleCodes);
            user.Ignore(u => u.IsFresh);
            user.Ignore(u => u.IsProvisioned);
            user.OwnsMany<UserRole>("_roles", role =>
            {
                role.ToTable("user_role");
                role.WithOwner().HasForeignKey("UserId");
                role.Property<Guid>("UserId").HasColumnName("user_id");
                role.Property(r => r.RoleCode).HasColumnName("role_code");
                role.HasKey("UserId", nameof(UserRole.RoleCode));
            });
            user.Navigation("_roles").AutoInclude();
        });

        modelBuilder.Entity<Department>(department =>
        {
            department.ToTable("department");
            department.HasKey(d => d.Id);
            department.Property(d => d.Version).IsConcurrencyToken();
        });

        modelBuilder.Entity<Role>(role =>
        {
            role.ToTable("role");
            role.HasKey(r => r.Code);
            role.Property(r => r.DataScope).HasConversion<string>();
            role.Property(r => r.Version).IsConcurrencyToken();
        });

        modelBuilder.Entity<DocumentType>(type =>
        {
            type.ToTable("document_type");
            type.HasKey(t => t.Code);
            type.Property(t => t.SubmitRequirements).HasColumnType("jsonb");
            type.Property(t => t.Version).IsConcurrencyToken();
        });

        modelBuilder.Entity<Delegation>(delegation =>
        {
            delegation.ToTable("delegation");
            delegation.HasKey(d => d.Id);
        });

        modelBuilder.ApplyAggregateRootConventions();
    }
}
