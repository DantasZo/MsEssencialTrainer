using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using MsFundamentals.Trainer.Models;

namespace MsFundamentals.Trainer.Services;

public static class QuestionBankUtilities
{
    private static readonly HashSet<string> _expectedOptions = new(["A", "B", "C", "D"]);

    public static List<Question> Sanitize(string track, IEnumerable<Question> raw, ILogger logger)
    {
        var result = new List<Question>();
        var seen = new HashSet<string>();

        foreach (var q in raw)
        {
            if (q is null) continue;

            var normalizedStem = NormalizeStem(q.Stem);
            if (string.IsNullOrWhiteSpace(normalizedStem))
            {
                logger.LogWarning("Questão ignorada em {Track}: enunciado vazio ou inválido.", track);
                continue;
            }

            var options = q.Options ?? new Dictionary<string, string>();
            if (options.Count != 4 || !_expectedOptions.All(options.ContainsKey))
            {
                logger.LogWarning("Questão ignorada em {Track}: opções devem conter A, B, C e D.");
                continue;
            }

            var correct = (q.CorrectOption ?? string.Empty).Trim().ToUpperInvariant();
            if (!_expectedOptions.Contains(correct))
            {
                logger.LogWarning("Questão ignorada em {Track}: alternativa correta ausente ou inválida.", track);
                continue;
            }

            if (!options.ContainsKey(correct))
            {
                logger.LogWarning("Questão ignorada em {Track}: alternativa correta não existe no conjunto de opções.", track);
                continue;
            }

            var objectives = (q.ObjectiveRefs ?? new List<string>())
                .Where(o => !string.IsNullOrWhiteSpace(o))
                .Select(o => o.Trim())
                .ToList();
            if (objectives.Count == 0)
            {
                objectives.Add($"{track}: Objetivo não informado");
            }

            var primaryObjective = objectives.First().ToUpperInvariant();
            var key = $"{primaryObjective}::{normalizedStem}";
            if (!seen.Add(key))
            {
                logger.LogWarning("Questão duplicada removida em {Track} (objetivo {Objective}).", track, primaryObjective);
                continue;
            }

            result.Add(new Question
            {
                Id = string.IsNullOrWhiteSpace(q.Id) ? $"S{result.Count + 1}" : q.Id.Trim(),
                Stem = q.Stem.Trim(),
                Options = options.ToDictionary(k => k.Key.ToUpperInvariant(), v => v.Value.Trim()),
                CorrectOption = correct,
                Difficulty = string.IsNullOrWhiteSpace(q.Difficulty) ? "medium" : q.Difficulty.Trim().ToLowerInvariant(),
                ObjectiveRefs = objectives
            });
        }

        return result;
    }

    public static List<Question> EnsureUniqueByStemAndObjective(IEnumerable<Question> questions)
    {
        var result = new List<Question>();
        var seen = new HashSet<string>();

        foreach (var q in questions)
        {
            var normalizedStem = NormalizeStem(q.Stem);
            var objectiveKey = (q.ObjectiveRefs.FirstOrDefault() ?? string.Empty).ToUpperInvariant();
            var key = $"{objectiveKey}::{normalizedStem}";
            if (seen.Add(key))
            {
                result.Add(q);
            }
        }

        return result;
    }

    private static string NormalizeStem(string stem)
    {
        if (string.IsNullOrWhiteSpace(stem)) return string.Empty;

        var normalized = stem.ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();
        foreach (var ch in normalized)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (category == UnicodeCategory.NonSpacingMark) continue;

            if (char.IsLetterOrDigit(ch))
            {
                sb.Append(ch);
            }
            else if (char.IsWhiteSpace(ch))
            {
                sb.Append(' ');
            }
        }

        return Regex.Replace(sb.ToString(), "\\s+", " ").Trim();
    }
}
