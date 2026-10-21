using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Akanti.API.Models;

namespace Akanti.API.Data;

public class ApplicationDbContext : DbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options) { }

    public DbSet<User> Users => Set<User>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Income> Incomes => Set<Income>();
    public DbSet<Expense> Expenses => Set<Expense>();
    public DbSet<Budget> Budgets => Set<Budget>();
    public DbSet<Debt> Debts => Set<Debt>();
    public DbSet<Transaction> Transactions => Set<Transaction>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<PasswordResetToken> PasswordResetTokens => Set<PasswordResetToken>();
    public DbSet<EmailVerification> EmailVerifications => Set<EmailVerification>();

    // Invoicing / PayPal
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Invoice> Invoices => Set<Invoice>();
    public DbSet<InvoiceItem> InvoiceItems => Set<InvoiceItem>();
    public DbSet<PayPalWebhookEvent> PayPalWebhookEvents => Set<PayPalWebhookEvent>();
    public DbSet<InvoiceDraft> InvoiceDrafts => Set<InvoiceDraft>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<User>()
            .HasIndex(u => u.Email)
            .IsUnique();

        modelBuilder.Entity<Income>()
            .HasOne(i => i.User)
            .WithMany(u => u.Incomes)
            .HasForeignKey(i => i.UserId);

        modelBuilder.Entity<Income>()
            .HasOne(i => i.Category)
            .WithMany(c => c.Incomes)
            .HasForeignKey(i => i.CategoryId);

        modelBuilder.Entity<Expense>()
            .HasOne(e => e.User)
            .WithMany(u => u.Expenses)
            .HasForeignKey(e => e.UserId);

        modelBuilder.Entity<Expense>()
            .HasOne(e => e.Category)
            .WithMany(c => c.Expenses)
            .HasForeignKey(e => e.CategoryId);

        modelBuilder.Entity<Budget>()
            .HasOne(b => b.User)
            .WithMany(u => u.Budgets)
            .HasForeignKey(b => b.UserId);

        modelBuilder.Entity<Budget>()
            .HasOne(b => b.Category)
            .WithMany()
            .HasForeignKey(b => b.CategoryId);

        modelBuilder.Entity<Debt>()
            .HasOne(d => d.User)
            .WithMany(u => u.Debts)
            .HasForeignKey(d => d.UserId);

        modelBuilder.Entity<Notification>()
            .HasOne(n => n.User)
            .WithMany(u => u.Notifications)
            .HasForeignKey(n => n.UserId);

        modelBuilder.Entity<AuditLog>()
            .HasOne(a => a.User)
            .WithMany(u => u.AuditLogs)
            .HasForeignKey(a => a.UserId);

        modelBuilder.Entity<Transaction>()
            .HasOne(t => t.User)
            .WithMany()
            .HasForeignKey(t => t.UserId);

        modelBuilder.Entity<Transaction>()
            .HasOne(t => t.Income)
            .WithMany()
            .HasForeignKey(t => t.IncomeId);

        modelBuilder.Entity<Transaction>()
            .HasOne(t => t.Expense)
            .WithMany()
            .HasForeignKey(t => t.ExpenseId);

        modelBuilder.Entity<PasswordResetToken>()
            .HasOne(p => p.User)
            .WithMany()
            .HasForeignKey(p => p.UserId);

        modelBuilder.Entity<EmailVerification>()
            .HasOne(e => e.User)
            .WithMany()
            .HasForeignKey(e => e.UserId);

        // --- Invoicing / PayPal ---

        modelBuilder.Entity<Customer>()
            .HasOne(c => c.User)
            .WithMany()
            .HasForeignKey(c => c.UserId);

        modelBuilder.Entity<Invoice>()
            .HasOne(i => i.User)
            .WithMany()
            .HasForeignKey(i => i.UserId);

        modelBuilder.Entity<Invoice>()
            .HasOne(i => i.Customer)
            .WithMany(c => c.Invoices)
            .HasForeignKey(i => i.CustomerId);

        // No action on delete: an income record must not disappear if its invoice is removed.
        modelBuilder.Entity<Invoice>()
            .HasOne(i => i.Income)
            .WithMany()
            .HasForeignKey(i => i.IncomeId)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<InvoiceItem>()
            .HasOne(it => it.Invoice)
            .WithMany(i => i.Items)
            .HasForeignKey(it => it.InvoiceId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<InvoiceDraft>()
            .HasOne(d => d.User)
            .WithMany()
            .HasForeignKey(d => d.UserId);

        modelBuilder.Entity<InvoiceDraft>()
            .HasOne(d => d.Invoice)
            .WithMany()
            .HasForeignKey(d => d.InvoiceId)
            .OnDelete(DeleteBehavior.SetNull);

        // Invoice numbers are unique per user; soft-deleted rows would block reuse, so
        // the index is filtered to live rows.
        modelBuilder.Entity<Invoice>()
            .HasIndex(i => new { i.UserId, i.Number })
            .IsUnique()
            .HasFilter("\"IsDeleted\" = false");

        // PayPal's invoice id, when present, must map to exactly one local row.
        modelBuilder.Entity<Invoice>()
            .HasIndex(i => i.PayPalInvoiceId)
            .IsUnique()
            .HasFilter("\"PayPalInvoiceId\" IS NOT NULL");

        modelBuilder.Entity<Invoice>()
            .HasIndex(i => new { i.UserId, i.Status, i.DueDate, i.IsDeleted });

        modelBuilder.Entity<Customer>()
            .HasIndex(c => new { c.UserId, c.Email })
            .IsUnique()
            .HasFilter("\"IsDeleted\" = false");

        // Idempotency: a PayPal event id may only be ingested once.
        modelBuilder.Entity<PayPalWebhookEvent>()
            .HasIndex(e => e.PayPalEventId)
            .IsUnique();

        modelBuilder.Entity<PayPalWebhookEvent>()
            .HasIndex(e => new { e.ProcessedAt, e.ReceivedAt });

        modelBuilder.Entity<InvoiceDraft>()
            .HasIndex(d => new { d.UserId, d.Status, d.UpdatedAt });

        modelBuilder.Entity<Category>().HasData(
            new Category { Id = 1, Name = "Salary", Type = CategoryType.Income, IsDefault = true },
            new Category { Id = 2, Name = "Freelance", Type = CategoryType.Income, IsDefault = true },
            new Category { Id = 3, Name = "Investment", Type = CategoryType.Income, IsDefault = true },
            new Category { Id = 4, Name = "Other Income", Type = CategoryType.Income, IsDefault = true },
            new Category { Id = 5, Name = "Food & Dining", Type = CategoryType.Expense, IsDefault = true },
            new Category { Id = 6, Name = "Transportation", Type = CategoryType.Expense, IsDefault = true },
            new Category { Id = 7, Name = "Housing", Type = CategoryType.Expense, IsDefault = true },
            new Category { Id = 8, Name = "Utilities", Type = CategoryType.Expense, IsDefault = true },
            new Category { Id = 9, Name = "Entertainment", Type = CategoryType.Expense, IsDefault = true },
            new Category { Id = 10, Name = "Healthcare", Type = CategoryType.Expense, IsDefault = true },
            new Category { Id = 11, Name = "Shopping", Type = CategoryType.Expense, IsDefault = true },
            new Category { Id = 12, Name = "Education", Type = CategoryType.Expense, IsDefault = true },
            new Category { Id = 13, Name = "Other Expense", Type = CategoryType.Expense, IsDefault = true }
        );

        modelBuilder.Entity<Expense>()
            .HasIndex(e => new { e.UserId, e.Date, e.IsDeleted });

        modelBuilder.Entity<Income>()
            .HasIndex(i => new { i.UserId, i.Date, i.IsDeleted });

        modelBuilder.Entity<Budget>()
            .HasIndex(b => new { b.UserId, b.IsActive });

        modelBuilder.Entity<Debt>()
            .HasIndex(d => new { d.UserId, d.IsDeleted, d.Status, d.DueDate });

        modelBuilder.Entity<Notification>()
            .HasIndex(n => new { n.UserId, n.IsRead, n.CreatedAt });

        modelBuilder.Entity<AuditLog>()
            .HasIndex(a => new { a.CreatedAt });

        modelBuilder.Entity<EmailVerification>()
            .HasIndex(v => new { v.UserId, v.Code, v.IsUsed, v.ExpiresAt });

        modelBuilder.Entity<PasswordResetToken>()
            .HasIndex(t => new { t.UserId, t.Code, t.IsUsed, t.ExpiresAt });

        // PostgreSQL compatibility: the Npgsql provider maps DateTime to 'timestamp with
        // time zone' and only accepts UTC values. This app writes a mix of Kind=Utc
        // (DateTime.UtcNow) and Kind=Unspecified (date-only values from HTTP), so every
        // DateTime is normalised to UTC on write without changing the value. Because the
        // columns keep the provider's default mapping, date/time query constants
        // (e.g. DateTime.UtcNow) are translated consistently and cannot type-clash.
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var property in entityType.GetProperties())
            {
                var clrType = Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType;
                if (clrType != typeof(DateTime))
                    continue;

                if (property.ClrType == typeof(DateTime))
                {
                    property.SetValueConverter(new ValueConverter<DateTime, DateTime>(
                        v => DateTime.SpecifyKind(v, DateTimeKind.Utc),
                        v => v));
                }
                else
                {
                    property.SetValueConverter(new ValueConverter<DateTime?, DateTime?>(
                        v => v.HasValue ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : v,
                        v => v));
                }
            }
        }
    }
}
