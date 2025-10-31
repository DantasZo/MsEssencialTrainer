
namespace MsFundamentals.Trainer.Infrastructure;

// Very rough estimator: ~4 chars per token
public sealed class TokenEstimator
{
    public int EstimateTokens(string text)
        => Math.Max(1, text.Length / 4);
}
