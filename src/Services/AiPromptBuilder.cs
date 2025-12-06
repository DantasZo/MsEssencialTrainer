using System.Text;
using System.Text.Json;
using MsFundamentals.Trainer.Models;

namespace MsFundamentals.Trainer.Services;

public sealed class AiPromptBuilder
{
    public string BuildExamSystemPrompt() =>
        "Voce e um especialista Microsoft certificado (AZ-900 e AI-900). Gere questoes originais em portugues-BR com 4 alternativas e 1 correta, balanceando dificuldade. Retorne somente JSON valido.";

    public string BuildExamUserPrompt(string track, int count)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Gere {count} questoes para a certificacao {track}.");
        sb.AppendLine("Formato JSON:");
        sb.AppendLine("[{");
        sb.AppendLine("  \"stem\": \"...\",");
        sb.AppendLine("  \"options\": { \"A\": \"...\", \"B\": \"...\", \"C\": \"...\", \"D\": \"...\" },");
        sb.AppendLine("  \"correctOption\": \"A|B|C|D\",");
        sb.AppendLine("  \"difficulty\": \"easy|medium|hard\",");
        sb.AppendLine($"  \"objectiveRefs\": [\"{track}: ...\"]");
        sb.AppendLine("}]");
        sb.AppendLine("Responda apenas JSON valido.");
        return sb.ToString();
    }

    public string BuildAnalysisSystemPrompt(string analysisMode) =>
        analysisMode == "deep"
            ? "Voce e um instrutor Microsoft especializado. Produza analise pedagogica profunda em portugues-BR. Sempre responda JSON."
            : "Voce e um instrutor Microsoft. Gere analise concisa em portugues-BR. Sempre responda JSON.";

    public string BuildAnalysisUserPrompt(Question[] questions, (string questionId, string selected)[] wrong, string language, string analysisMode)
    {
        var payload = new
        {
            analysisMode,
            language,
            questions = questions.Select(q => new
            {
                id = q.Id,
                stem = q.Stem,
                options = q.Options,
                correctOption = q.CorrectOption,
                difficulty = q.Difficulty,
                objectiveRefs = q.ObjectiveRefs
            }),
            wrongAnswers = wrong.Select(w => new { questionId = w.questionId, selected = w.selected })
        };

        var explanationSpec = analysisMode == "deep"
            ? "\"explanation\": 5-8 frases: resposta do aluno <selected>, correta <correctOption>, conceito central, motivo do erro, dica acionavel, exemplo curto."
            : "\"explanation\": 1-3 frases: motivo do erro e dica curta.";

        var sb = new StringBuilder();
        sb.AppendLine("Analise o desempenho do aluno.");
        sb.AppendLine($"Entrada: {Json(payload, indented: false)}");
        sb.AppendLine("Formato JSON obrigatorio:");
        sb.AppendLine("{");
        sb.AppendLine("  \"score\": 0-100,");
        sb.AppendLine("  \"perQuestion\": [");
        sb.AppendLine($"    {{ \"questionId\": \"Qn\", \"isCorrect\": true/false, {explanationSpec} }}");
        sb.AppendLine("  ],");
        sb.AppendLine("  \"strengths\": [\"...\"],");
        sb.AppendLine("  \"gaps\": [\"...\"],");
        sb.AppendLine("  \"studyPlan\": [");
        sb.AppendLine("    { \"topic\": \"...\", \"why\": \"...\", \"resources\": [ { \"title\": \"...\", \"url\": \"...\" } ] }");
        sb.AppendLine("  ]");
        sb.AppendLine("}");
        sb.AppendLine("Regras: somente questoes fornecidas; inclua todas as incorretas; explanation sempre presente; retornar somente JSON valido.");
        return sb.ToString();
    }

    private static string Json(object o, bool indented = true) => JsonSerializer.Serialize(o, new JsonSerializerOptions { WriteIndented = indented });
}
