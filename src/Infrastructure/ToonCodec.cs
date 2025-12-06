using System.Linq;
using System.Text.Json;
using MsFundamentals.Trainer.Models;

namespace MsFundamentals.Trainer.Infrastructure;

/// <summary>
/// Utilidades para trabalhar com o formato "toon" (chaves curtas) usado em chamadas Azure OpenAI,
/// reduzindo tokens ao forçar respostas compactas e convertendo-as para o modelo interno de questões.
/// </summary>
public static class ToonCodec
{
    /// <summary>
    /// Esquema JSON (json_schema) com chaves curtas para solicitar questões via Azure OpenAI.
    /// Mantém os campos essenciais, mas com identificadores compactos: s (stem), o (options),
    /// c (correct), d (difficulty) e r (refs).
    /// </summary>
    public static object AzureQuestionSchema => new
    {
        type = "json_schema",
        json_schema = new
        {
            name = "questions_toon",
            strict = true,
            schema = new
            {
                type = "array",
                items = new
                {
                    type = "object",
                    properties = new
                    {
                        s = new { type = "string", description = "Enunciado da questão" },
                        o = new
                        {
                            type = "object",
                            properties = new
                            {
                                a = new { type = "string" },
                                b = new { type = "string" },
                                c = new { type = "string" },
                                d = new { type = "string" }
                            },
                            required = new[] { "a", "b", "c", "d" }
                        },
                        c = new { type = "string", enum = new[] { "A", "B", "C", "D" } },
                        d = new { type = "string", enum = new[] { "easy", "medium", "hard" } },
                        r = new
                        {
                            type = "array",
                            items = new { type = "string" },
                            minItems = 1
                        }
                    },
                    required = new[] { "s", "o", "c", "r" }
                }
            }
        }
    };

    /// <summary>
    /// Converte um JSON em formato "toon" (chaves curtas) para a estrutura completa de <see cref="Question"/>.
    /// Se o conteúdo não estiver no formato esperado, devolve o JSON original.
    /// </summary>
    public static string NormalizeQuestions(string? toonJson)
    {
        if (string.IsNullOrWhiteSpace(toonJson)) return toonJson ?? string.Empty;

        try
        {
            using var doc = JsonDocument.Parse(toonJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return toonJson;

            var questions = new List<Question>();
            foreach (var item in doc.RootElement.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object) continue;

                var hasStem = item.TryGetProperty("s", out var stemEl) && stemEl.ValueKind == JsonValueKind.String;
                var hasOptions = item.TryGetProperty("o", out var optsEl) && optsEl.ValueKind == JsonValueKind.Object;
                var hasCorrect = item.TryGetProperty("c", out var correctEl) && correctEl.ValueKind == JsonValueKind.String;
                if (!hasStem || !hasOptions || !hasCorrect) continue;

                var options = new Dictionary<string, string>();
                foreach (var key in new[] { "a", "b", "c", "d" })
                {
                    if (optsEl.TryGetProperty(key, out var optVal) && optVal.ValueKind == JsonValueKind.String)
                    {
                        options[key.ToUpperInvariant()] = optVal.GetString() ?? string.Empty;
                    }
                }
                if (options.Count != 4) continue;

                var refs = new List<string>();
                if (item.TryGetProperty("r", out var refsEl) && refsEl.ValueKind == JsonValueKind.Array)
                {
                    refs.AddRange(refsEl.EnumerateArray()
                        .Where(x => x.ValueKind == JsonValueKind.String)
                        .Select(x => x.GetString() ?? string.Empty)
                        .Where(x => !string.IsNullOrWhiteSpace(x)));
                }

                questions.Add(new Question
                {
                    Stem = stemEl.GetString() ?? string.Empty,
                    Options = options,
                    CorrectOption = correctEl.GetString() ?? string.Empty,
                    Difficulty = item.TryGetProperty("d", out var diffEl) && diffEl.ValueKind == JsonValueKind.String
                        ? diffEl.GetString() ?? "medium"
                        : "medium",
                    ObjectiveRefs = refs.Count > 0 ? refs : new List<string> { "General" }
                });
            }

            if (questions.Count == 0) return toonJson;
            return JsonSerializer.Serialize(questions);
        }
        catch (JsonException)
        {
            return toonJson;
        }
    }
}
