
namespace MsFundamentals.Trainer.DTOs;

public sealed class CreateExamRequest
{
    public required string Track { get; set; } // "AZ-900" | "AI-900"
    public int Count { get; set; } = 10;
    public string Language { get; set; } = "pt-BR";
    // Optional: override difficulty mix (e.g., {"easy":4,"medium":4,"hard":2})
    public Dictionary<string,int>? DifficultyMix { get; set; }
}
