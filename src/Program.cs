using System.Linq;
using System.Reflection;
using System.Text.Json;
using FluentValidation;
using FluentValidation.Results;
using MsFundamentals.Trainer.DTOs;
using MsFundamentals.Trainer.Infrastructure;
using MsFundamentals.Trainer.Models;
using MsFundamentals.Trainer.Repositories;
using MsFundamentals.Trainer.Services;
using Microsoft.Extensions.Configuration;

var builder = WebApplication.CreateBuilder(args);

// Ensure User Secrets in Development
if (builder.Environment.IsDevelopment())
{
    builder.Configuration.AddUserSecrets<Program>(optional: true);
}

// Add services
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddMemoryCache();
builder.Services.AddHttpClient();

builder.Services.AddSingleton<CacheService>();
builder.Services.AddSingleton<Gpt5Client>();
builder.Services.AddSingleton<AiPromptBuilder>();
builder.Services.AddSingleton<FeedbackService>();
builder.Services.AddSingleton<ExamService>();

builder.Services.AddSingleton<IExamRepository, InMemoryExamRepository>();

builder.Services.AddValidatorsFromAssemblyContaining<MsFundamentals.Trainer.Validation.CreateExamRequestValidator>();

var app = builder.Build();

// Telemetry
app.UseMiddleware<TelemetryMiddleware>();

// Swagger
app.UseSwagger();
app.UseSwaggerUI();

// Seed
var cache = app.Services.GetRequiredService<CacheService>();
var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Seed");
SeedLoader.LoadSeeds(cache, logger, app.Environment.ContentRootPath);

// Helpers
IResult Bad(ValidationResult vr) => Results.ValidationProblem(vr.ToDictionary(), statusCode: 400);

// Routes
app.MapPost("/exams", async (CreateExamRequest req, IValidator<CreateExamRequest> validator, ExamService service, HttpContext ctx, CancellationToken ct) =>
{
    var vr = await validator.ValidateAsync(req, ct);
    if (!vr.IsValid) return Bad(vr);

    var exam = await service.CreateExamAsync(req.Track, req.Language, req.Count, req.DifficultyMix, ctx, ct);

    var res = new CreateExamResponse{
        ExamId = exam.ExamId,
        Track = exam.Track,
        CreatedAt = exam.CreatedAtUtc,
        Questions = exam.Questions // inclui correctOption (admin/validação)
    };
    return Results.Ok(res);
});

app.MapGet("/exams/{examId:guid}", async (Guid examId, IExamRepository repo, CancellationToken ct) =>
{
    var exam = await repo.GetExamAsync(examId, ct);
    if (exam is null) return Results.NotFound();

    var res = new GetExamResponse{
        ExamId = exam.ExamId,
        Track = exam.Track,
        CreatedAt = exam.CreatedAtUtc,
        Questions = exam.Questions.Select(q => new GetExamQuestion{
            Id = q.Id,
            Stem = q.Stem,
            Options = q.Options,
            Difficulty = q.Difficulty,
            ObjectiveRefs = q.ObjectiveRefs
        }).ToList()
    };
    return Results.Ok(res);
});

app.MapPost("/exams/{examId:guid}/submissions", async (Guid examId, SubmitAnswersRequest req, IValidator<SubmitAnswersRequest> validator, IExamRepository repo, CancellationToken ct) =>
{
    var vr = await validator.ValidateAsync(req, ct);
    if (!vr.IsValid) return Results.ValidationProblem(vr.ToDictionary(), statusCode: 400);

    var exam = await repo.GetExamAsync(examId, ct);
    if (exam is null) return Results.NotFound();

    var submission = new Submission{
        ExamId = examId,
        Answers = req.Answers
    };
    await repo.SaveSubmissionAsync(submission, ct);
    return Results.Ok(new SubmitAnswersResponse{
        SubmissionId = submission.SubmissionId,
        ReceivedAt = submission.ReceivedAtUtc
    });
});

// Novo endpoint de análise baseado em submissionId
app.MapPost("/submissions/{submissionId:guid}/analysis", async (Guid submissionId, AnalysisRequest req, IValidator<AnalysisRequest> validator, IExamRepository repo, FeedbackService feedback, HttpContext ctx, CancellationToken ct) =>
{
    var vr = await validator.ValidateAsync(req, ct);
    if (!vr.IsValid) return Results.ValidationProblem(vr.ToDictionary(), statusCode: 400);

    var submission = await repo.GetSubmissionAsync(submissionId, ct);
    if (submission is null) return Results.NotFound(new { error = "Submissão não encontrada." });

    var exam = await repo.GetExamAsync(submission.ExamId, ct);
    if (exam is null) return Results.NotFound(new { error = "Exame associado à submissão não encontrado." });

    var (result, tokIn, tokOut) = await feedback.GenerateFeedbackAsync(exam, submission, req.AnalysisMode, req.Language, ctx, ct);

    var env = new AnalysisEnvelopeResponse{
        Result = result,
        SubmissionId = submission.SubmissionId,
        ExamId = exam.ExamId
    };
    return Results.Ok(env);
});

// Endpoint de teste da IA
app.MapGet("/ai/ping", async (Gpt5Client client, HttpContext ctx, CancellationToken ct) =>
{
    try
    {
        var (content, tin, tout) = await client.ChatJsonAsync(
            "Você é um serviço de verificação. Responda somente JSON.",
            "Retorne {\"status\":\"ok\"}" ,
            maxTokens: 50,
            ct: ct);
        return Results.Ok(new { success = true, tokensIn = tin, tokensOut = tout, raw = content });
    }
    catch (Exception ex)
    {
        return Results.Problem($"Falha ao chamar IA: {ex.Message}");
    }
});

// Seed status
app.MapGet("/seed/status", () =>
{
    var tracks = SeedLoader.LastCandidates.Keys.Any()
        ? SeedLoader.LastCandidates.Keys
        : new[]{"AZ-900","AI-900","DP-900"};
    var lang = "pt-BR";
    var data = new List<object>();
    foreach (var t in tracks)
    {
        var list = cache.GetOrSet($"BANK::{t}::{lang}", () => new List<Question>());
        var counts = list
            .GroupBy(q => q.Difficulty ?? "unknown")
            .ToDictionary(g => g.Key, g => g.Count());
        data.Add(new {
            track = t,
            language = lang,
            total = list.Count,
            byDifficulty = counts
        });
    }
    return Results.Ok(data);
});

// Diagnostic endpoint for seed file path candidates
app.MapGet("/seed/diag", () =>
{
    var dict = SeedLoader.LastCandidates.ToDictionary(k => k.Key, v => v.Value.Select(p => new { path = p, exists = File.Exists(p) }).ToList());
    return Results.Ok(new { candidates = dict });
});

app.Run();

public partial class Program { }
