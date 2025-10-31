using System.Text;
using System.Text.Json;
using MsFundamentals.Trainer.Models;

namespace MsFundamentals.Trainer.Services;

public sealed class AiPromptBuilder
{
    public string BuildExamSystemPrompt() =>
        "Você é um especialista Microsoft certificado (AZ-900 e AI-900). Gere questões originais e únicas em português-Brasil, formato múltipla escolha (4 alternativas, 1 correta). Balanceie os níveis de dificuldade (fácil, médio, difícil). Cada questão deve indicar os tópicos do Microsoft Learn relacionados. Responda apenas JSON válido.";

    public string BuildExamUserPrompt(string track, int count)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Gere {count} questões para a certificação {track}.");
        sb.AppendLine("Formato JSON:");
        sb.AppendLine("[");
        sb.AppendLine("  {");
        sb.AppendLine("    \"stem\": \"...\",");
        sb.AppendLine("    \"options\": { \"A\": \"...\", \"B\": \"...\", \"C\": \"...\", \"D\": \"...\" },");
        sb.AppendLine("    \"correctOption\": \"A|B|C|D\",");
        sb.AppendLine("    \"difficulty\": \"easy|medium|hard\",");
        sb.AppendLine($"    \"objectiveRefs\": [\"{track}: ...\"]");
        sb.AppendLine("  }");
        sb.AppendLine("]");
        sb.AppendLine("Responda apenas JSON válido.");
        return sb.ToString();
    }

    public string BuildAnalysisSystemPrompt(string analysisMode) =>
        analysisMode == "deep"
            ? "Você é um instrutor Microsoft altamente especializado. Produza uma análise pedagógica profunda. Para cada questão incorreta: explique por que a resposta do aluno está errada, por que a correta está certa, o conceito central, um erro de raciocínio comum e uma dica de estudo acionável. Saída obrigatoriamente em JSON."
            : "Você é um instrutor Microsoft. Gere análise concisa para questões incorretas com explicação breve e plano de estudo resumido. Responda somente JSON.";

    public string BuildAnalysisUserPrompt(Question[] questions, (string questionId, string selected)[] wrong, string language, string analysisMode)
    {
        var payload = new
        {
            analysisMode,
            questions = questions.Select(q => new {
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
            ? "'explanation': texto obrigatório (5-8 frases) contendo: 1) resposta do aluno <selected>; 2) correta <correctOption>; 3) conceito principal; 4) por que a alternativa do aluno está incorreta; 5) dica prática de estudo; 6) exemplo ou analogia curta."
            : "'explanation': 1-3 frases: motivo do erro e dica curta.";

        var sb = new StringBuilder();
        sb.AppendLine("Analise o desempenho do aluno.");
        sb.AppendLine($"Idioma: {language}");
        sb.AppendLine("Entrada:");
        sb.AppendLine(Json(payload));
        sb.AppendLine("Gere saída JSON COM ESTRUTURA E CAMPOS OBRIGATÓRIOS:");
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
        sb.AppendLine("Regras:");
        sb.AppendLine("- Inclua TODAS as questões incorretas em perQuestion.");
        sb.AppendLine("- NÃO invente questões não listadas.");
        sb.AppendLine("- Campo explanation sempre presente; para corretas pode ser frase positiva curta.");
        sb.AppendLine("- Use português-BR.");
        sb.AppendLine("Responda SOMENTE JSON válido.");
        return sb.ToString();
    }

    private static string Json(object o) => JsonSerializer.Serialize(o, new JsonSerializerOptions { WriteIndented = true });
}
