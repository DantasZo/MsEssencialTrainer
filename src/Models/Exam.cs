
namespace MsFundamentals.Trainer.Models;

public sealed class Exam
{
    public Guid ExamId { get; set; } = Guid.NewGuid();
    public required string Track { get; set; } // "AZ-900" | "AI-900"
    public required string Language { get; set; } // "pt-BR"
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public required List<Question> Questions { get; set; }
}

public sealed class Question
{
    public required string Id { get; set; } // "Q1"
    public required string Stem { get; set; }
    public required Dictionary<string, string> Options { get; set; } // A..D
    public required string CorrectOption { get; set; } // "A"|"B"|"C"|"D"
    public required string Difficulty { get; set; } // "easy"|"medium"|"hard"
    public required List<string> ObjectiveRefs { get; set; }
}
