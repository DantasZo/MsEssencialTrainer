
namespace MsFundamentals.Trainer.DTOs;

public sealed class AnalysisRequest
{
    public string AnalysisMode { get; set; } = "light"; // "light" | "deep"
    public string Language { get; set; } = "pt-BR";
}
