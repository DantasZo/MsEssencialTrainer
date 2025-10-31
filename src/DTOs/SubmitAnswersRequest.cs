
using MsFundamentals.Trainer.Models;

namespace MsFundamentals.Trainer.DTOs;

public sealed class SubmitAnswersRequest
{
    public required List<SubmissionAnswer> Answers { get; set; }
}
