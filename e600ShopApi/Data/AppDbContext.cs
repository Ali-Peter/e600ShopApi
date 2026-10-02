using e600ShopApi.Domain;
using Microsoft.EntityFrameworkCore;

namespace e600ShopApi.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Product> Products => Set<Product>();

    public DbSet<User> Users => Set<User>();

    public DbSet<Order> Orders => Set<Order>();

    public DbSet<OrderItem> OrderItems => Set<OrderItem>();

    public DbSet<Address> Addresses => Set<Address>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Product>(entity =>
        {
            entity.Property(p => p.Name).IsRequired().HasMaxLength(200);
            entity.Property(p => p.Description).IsRequired().HasMaxLength(4000);
            entity.Property(p => p.Price).HasPrecision(18, 2);
            entity.Property(p => p.ImageUrl).HasMaxLength(2048);
            entity.Property(p => p.Category).IsRequired().HasMaxLength(100);
            entity.Property(p => p.CreatedAt).HasDefaultValueSql("now()");
            entity.HasIndex(p => p.Category);
        });

        modelBuilder.Entity<User>(entity =>
        {
            entity.Property(u => u.Email).IsRequired().HasMaxLength(320);
            entity.Property(u => u.FirstName).IsRequired().HasMaxLength(100);
            entity.Property(u => u.LastName).IsRequired().HasMaxLength(100);
            entity.Property(u => u.GoogleSubjectId).HasMaxLength(64);
            entity.Property(u => u.CreatedAt).HasDefaultValueSql("now()");
            entity.HasIndex(u => u.Email).IsUnique();
            entity.HasIndex(u => u.GoogleSubjectId).IsUnique();
        });

        modelBuilder.Entity<Order>(entity =>
        {
            entity.Property(o => o.TotalAmount).HasPrecision(18, 2);
            entity.Property(o => o.Status)
                .HasConversion<string>()
                .HasMaxLength(32);
            entity.Property(o => o.CreatedAt).HasDefaultValueSql("now()");
            entity.HasIndex(o => o.UserId);
            entity
                .HasOne(o => o.User)
                .WithMany(u => u.Orders)
                .HasForeignKey(o => o.UserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<OrderItem>(entity =>
        {
            entity.Property(oi => oi.UnitPrice).HasPrecision(18, 2);
            entity.Property(oi => oi.Subtotal).HasPrecision(18, 2);
            entity.HasIndex(oi => oi.OrderId);
            entity.HasIndex(oi => oi.ProductId);
            entity.ToTable(table =>
                table.HasCheckConstraint("CK_OrderItem_Quantity", "\"Quantity\" > 0"));
            entity
                .HasOne(oi => oi.Order)
                .WithMany(o => o.Items)
                .HasForeignKey(oi => oi.OrderId)
                .OnDelete(DeleteBehavior.Cascade);
            entity
                .HasOne(oi => oi.Product)
                .WithMany()
                .HasForeignKey(oi => oi.ProductId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Address>(entity =>
        {
            entity.Property(a => a.FullName).IsRequired().HasMaxLength(200);
            entity.Property(a => a.AddressLine).IsRequired().HasMaxLength(300);
            entity.Property(a => a.City).IsRequired().HasMaxLength(100);
            entity.Property(a => a.State).IsRequired().HasMaxLength(100);
            entity.Property(a => a.Country).IsRequired().HasMaxLength(100);
            entity.Property(a => a.PhoneNumber).IsRequired().HasMaxLength(40);
            entity.HasIndex(a => a.UserId);
            entity
                .HasOne(a => a.User)
                .WithMany(u => u.Addresses)
                .HasForeignKey(a => a.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
