namespace Akanti.API.DTOs.Admin;

public class AdminDashboardDto
{
    public int TotalUsers { get; set; }
    public int ActiveUsers { get; set; }
    public int NewUsersThisMonth { get; set; }
    public decimal TotalIncome { get; set; }
    public decimal TotalExpenses { get; set; }
    public int TotalIncomes { get; set; }
    public int TotalExpensesCount { get; set; }
    public int TotalBudgets { get; set; }
    public int TotalDebts { get; set; }
}

public class AdminUserDto
{
    public int Id { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? PhoneNumber { get; set; }
    public string UserType { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public bool IsAdmin { get; set; }
    public DateTime CreatedAt { get; set; }
    public decimal TotalIncome { get; set; }
    public decimal TotalExpenses { get; set; }
    public int IncomeCount { get; set; }
    public int ExpenseCount { get; set; }
    public int BudgetCount { get; set; }
    public int DebtCount { get; set; }
}

public class AdminUserDetailDto
{
    public int Id { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? PhoneNumber { get; set; }
    public string UserType { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public bool IsAdmin { get; set; }
    public DateTime CreatedAt { get; set; }
    public decimal TotalIncome { get; set; }
    public decimal TotalExpenses { get; set; }
    public decimal SavingsRate { get; set; }
    public List<AdminIncomeDto> Incomes { get; set; } = new();
    public List<AdminExpenseDto> Expenses { get; set; } = new();
    public List<AdminBudgetDto> Budgets { get; set; } = new();
    public List<AdminDebtDto> Debts { get; set; } = new();
}

public class AdminIncomeDto
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string? CategoryName { get; set; }
    public DateTime Date { get; set; }
    public string Frequency { get; set; } = string.Empty;
}

public class AdminExpenseDto
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string? CategoryName { get; set; }
    public DateTime Date { get; set; }
    public string Frequency { get; set; } = string.Empty;
}

public class AdminBudgetDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public decimal Spent { get; set; }
    public string Period { get; set; } = string.Empty;
    public bool IsActive { get; set; }
}

public class AdminDebtDto
{
    public int Id { get; set; }
    public string Description { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public decimal AmountPaid { get; set; }
    public string Type { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime? DueDate { get; set; }
}

public class AdminAuditLogDto
{
    public int Id { get; set; }
    public string? UserEmail { get; set; }
    public string Action { get; set; } = string.Empty;
    public string EntityName { get; set; } = string.Empty;
    public int? EntityId { get; set; }
    public string? IpAddress { get; set; }
    public DateTime CreatedAt { get; set; }
}
