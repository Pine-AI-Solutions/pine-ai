using Microsoft.EntityFrameworkCore;
using PineAI.Core.Entities;

namespace PineAI.Persistence.Services;

public class PineAIDbContext : DbContext
{
    public PineAIDbContext(DbContextOptions<PineAIDbContext> options) 
        : base(options) { }

    public DbSet<Customer> Customer { get; set; }
    public DbSet<SmsLog> SmsLog { get; set; }
    public DbSet<SmsSendJob> SmsSendJob { get; set; }
    public DbSet<SmsSendJobPart> SmsSendJobPart { get; set; }
    public DbSet<OrderStatus> OrderStatus { get; set; }
    public DbSet<CustomerOrder> CustomerOrder { get; set; }
    public DbSet<ApiKey> ApiKey { get; set; }
    public DbSet<BotChatMessage> BotChatMessage { get; set; }
    public DbSet<MenuLink> MenuLink { get; set; }
    public DbSet<UserMenuLink> UserMenuLink { get; set; }
    public DbSet<Product> Product { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<SmsSendJobPart>()
            .HasIndex(p => new { p.Status, p.ScheduledAt })
            .HasDatabaseName("IX_SmsSendJobPart_Status_ScheduledAt");

        modelBuilder.Entity<OrderStatus>()
            .HasIndex(o => o.Code)
            .IsUnique()
            .HasDatabaseName("IX_OrderStatus_Code");

        modelBuilder.Entity<CustomerOrder>()
            .HasIndex(o => o.OrderCode)
            .IsUnique()
            .HasDatabaseName("IX_CustomerOrder_OrderCode");

        modelBuilder.Entity<CustomerOrder>()
            .HasOne(o => o.Customer)
            .WithMany(c => c.CustomerOrders)
            .HasForeignKey(o => o.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<CustomerOrder>()
            .HasOne(o => o.OrderStatus)
            .WithMany(s => s.CustomerOrders)
            .HasForeignKey(o => o.OrderStatusId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<ApiKey>()
            .HasIndex(k => k.Key)
            .IsUnique()
            .HasDatabaseName("IX_ApiKey_Key");

        modelBuilder.Entity<BotChatMessage>()
            .HasIndex(m => m.BaleUsername)
            .HasDatabaseName("IX_BotChatMessage_BaleUsername");

        modelBuilder.Entity<BotChatMessage>()
            .HasIndex(m => m.SentAt)
            .HasDatabaseName("IX_BotChatMessage_SentAt");

        // ── MenuLink ──────────────────────────────────────────────────────────

        modelBuilder.Entity<UserMenuLink>()
            .HasOne(um => um.MenuLink)
            .WithMany(m => m.UserMenuLinks)
            .HasForeignKey(um => um.MenuLinkId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<UserMenuLink>()
            .HasIndex(um => new { um.UserId, um.MenuLinkId })
            .IsUnique()
            .HasDatabaseName("IX_UserMenuLink_UserId_MenuLinkId");

        // Seed default menu links
        modelBuilder.Entity<MenuLink>().HasData(
            new MenuLink { Id = 1, Title = "خانه", Url = "/", IconName = "bi-house-door-fill", SectionLabel = "منو", DisplayOrder = 1 },
            new MenuLink { Id = 2, Title = "افزودن مشتری", Url = "/customer/add", IconName = "bi-person-plus-fill", SectionLabel = "مشتریان", DisplayOrder = 2 },
            new MenuLink { Id = 3, Title = "ورود از اکسل", Url = "/customer/import", IconName = "bi-file-earmark-excel-fill", SectionLabel = "مشتریان", DisplayOrder = 3 },
            new MenuLink { Id = 4, Title = "وضعیت‌های سفارش", Url = "/order/statuses", IconName = "bi-tags-fill", SectionLabel = "سفارشات", DisplayOrder = 4 },
            new MenuLink { Id = 5, Title = "بارکد پستی", Url = "/order/ananas-tracking", IconName = "bi-box-seam-fill", SectionLabel = "سفارشات", DisplayOrder = 5 },
            new MenuLink { Id = 6, Title = "کلیدهای API", Url = "/settings/apikeys", IconName = "bi-key-fill", SectionLabel = "تنظیمات", DisplayOrder = 6 },
            new MenuLink { Id = 7, Title = "مکالمات کاربران", Url = "/bot/conversations", IconName = "bi-chat-dots-fill", SectionLabel = "ربات بله", DisplayOrder = 7 }
        );
    }
}
