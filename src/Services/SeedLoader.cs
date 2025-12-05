using System.Linq;
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

            var tracks = new[]{
                new { Track = "AZ-900", File = "questions.az900.json" },
                new { Track = "AI-900", File = "questions.ai900.json" },
                new { Track = "DP-900", File = "questions.dp900.json" }
            };

            _lastCandidates = tracks.ToDictionary(t => t.Track, t => BuildCandidates(t.File));

            foreach (var kv in _lastCandidates)
            {
                logger.LogInformation("{Track} candidates: {Cnt}", kv.Key, kv.Value.Length);
            }

            var loaded = 0;
            foreach (var cfg in tracks)
            {
                var path = _lastCandidates[cfg.Track].FirstOrDefault(File.Exists);
                if (path is null)
                {
                    logger.LogWarning("{Track} seed file not found. Checked: {Candidates}", cfg.Track, string.Join(";", _lastCandidates[cfg.Track]));
                    continue;
                }

                var json = File.ReadAllText(path);
                var raw = JsonSerializer.Deserialize<List<Question>>(json, opts) ?? new();
                var sanitized = QuestionBankUtilities.Sanitize(cfg.Track, raw, logger);
                cache.Set($"BANK::{cfg.Track}::pt-BR", sanitized);
                logger.LogInformation("Loaded {Track} seed from {Path}: {Count} questions (após validação).", cfg.Track, path, sanitized.Count);
                loaded++;
            }

            if (loaded == 0)
            {
                logger.LogError("Nenhum arquivo de seed encontrado para AZ-900, AI-900 ou DP-900.");
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to load seed questions.");
        }
    }
}
