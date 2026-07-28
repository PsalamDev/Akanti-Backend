using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Akanti.API.Services;

namespace Akanti.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class AIAssistantController : ControllerBase
{
    private readonly IAIService _aiService;

    public AIAssistantController(IAIService aiService)
    {
        _aiService = aiService;
    }

    private int GetUserId() => int.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);

    [HttpGet("health-score")]
    public async Task<ActionResult<object>> GetFinancialHealthScore()
    {
        var score = await _aiService.GetFinancialHealthScoreAsync(GetUserId());
        return Ok(new { score, label = GetHealthLabel(score) });
    }

    [HttpGet("spending-analysis")]
    public async Task<ActionResult<object>> GetSpendingAnalysis([FromQuery] DateTime? startDate, [FromQuery] DateTime? endDate)
    {
        var start = startDate ?? DateTime.UtcNow.AddMonths(-1);
        var end = endDate ?? DateTime.UtcNow;
        var analysis = await _aiService.GetSpendingAnalysisAsync(GetUserId(), start, end);
        return Ok(new { analysis, startDate = start, endDate = end });
    }

    [HttpGet("budget-recommendation/{budgetId}")]
    public async Task<ActionResult<object>> GetBudgetRecommendation(int budgetId)
    {
        var recommendation = await _aiService.GetBudgetRecommendationAsync(GetUserId(), budgetId);
        return Ok(new { recommendation });
    }

    [HttpGet("savings-suggestions")]
    public async Task<ActionResult<object>> GetSavingsSuggestions()
    {
        var suggestions = await _aiService.GetSavingsSuggestionsAsync(GetUserId());
        return Ok(new { suggestions });
    }

    [HttpGet("expense-prediction")]
    public async Task<ActionResult<object>> GetExpensePrediction()
    {
        var prediction = await _aiService.GetExpensePredictionAsync(GetUserId());
        return Ok(new { prediction });
    }

    [HttpPost("chat")]
    public async Task<ActionResult<object>> Chat([FromBody] ChatRequest request)
    {
        var response = await _aiService.GetChatResponseAsync(GetUserId(), request.Message);
        return Ok(new { response });
    }

    private static string GetHealthLabel(decimal score) => score switch
    {
        >= 80 => "Excellent",
        >= 60 => "Good",
        >= 40 => "Fair",
        _ => "Needs Improvement"
    };
}

public class ChatRequest
{
    public string Message { get; set; } = string.Empty;
}