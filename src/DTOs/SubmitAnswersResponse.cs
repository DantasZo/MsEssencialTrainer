
namespace MsFundamentals.Trainer.DTOs;

public sealed class SubmitAnswersResponse
{
    public required Guid SubmissionId { get; set; }
    public required DateTime ReceivedAt { get; set; }
}
