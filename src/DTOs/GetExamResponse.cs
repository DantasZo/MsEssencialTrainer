
namespace MsFundamentals.Trainer.DTOs;

public sealed class GetExamResponse
{
    public required Guid ExamId { get; set; }
    public required string Track { get; set; }
    public required DateTime CreatedAt { get; set; }
    public required List<GetExamQuestion> Questions { get; set; }
}

public sealed class GetExamQuestion
{
    public required string Id { get; set; }
    public required string Stem { get; set; }
    public required Dictionary<string, string> Options { get; set; }
    public required string Difficulty { get; set; }
    public required List<string> ObjectiveRefs { get; set; }
}
