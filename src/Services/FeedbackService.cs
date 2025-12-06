using System.Text.Json;
using MsFundamentals.Trainer.Infrastructure;
using MsFundamentals.Trainer.Models;

namespace MsFundamentals.Trainer.Services;

public sealed class FeedbackService
{
    private readonly Gpt5Client _gpt;
    private readonly AiPromptBuilder _prompts;
    private readonly TokenEstimator _tokenEstimator;
    private readonly ILogger<FeedbackService> _logger;

    private const int AnalysisPromptBudgetTokens = 8000;
    private const int LightMaxWrong = 6;
    private const int DeepMaxWrong = 10;

    public FeedbackService(Gpt5Client gpt, AiPromptBuilder prompts, TokenEstimator tokenEstimator, ILogger<FeedbackService> logger)
    {
        _gpt = gpt;
        _prompts = prompts;
        _tokenEstimator = tokenEstimator;
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
        var qById = exam.Questions.ToDictionary(q => q.Id);
        var answersById = submission.Answers.ToDictionary(a => a.QuestionId, a => a, StringComparer.OrdinalIgnoreCase);

        var total = exam.Questions.Count;
        int correct = 0;
        var per = new List<PerQuestionAnalysis>();

        foreach (var q in exam.Questions)
        {
            if (!answersById.TryGetValue(q.Id, out var ans))
            {
                per.Add(new PerQuestionAnalysis
                {
                    QuestionId = q.Id,
                    IsCorrect = false,
                    Explanation = "Questao nao respondida.",
                    ObjectiveRefs = q.ObjectiveRefs
                });
                continue;
            }

            var ok = string.Equals(q.CorrectOption, ans.Selected, StringComparison.OrdinalIgnoreCase);
            if (ok) correct++;
            per.Add(new PerQuestionAnalysis
            {
                QuestionId = q.Id,
                IsCorrect = ok,
                Explanation = null,
                ObjectiveRefs = q.ObjectiveRefs
            });
        }

        var score = (int)Math.Round(100.0 * correct / Math.Max(1, total));

        if (analysisMode != "deep" && score >= 90)
        {
            var light = new AnalysisResult
            {
                Score = score,
                PerQuestion = per.Select(p => new PerQuestionAnalysis
                {
                    QuestionId = p.QuestionId,
                    IsCorrect = p.IsCorrect,
                    Explanation = p.IsCorrect ? "Resposta correta." : "Revise o conceito envolvido.",
                    ObjectiveRefs = p.ObjectiveRefs
                }).ToList(),
                Strengths = new List<string> { "Excelente desempenho geral." },
                Gaps = new List<string>(),
                StudyPlan = new List<StudyPlanItem>()
            };
            return (light, 0, 0);
        }

        var wrongBase = per.Where(p => !p.IsCorrect).ToList();
        if (analysisMode == "light")
            wrongBase = wrongBase.Take(LightMaxWrong).ToList();
        else
            wrongBase = wrongBase.Take(DeepMaxWrong).ToList();

        if (wrongBase.Count == 0)
        {
            var empty = new AnalysisResult
            {
                Score = score,
                PerQuestion = per.Select(p => new PerQuestionAnalysis
                {
                    QuestionId = p.QuestionId,
                    IsCorrect = p.IsCorrect,
                    Explanation = "Resposta correta.",
                    ObjectiveRefs = p.ObjectiveRefs
                }).ToList(),
                Strengths = new List<string> { "Sem itens criticos a revisar." },
                Gaps = new List<string>(),
                StudyPlan = new List<StudyPlanItem>()
            };
            return (empty, 0, 0);
        }

        var (wrongQuestions, wrongPairs, userPrompt) = BuildPromptWithinBudget(qById, wrongBase, answersById, language, analysisMode);

        var sys = _prompts.BuildAnalysisSystemPrompt(analysisMode);

        var (content, tokIn, tokOut) = await _gpt.ChatJsonAsync(sys, userPrompt, Gpt5Client.ResponseFormat.JsonObject, ct: ct);

        httpContext.Items["AI_tokens_in"] = (int)(httpContext.Items.TryGetValue("AI_tokens_in", out var tin) ? Convert.ToInt32(tin) : 0) + tokIn;
        httpContext.Items["AI_tokens_out"] = (int)(httpContext.Items.TryGetValue("AI_tokens_out", out var tout) ? Convert.ToInt32(tout) : 0) + tokOut;

        AnalysisResult? result = null;
        try
        {
            result = JsonSerializer.Deserialize<AnalysisResult>(content, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falha ao desserializar resposta do modelo: {Content}", content);
        }

        if (result is null)
        {
            result = new AnalysisResult
            {
                Score = score,
                PerQuestion = per.Select(p => new PerQuestionAnalysis
                {
                    QuestionId = p.QuestionId,
                    IsCorrect = p.IsCorrect,
                    Explanation = p.IsCorrect ? "Resposta correta." : "Sua resposta esta incorreta; revise atentamente o objetivo associado e compare cada alternativa.",
                    ObjectiveRefs = p.ObjectiveRefs
                }).ToList(),
                Strengths = correct >= total / 2 ? new List<string> { "Conhecimento basico estabelecido." } : new List<string>(),
                Gaps = wrongBase.Select(w => string.Join("; ", qById[w.QuestionId].ObjectiveRefs)).Distinct().ToList(),
                StudyPlan = wrongBase
                    .Select(w => new StudyPlanItem
                    {
                        Topic = string.Join("; ", qById[w.QuestionId].ObjectiveRefs),
                        Why = $"Erro na questao {w.QuestionId}",
                        Resources = new List<ResourceLink>
                        {
                            new ResourceLink { Title = "Microsoft Learn", Url = "https://learn.microsoft.com/pt-br/training/" }
                        }
                    }).ToList()
            };
        }
        else
        {
            var perMap = per.ToDictionary(x => x.QuestionId);
            foreach (var item in result.PerQuestion)
            {
                if (perMap.TryGetValue(item.QuestionId, out var baseline))
                {
                    item.IsCorrect = baseline.IsCorrect;
                    item.ObjectiveRefs ??= baseline.ObjectiveRefs;
                    if (!item.IsCorrect && string.IsNullOrWhiteSpace(item.Explanation))
                    {
                        var correctOpt = qById[item.QuestionId].CorrectOption;
                        item.Explanation = $"Sua resposta estava incorreta. Correta: {correctOpt}. Revise: {string.Join(", ", baseline.ObjectiveRefs ?? new List<string>())}.";
                    }
                    if (item.IsCorrect && string.IsNullOrWhiteSpace(item.Explanation))
                    {
                        item.Explanation = "Boa! Continue consolidando este conceito.";
                    }
                }
            }

            foreach (var p in per)
            {
                if (result.PerQuestion.All(r => r.QuestionId != p.QuestionId))
                {
                    var correctOpt = qById[p.QuestionId].CorrectOption;
                    result.PerQuestion.Add(new PerQuestionAnalysis
                    {
                        QuestionId = p.QuestionId,
                        IsCorrect = p.IsCorrect,
                        Explanation = p.IsCorrect ? "Correto." : $"Nao informado pelo modelo. Correta: {correctOpt}. Revise objetivos: {string.Join(", ", p.ObjectiveRefs ?? new List<string>())}.",
                        ObjectiveRefs = p.ObjectiveRefs
                    });
                }
            }
        }

        return (result, tokIn, tokOut);
    }

    private (Question[] wrongQuestions, (string questionId, string selected)[] wrongPairs, string prompt) BuildPromptWithinBudget(
        Dictionary<string, Question> qById,
        List<PerQuestionAnalysis> wrongBase,
        Dictionary<string, SubmissionAnswer> answersById,
        string language,
        string analysisMode)
    {
        var wrongList = wrongBase.ToList();

        Question[] wrongQuestions = Array.Empty<Question>();
        (string questionId, string selected)[] wrongPairs = Array.Empty<(string, string)>();
        string prompt = string.Empty;

        void Rebuild()
        {
            wrongPairs = wrongList
                .Select(w => answersById.TryGetValue(w.QuestionId, out var ans)
                    ? (w.QuestionId, ans.Selected)
                    : (w.QuestionId, string.Empty))
                .ToArray();
            wrongQuestions = wrongList.Select(w => qById[w.QuestionId]).ToArray();
            prompt = _prompts.BuildAnalysisUserPrompt(wrongQuestions, wrongPairs, language, analysisMode);
        }

        Rebuild();

        while (_tokenEstimator.EstimateTokens(prompt) + _tokenEstimator.EstimateTokens(_prompts.BuildAnalysisSystemPrompt(analysisMode)) > AnalysisPromptBudgetTokens && wrongList.Count > 1)
        {
            wrongList = wrongList.Take(wrongList.Count - 1).ToList();
            Rebuild();
        }

        return (wrongQuestions, wrongPairs, prompt);
    }
}
