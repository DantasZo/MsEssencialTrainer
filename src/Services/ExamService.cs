using System.Linq;
using System.Text.Json;
using MsFundamentals.Trainer.Infrastructure;
using MsFundamentals.Trainer.Models;
using MsFundamentals.Trainer.Repositories;

namespace MsFundamentals.Trainer.Services;

public sealed class ExamService
{
    private readonly IExamRepository _repo;
    private readonly Gpt5Client _gpt;
    private readonly AiPromptBuilder _prompts;
    private readonly CacheService _cache;
    private readonly ILogger<ExamService> _logger;
    private readonly Random _rng = new();

    public ExamService(IExamRepository repo, Gpt5Client gpt, AiPromptBuilder prompts, CacheService cache, ILogger<ExamService> logger)
    {
        _repo = repo;
        _gpt = gpt;
        _prompts = prompts;
        _cache = cache;
        _logger = logger;
    }

    private static string BankKey(string track, string lang) => $"BANK::{track}::{lang}";

    public async Task<Exam> CreateExamAsync(string track, string lang, int count, Dictionary<string, int>? mix, HttpContext httpContext, CancellationToken ct = default)
    {
        // Load bank from cache (seeded by SeedLoader) using a local copy to avoid concurrent mutation
        var cached = _cache.GetOrSet(BankKey(track, lang), () => new List<Question>());
        var bank = QuestionBankUtilities
            .EnsureUniqueByStemAndObjective(cached)
            .Select(CloneQuestion)
            .ToList();

        // Try sample from bank
        var selected = SampleBalanced(bank, count, mix);

        // If insufficient, ask LLM for the remainder
        if (selected.Count < count)
        {
            var missing = count - selected.Count;
            var sys = _prompts.BuildExamSystemPrompt();
            var user = _prompts.BuildExamUserPrompt(track, missing);

            try
            {
                var (content, tokIn, tokOut) = await _gpt.ChatJsonAsync(sys, user, Gpt5Client.ResponseFormat.QuestionsToon, ct: ct);
                httpContext.Items["AI_tokens_in"] = (int)(httpContext.Items.TryGetValue("AI_tokens_in", out var tin) ? Convert.ToInt32(tin) : 0) + tokIn;
                httpContext.Items["AI_tokens_out"] = (int)(httpContext.Items.TryGetValue("AI_tokens_out", out var tout) ? Convert.ToInt32(tout) : 0) + tokOut;

                var newQs = JsonSerializer.Deserialize<List<Question>>(content, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();
                foreach (var q in newQs)
                {
                    if (q.Options.Count != 4 || !new HashSet<string>(q.Options.Keys).SetEquals(new[] { "A", "B", "C", "D" })) continue;
                    bank.Add(q);
                }
                bank = QuestionBankUtilities.EnsureUniqueByStemAndObjective(bank);
                selected = SampleBalanced(bank, count, mix);
                _cache.Set(BankKey(track, lang), bank.Select(CloneQuestion).ToList());
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Falha ao gerar questoes via LLM; usando fallback seed.");
                // Fallback: repetir questoes existentes para preencher (apenas se houver no banco)
                while (selected.Count < count && bank.Count > 0)
                {
                    // Seleciona questao pseudo-aleatoria
                    var next = bank[selected.Count % bank.Count];
                    selected.Add(next);
                }
                // Se ainda assim nao houver perguntas suficientes, adicionar um placeholder
                if (selected.Count == 0)
                {
                    _logger.LogWarning("Nenhuma questao disponivel apos tentar gerar com IA. Adicionando questao placeholder.");
                    selected.Add(new Question
                    {
                        Id = "Q1",
                        Stem = "Placeholder: nenhuma questao disponivel (verifique seeds ou configuracao da IA).",
                        Options = new Dictionary<string, string>
                        {
                            { "A", "Opcao A" },
                            { "B", "Opcao B" },
                            { "C", "Opcao C" },
                            { "D", "Opcao D" }
                        },
                        CorrectOption = "A",
                        Difficulty = "easy",
                        ObjectiveRefs = new List<string> { "Placeholder" }
                    });
                }
            }
        }

        // Assign sequential IDs
        for (int i = 0; i < selected.Count; i++)
        {
            selected[i] = new Question
            {
                Id = $"Q{i + 1}",
                Stem = selected[i].Stem,
                Options = selected[i].Options,
                CorrectOption = selected[i].CorrectOption,
                Difficulty = selected[i].Difficulty,
                ObjectiveRefs = selected[i].ObjectiveRefs
            };
        }

        var exam = new Exam
        {
            Track = track,
            Language = lang,
            Questions = selected
        };
        await _repo.SaveExamAsync(exam, ct);
        return exam;
    }

    private List<Question> SampleBalanced(List<Question> bank, int count, Dictionary<string, int>? mix)
    {
        if (bank.Count == 0) return new List<Question>();
        mix ??= new Dictionary<string, int> { { "easy", 4 }, { "medium", 4 }, { "hard", 2 } };
        var result = new List<Question>();

        foreach (var kv in mix)
        {
            var pool = bank.Where(q => q.Difficulty.Equals(kv.Key, StringComparison.OrdinalIgnoreCase)).OrderBy(_ => _rng.Next()).Take(kv.Value);
            result.AddRange(pool);
        }

        if (result.Count < count)
        {
            var remaining = bank.OrderBy(_ => _rng.Next()).Where(q => !result.Contains(q)).Take(count - result.Count);
            result.AddRange(remaining);
        }
        return result.Take(count).ToList();
    }

    private static Question CloneQuestion(Question q) => new Question
    {
        Id = q.Id,
        Stem = q.Stem,
        Options = new Dictionary<string, string>(q.Options),
        CorrectOption = q.CorrectOption,
        Difficulty = q.Difficulty,
        ObjectiveRefs = new List<string>(q.ObjectiveRefs)
    };
}
