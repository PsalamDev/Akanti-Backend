using Microsoft.EntityFrameworkCore;
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
    public DbSet<AIRecommendation> AIRecommendations => Set<AIRecommendation>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<PasswordResetToken> PasswordResetTokens => Set<PasswordResetToken>();
    public DbSet<EmailVerification> EmailVerifications => Set<EmailVerification>();

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

        modelBuilder.Entity<AIRecommendation>()
            .HasOne(a => a.User)
            .WithMany(u => u.AIRecommendations)
            .HasForeignKey(a => a.UserId);

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
    }
}
