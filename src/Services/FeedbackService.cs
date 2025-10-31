using System.Text.Json;
using MsFundamentals.Trainer.Infrastructure;
using MsFundamentals.Trainer.Models;

namespace MsFundamentals.Trainer.Services;

public sealed class FeedbackService
{
    private readonly Gpt5Client _gpt;
    private readonly AiPromptBuilder _prompts;
    private readonly ILogger<FeedbackService> _logger;

    public FeedbackService(Gpt5Client gpt, AiPromptBuilder prompts, ILogger<FeedbackService> logger)
    {
        _gpt = gpt;
        _prompts = prompts;
        _logger = logger;
    }

    public async Task<(AnalysisResult result, int tokIn, int tokOut)> GenerateFeedbackAsync(
        Exam exam,
        Submission submission,
        string analysisMode,
        string language,
        HttpContext httpContext,
        CancellationToken ct = default)
    {
        // Local correction first
        var qById = exam.Questions.ToDictionary(q => q.Id);
        var total = exam.Questions.Count;
        int correct = 0;
        var per = new List<PerQuestionAnalysis>();

        foreach (var a in submission.Answers)
        {
            if (!qById.TryGetValue(a.QuestionId, out var q)) continue;
            var ok = string.Equals(q.CorrectOption, a.Selected, StringComparison.OrdinalIgnoreCase);
            if (ok) correct++;
            per.Add(new PerQuestionAnalysis{
                QuestionId = a.QuestionId,
                IsCorrect = ok,
                Explanation = null,
                ObjectiveRefs = q.ObjectiveRefs
            });
        }

        var score = (int)Math.Round(100.0 * correct / Math.Max(1, total));

        // Economic mode: skip GPT only if high score AND user did not request deep
        if (analysisMode != "deep" && score >= 90)
        {
            var light = new AnalysisResult{
                Score = score,
                PerQuestion = per.Select(p => new PerQuestionAnalysis{
                    QuestionId = p.QuestionId,
                    IsCorrect = p.IsCorrect,
                    Explanation = p.IsCorrect ? "Resposta correta." : "Revise o conceito envolvido.",
                    ObjectiveRefs = p.ObjectiveRefs
                }).ToList(),
                Strengths = new List<string>{ "Excelente desempenho geral." },
                Gaps = new List<string>(),
                StudyPlan = new List<StudyPlanItem>()
            };
            return (light, 0, 0);
        }

        // Build wrong set
        var wrong = per.Where(p => !p.IsCorrect).ToList();
        if (analysisMode == "light")
            wrong = wrong.Take(5).ToList(); // permitir mais que antes

        if (wrong.Count == 0)
        {
            var empty = new AnalysisResult{
                Score = score,
                PerQuestion = per.Select(p => new PerQuestionAnalysis{
                    QuestionId = p.QuestionId,
                    IsCorrect = p.IsCorrect,
                    Explanation = "Resposta correta.",
                    ObjectiveRefs = p.ObjectiveRefs
                }).ToList(),
                Strengths = new List<string>{ "Sem itens críticos a revisar." },
                Gaps = new List<string>(),
                StudyPlan = new List<StudyPlanItem>()
            };
            return (empty, 0, 0);
        }

        var wrongPairs = wrong.Select(w => (w.QuestionId, submission.Answers.First(a => a.QuestionId == w.QuestionId).Selected)).ToArray();
        var wrongQuestions = wrong.Select(w => qById[w.QuestionId]).ToArray();

        var sys = _prompts.BuildAnalysisSystemPrompt(analysisMode);
        var user = _prompts.BuildAnalysisUserPrompt(wrongQuestions, wrongPairs, language, analysisMode);

        var (content, tokIn, tokOut) = await _gpt.ChatJsonAsync(sys, user, ct: ct);

        // Attach tokens to httpContext for Telemetry
        httpContext.Items["AI_tokens_in"] = (int)(httpContext.Items.TryGetValue("AI_tokens_in", out var tin) ? Convert.ToInt32(tin) : 0) + tokIn;
        httpContext.Items["AI_tokens_out"] = (int)(httpContext.Items.TryGetValue("AI_tokens_out", out var tout) ? Convert.ToInt32(tout) : 0) + tokOut;

        // Parse response
        AnalysisResult? result = null;
        try
        {
            result = JsonSerializer.Deserialize<AnalysisResult>(content, new JsonSerializerOptions{
                PropertyNameCaseInsensitive = true
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falha ao desserializar resposta do modelo: {Content}", content);
        }

        if (result is null)
        {
            // Fallback enriquecido
            result = new AnalysisResult{
                Score = score,
                PerQuestion = per.Select(p => new PerQuestionAnalysis{
                    QuestionId = p.QuestionId,
                    IsCorrect = p.IsCorrect,
                    Explanation = p.IsCorrect ? "Resposta correta." : "Sua resposta está incorreta; revise atentamente o objetivo associado e compare cada alternativa.",
                    ObjectiveRefs = p.ObjectiveRefs
                }).ToList(),
                Strengths = correct >= total/2 ? new List<string>{ "Conhecimento básico estabelecido." } : new List<string>(),
                Gaps = wrong.Select(w => string.Join("; ", qById[w.QuestionId].ObjectiveRefs)).Distinct().ToList(),
                StudyPlan = wrong
                    .Select(w => new StudyPlanItem{
                        Topic = string.Join("; ", qById[w.QuestionId].ObjectiveRefs),
                        Why = $"Erro na questão {w.QuestionId}",
                        Resources = new List<ResourceLink>{
                            new ResourceLink{ Title = "Microsoft Learn", Url = "https://learn.microsoft.com/pt-br/training/" }
                        }
                    }).ToList()
            };
        }
        else
        {
            // Sincroniza flags e garante explicações
            var perMap = per.ToDictionary(x => x.QuestionId);
            foreach (var item in result.PerQuestion)
            {
                if (perMap.TryGetValue(item.QuestionId, out var baseline))
                {
                    item.IsCorrect = baseline.IsCorrect;
                    item.ObjectiveRefs ??= baseline.ObjectiveRefs;
                    if (!item.IsCorrect && string.IsNullOrWhiteSpace(item.Explanation))
                    {
                        // Inject minimal explanation if missing
                        var correctOpt = qById[item.QuestionId].CorrectOption;
                        item.Explanation = $"Sua resposta estava incorreta. A alternativa correta é {correctOpt}. Revise: {string.Join(", ", baseline.ObjectiveRefs ?? new List<string>())}.";
                    }
                    if (item.IsCorrect && string.IsNullOrWhiteSpace(item.Explanation))
                    {
                        item.Explanation = "Boa! Continue consolidando este conceito.";
                    }
                }
            }

            // Garante que todas questões processadas apareçam (importante para deep)
            foreach (var p in per)
            {
                if (result.PerQuestion.All(r => r.QuestionId != p.QuestionId))
                {
                    var correctOpt = qById[p.QuestionId].CorrectOption;
                    result.PerQuestion.Add(new PerQuestionAnalysis{
                        QuestionId = p.QuestionId,
                        IsCorrect = p.IsCorrect,
                        Explanation = p.IsCorrect ? "Correto." : $"Não informado pelo modelo. Correta: {correctOpt}. Revise objetivos: {string.Join(", ", p.ObjectiveRefs)}.",
                        ObjectiveRefs = p.ObjectiveRefs
                    });
                }
            }
        }

        return (result, tokIn, tokOut);
    }
}
