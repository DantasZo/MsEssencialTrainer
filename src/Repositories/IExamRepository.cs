using MsFundamentals.Trainer.Models;

namespace MsFundamentals.Trainer.Repositories;

public interface IExamRepository
{
    Task SaveExamAsync(Exam exam, CancellationToken ct = default);
    Task<Exam?> GetExamAsync(Guid examId, CancellationToken ct = default);
    Task SaveSubmissionAsync(Submission submission, CancellationToken ct = default);
    Task<Submission?> GetLatestSubmissionAsync(Guid examId, CancellationToken ct = default);
    Task<Submission?> GetSubmissionAsync(Guid submissionId, CancellationToken ct = default);
}
