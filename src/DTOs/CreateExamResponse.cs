
using MsFundamentals.Trainer.Models;

namespace MsFundamentals.Trainer.DTOs;

public sealed class CreateExamResponse
{
    public required Guid ExamId { get; set; }
    public required string Track { get; set; }
    public required DateTime CreatedAt { get; set; }
    public required IEnumerable<Question> Questions { get; set; }
}
