using MsFundamentals.Trainer.Models;
using System.Collections.Concurrent;

namespace MsFundamentals.Trainer.Repositories;

public sealed class InMemoryExamRepository : IExamRepository
{
    private readonly ConcurrentDictionary<Guid, Exam> _exams = new();
    // Armazena submissões por SubmissionId
    private readonly ConcurrentDictionary<Guid, Submission> _submissionsById = new();
    // Mantém último SubmissionId por ExamId
    private readonly ConcurrentDictionary<Guid, Guid> _latestSubmissionByExam = new();

    public Task SaveExamAsync(Exam exam, CancellationToken ct = default)
    {
        _exams[exam.ExamId] = exam;
        return Task.CompletedTask;
    }

    public Task<Exam?> GetExamAsync(Guid examId, CancellationToken ct = default)
    {
        _exams.TryGetValue(examId, out var exam);
        return Task.FromResult(exam);
    }

    public Task SaveSubmissionAsync(Submission submission, CancellationToken ct = default)
    {
        _submissionsById[submission.SubmissionId] = submission;
        _latestSubmissionByExam[submission.ExamId] = submission.SubmissionId;
        return Task.CompletedTask;
    }

    public Task<Submission?> GetLatestSubmissionAsync(Guid examId, CancellationToken ct = default)
    {
        if (_latestSubmissionByExam.TryGetValue(examId, out var sid) && _submissionsById.TryGetValue(sid, out var sub))
            return Task.FromResult<Submission?>(sub);
        return Task.FromResult<Submission?>(null);
    }

    public Task<Submission?> GetSubmissionAsync(Guid submissionId, CancellationToken ct = default)
    {
        _submissionsById.TryGetValue(submissionId, out var sub);
        return Task.FromResult(sub);
    }
}
