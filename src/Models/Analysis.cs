
namespace MsFundamentals.Trainer.Models;

public sealed class AnalysisResult
{
    public int Score { get; set; }
    public required List<PerQuestionAnalysis> PerQuestion { get; set; }
    public required List<string> Strengths { get; set; }
    public required List<string> Gaps { get; set; }
    public required List<StudyPlanItem> StudyPlan { get; set; }
}

public sealed class PerQuestionAnalysis
{
    public required string QuestionId { get; set; }
    public bool IsCorrect { get; set; }
    public string? Explanation { get; set; }
    public List<string>? ObjectiveRefs { get; set; }
}

public sealed class StudyPlanItem
{
    public required string Topic { get; set; }
    public required string Why { get; set; }
    public required List<ResourceLink> Resources { get; set; }
}

public sealed class ResourceLink
{
    public required string Title { get; set; }
    public required string Url { get; set; }
}
