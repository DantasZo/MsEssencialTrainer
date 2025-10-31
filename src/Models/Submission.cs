
namespace MsFundamentals.Trainer.Models;

public sealed class Submission
{
    public Guid SubmissionId { get; set; } = Guid.NewGuid();
    public required Guid ExamId { get; set; }
    public DateTime ReceivedAtUtc { get; set; } = DateTime.UtcNow;
    public required List<SubmissionAnswer> Answers { get; set; }
}

public sealed class SubmissionAnswer
{
    public required string QuestionId { get; set; }
    public required string Selected { get; set; } // "A".."D"
}
