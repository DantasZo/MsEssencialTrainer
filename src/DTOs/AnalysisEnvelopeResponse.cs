
using MsFundamentals.Trainer.Models;

namespace MsFundamentals.Trainer.DTOs;

public sealed class AnalysisEnvelopeResponse
{
    public required AnalysisResult Result { get; set; }
    public required Guid SubmissionId { get; set; }
    public required Guid ExamId { get; set; }
}
