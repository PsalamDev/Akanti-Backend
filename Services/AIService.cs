using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Akanti.API.Data;
using Akanti.API.Models;

namespace Akanti.API.Services;

public class AIService : IAIService
{
    private readonly ApplicationDbContext _context;
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;

    public AIService(ApplicationDbContext context, HttpClient httpClient, IConfiguration configuration)
    {
        _context = context;
        _httpClient = httpClient;
        _configuration = configuration;
    }

    public async Task<string> GetSpendingAnalysisAsync(int userId, DateTime startDate, DateTime endDate)
    {
        var expenses = await _context.Expenses
            .Where(e => e.UserId == userId && e.Date >= startDate && e.Date <= endDate && !e.IsDeleted)
            .Include(e => e.Category)
            .ToListAsync();

        var totalExpenses = expenses.Sum(e => e.Amount);
        var categoryBreakdown = expenses
            .GroupBy(e => e.Category != null ? e.Category.Name : "Uncategorized")
            .Select(g => new { Category = g.Key, Amount = g.Sum(e => e.Amount), Count = g.Count() })
            .OrderByDescending(x => x.Amount)
            .ToList();

        var prompt = $"""
            Analyze the following spending data and provide insights:
            Total Expenses: ${totalExpenses:F2}
            Period: {startDate:MMM dd} - {endDate:MMM dd yyyy}
            Categories: {string.Join(", ", categoryBreakdown.Select(c => $"{c.Category}: ${c.Amount:F2} ({c.Count} transactions)"))}
            
            Provide a brief analysis of spending patterns, top categories, and areas where spending could be reduced.
            """;

        return await CallAIAsync("You are a helpful personal finance advisor AI.", prompt);
    }

    public async Task<string> GetBudgetRecommendationAsync(int userId, int budgetId)
    {
        var budget = await _context.Budgets
            .FirstOrDefaultAsync(b => b.Id == budgetId && b.UserId == userId);

        if (budget == null) return "Budget not found.";

        var percentageUsed = budget.Amount > 0 ? (budget.Spent / budget.Amount) * 100 : 0;

        var prompt = $"""
            Budget: {budget.Name}
            Budget Amount: ${budget.Amount:F2}
            Spent: ${budget.Spent:F2} ({percentageUsed:F1}%)
            Period: {budget.Period}
            
            Provide recommendations for staying within this budget.
            """;

        return await CallAIAsync("You are a helpful personal finance advisor AI.", prompt);
    }

    public async Task<string> GetSavingsSuggestionsAsync(int userId)
    {
        var totalIncome = await _context.Incomes
            .Where(i => i.UserId == userId && !i.IsDeleted)
            .SumAsync(i => i.Amount);

        var totalExpenses = await _context.Expenses
            .Where(e => e.UserId == userId && !e.IsDeleted)
            .SumAsync(e => e.Amount);

        var savingsRate = totalIncome > 0 ? ((totalIncome - totalExpenses) / totalIncome) * 100 : 0;

        var prompt = $"""
            Financial Summary:
            Total Income: ${totalIncome:F2}
            Total Expenses: ${totalExpenses:F2}
            Savings Rate: {savingsRate:F1}%
            
            Provide actionable savings suggestions based on this data.
            """;

        return await CallAIAsync("You are a helpful personal finance advisor AI.", prompt);
    }

    public async Task<decimal> GetFinancialHealthScoreAsync(int userId)
    {
        var totalIncome = await _context.Incomes
            .Where(i => i.UserId == userId && !i.IsDeleted)
            .SumAsync(i => i.Amount);

        var totalExpenses = await _context.Expenses
            .Where(e => e.UserId == userId && !e.IsDeleted)
            .SumAsync(e => e.Amount);

        var activeDebts = await _context.Debts
            .Where(d => d.UserId == userId && !d.IsDeleted && d.Status != DebtStatus.Paid)
            .SumAsync(d => d.Amount - d.AmountPaid);

        var savingsRate = totalIncome > 0 ? (totalIncome - totalExpenses) / totalIncome : 0;
        var debtToIncome = totalIncome > 0 ? activeDebts / totalIncome : 1;

        decimal score = 50;
        if (savingsRate > 0.2m) score += 20;
        else if (savingsRate > 0.1m) score += 10;
        else if (savingsRate < 0) score -= 20;

        if (debtToIncome < 0.2m) score += 15;
        else if (debtToIncome < 0.4m) score += 5;
        else score -= 15;

        return Math.Clamp(score, 0, 100);
    }

    public async Task<string> GetExpensePredictionAsync(int userId)
    {
        var recentExpenses = await _context.Expenses
            .Where(e => e.UserId == userId && e.Date >= DateTime.UtcNow.AddMonths(-3) && !e.IsDeleted)
            .Include(e => e.Category)
            .GroupBy(e => e.Category != null ? e.Category.Name : "Uncategorized")
            .Select(g => new { Category = g.Key, AvgMonthly = g.Sum(e => e.Amount) / 3 })
            .ToListAsync();

        var prompt = $"""
            Based on the last 3 months of spending, predict next month's expenses:
            {string.Join("\n", recentExpenses.Select(e => $"- {e.Category}: ~${e.AvgMonthly:F2}/month"))}
            
            Provide a prediction and tips for reducing future expenses.
            """;

        return await CallAIAsync("You are a helpful personal finance advisor AI.", prompt);
    }

    public async Task<string> GetChatResponseAsync(int userId, string message)
    {
        var totalIncome = await _context.Incomes.Where(i => i.UserId == userId && !i.IsDeleted).SumAsync(i => i.Amount);
        var totalExpenses = await _context.Expenses.Where(e => e.UserId == userId && !e.IsDeleted).SumAsync(e => e.Amount);
        var activeDebts = await _context.Debts.Where(d => d.UserId == userId && !d.IsDeleted && d.Status != DebtStatus.Paid).CountAsync();

        var systemPrompt = $"""
            You are a personal finance assistant for Akanti. The user's financial summary:
            Total Income: ${totalIncome:F2}
            Total Expenses: ${totalExpenses:F2}
            Active Debts: {activeDebts}
            
            Answer the user's financial question concisely and helpfully.
            """;

        return await CallAIAsync(systemPrompt, message);
    }

    private async Task<string> CallAIAsync(string systemPrompt, string userMessage)
    {
        var apiKey = _configuration["Groq:ApiKey"];
        if (string.IsNullOrEmpty(apiKey))
            return "AI service not configured. Please add your Groq API key to appsettings.json.\n\nGet a free key at: https://console.groq.com/keys";

        var requestBody = new
        {
            model = "llama-3.3-70b-versatile",
            messages = new[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userMessage }
            },
            max_tokens = 500,
            temperature = 0.7
        };

        var url = "https://api.groq.com/openai/v1/chat/completions";

        for (int attempt = 0; attempt < 3; attempt++)
        {
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

            var response = await _httpClient.PostAsync(url,
                new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json"));

            if ((int)response.StatusCode == 429)
            {
                await Task.Delay(3000 * (attempt + 1));
                continue;
            }

            var responseContent = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
                return $"AI request failed ({response.StatusCode}): {responseContent}";

            using var doc = JsonDocument.Parse(responseContent);

            try
            {
                return doc.RootElement
                    .GetProperty("choices")[0]
                    .GetProperty("message")
                    .GetProperty("content")
                    .GetString() ?? "Unable to generate response.";
            }
            catch
            {
                return "Unable to parse AI response.";
            }
        }

        return "AI service is busy. Please try again in a moment.";
    }
}
