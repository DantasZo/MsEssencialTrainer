using System.Text.Json;
using MsFundamentals.Trainer.Infrastructure;
using MsFundamentals.Trainer.Models;
using MsFundamentals.Trainer.Services; // ensure CacheService

namespace MsFundamentals.Trainer.Services;

public static class SeedLoader
{
    // Expose diagnostic info
    public static IReadOnlyDictionary<string, string[]> LastCandidates => _lastCandidates;
    private static Dictionary<string, string[]> _lastCandidates = new();

    public static void LoadSeeds(CacheService cache, ILogger logger, string? contentRoot = null)
    {
        try
        {
            var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var baseDir = AppContext.BaseDirectory; // bin/... path
            var root = contentRoot ?? baseDir; // project root (src) normally
            logger.LogInformation("SeedLoader root={Root} baseDir={BaseDir}", root, baseDir);

            // Build ascending path list (root, parent, grandparent) for robustness
            var roots = new List<string>();
            var cur = new DirectoryInfo(root);
            for (int i = 0; i < 4 && cur is not null; i++)
            {
                roots.Add(cur.FullName);
                cur = cur.Parent;
            }
            // Always include baseDir too
            if (!roots.Contains(baseDir)) roots.Add(baseDir);

            string[] BuildCandidates(string fileName) => roots
                .SelectMany(r => new[]{
                    Path.Combine(r, "seed", fileName),
                    Path.Combine(r, fileName) // flat copy fallback
                })
                .Distinct()
                .ToArray();

            var azCandidates = BuildCandidates("questions.az900.json");
            var aiCandidates = BuildCandidates("questions.ai900.json");
            _lastCandidates = new Dictionary<string, string[]>{
                ["AZ-900"] = azCandidates,
                ["AI-900"] = aiCandidates
            };

            logger.LogInformation("AZ-900 candidates: {Cnt}", azCandidates.Length);
            logger.LogInformation("AI-900 candidates: {Cnt}", aiCandidates.Length);

            string? azPath = azCandidates.FirstOrDefault(File.Exists);
            string? aiPath = aiCandidates.FirstOrDefault(File.Exists);

            if (azPath is not null)
            {
                var json = File.ReadAllText(azPath);
                var qs = JsonSerializer.Deserialize<List<Question>>(json, opts) ?? new();
                cache.Set("BANK::AZ-900::pt-BR", qs);
                logger.LogInformation("Loaded AZ-900 seed from {Path}: {Count} questions.", azPath, qs.Count);
            }
            else
            {
                logger.LogWarning("AZ-900 seed file not found. Checked: {Candidates}", string.Join(";", azCandidates));
            }

            if (aiPath is not null)
            {
                var json = File.ReadAllText(aiPath);
                var qs = JsonSerializer.Deserialize<List<Question>>(json, opts) ?? new();
                cache.Set("BANK::AI-900::pt-BR", qs);
                logger.LogInformation("Loaded AI-900 seed from {Path}: {Count} questions.", aiPath, qs.Count);
            }
            else
            {
                logger.LogWarning("AI-900 seed file not found. Checked: {Candidates}", string.Join(";", aiCandidates));
            }

            if (azPath is null && aiPath is null)
            {
                logger.LogError("Nenhum arquivo de seed encontrado para AZ-900 ou AI-900.");
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to load seed questions.");
        }
    }
}
