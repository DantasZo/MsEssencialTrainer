# MicroTrainer / MsFundamentals.Trainer

API minimalista em .NET 8 para gerar exames práticos (AZ-900 / AI-900), receber respostas e produzir análise assistida por IA.

Foco direto: criar exame → responder → gerar análise. Sem persistência duradoura.

## Armazenamento (volátil)

- Banco de questões (seeds + possíveis geradas via IA): `IMemoryCache` nas chaves `BANK::<track>::<lang>` (ex.: `BANK::AZ-900::pt-BR`).
- Exames (`Exam`) e submissões (`Submission`): apenas em memória via `InMemoryExamRepository`.
- Reiniciar a aplicação apaga tudo. Não há disco ou banco configurado.
- Suporta múltiplas submissões por exame; análise é sempre por `submissionId`.

Para persistência real, implemente outro `IExamRepository` (ex.: EF Core + PostgreSQL / MongoDB / Redis).

## Endpoints

### 1. POST /exams
Cria um exame.
Request (CreateExamRequest):
```json
{
  "track": "AZ-900",
  "language": "pt-BR",
  "count": 10,
  "difficultyMix": { "easy": 4, "medium": 4, "hard": 2 }
}
```
Observações:
- `difficultyMix` é opcional; se ausente, o serviço usa um padrão `{easy:4,medium:4,hard:2}`.
- Se o seed não tiver questões suficientes, tenta gerar via IA; se falhar, duplica questões ou adiciona placeholder.

Response (CreateExamResponse) – inclui `correctOption` para uso administrativo:
```json
{
  "examId": "GUID",
  "track": "AZ-900",
  "createdAt": "2025-10-31T21:40:00Z",
  "questions": [
    {
      "id": "Q1",
      "stem": "Pergunta...",
      "options": { "A": "...", "B": "...", "C": "...", "D": "..." },
      "correctOption": "A",
      "difficulty": "easy",
      "objectiveRefs": ["OBJ1"]
    }
  ]
}
```

### 2. GET /exams/{examId}
Retorna exame para o candidato (sem `correctOption`).
Response (GetExamResponse):
```json
{
  "examId": "GUID",
  "track": "AZ-900",
  "createdAt": "2025-10-31T21:40:00Z",
  "questions": [
    {
      "id": "Q1",
      "stem": "Pergunta...",
      "options": { "A": "...", "B": "...", "C": "...", "D": "..." },
      "difficulty": "easy",
      "objectiveRefs": ["OBJ1"]
    }
  ]
}
```

### 3. POST /exams/{examId}/submissions
Registra respostas do usuário.
Request (SubmitAnswersRequest):
```json
{
  "answers": [
    { "questionId": "Q1", "selected": "A" },
    { "questionId": "Q2", "selected": "B" }
  ]
}
```
Response (SubmitAnswersResponse):
```json
{
  "submissionId": "GUID",
  "receivedAt": "2025-10-31T21:41:05Z"
}
```

### 4. POST /submissions/{submissionId}/analysis
Gera análise da submissão.
Request (AnalysisRequest):
```json
{
  "analysisMode": "light",
  "language": "pt-BR"
}
```
- `analysisMode`: `light` (limita questões incorretas analisadas) ou `deep` (todas).
- Otimização: se não for `deep` e score ≥ 90, retorno é local (sem IA).

Response (AnalysisEnvelopeResponse):
```json
{
  "examId": "GUID",
  "submissionId": "GUID",
  "result": {
    "score": 70,
    "perQuestion": [
      {
        "questionId": "Q1",
        "isCorrect": true,
        "explanation": "Resposta correta.",
        "objectiveRefs": ["OBJ1"]
      }
    ],
    "strengths": ["Conceitos fundamentais"],
    "gaps": ["Armazenamento"],
    "studyPlan": [
      {
        "topic": "Armazenamento",
        "why": "Erro na questão Q2",
        "resources": [
          { "title": "Microsoft Learn", "url": "https://learn.microsoft.com/pt-br/training/" }
        ]
      }
    ]
  }
}
```

### 5. GET /ai/ping
Teste da integração IA:
```json
{ "success": true, "tokensIn": 123, "tokensOut": 45, "raw": "{\"status\":\"ok\"}" }
```

### 6. GET /seed/status
Resumo do banco carregado por trilha:
```json
[
  {
    "track": "AZ-900",
    "language": "pt-BR",
    "total": 50,
    "byDifficulty": { "easy": 20, "medium": 20, "hard": 10 }
  }
]
```

### 7. GET /seed/diag
Diagnóstico dos caminhos candidatos de seed:
```json
{
  "candidates": {
    "AZ-900": [
      { "path": "/abs/path/seed/questions.az900.json", "exists": true }
    ],
    "AI-900": [
      { "path": "/abs/path/seed/questions.ai900.json", "exists": true }
    ]
  }
}
```

## Fluxo Resumido

1. Startup: `SeedLoader` tenta localizar e carregar arquivos de seed (`questions.az900.json`, `questions.ai900.json`) em memória.
2. Criar exame: seleção balanceada + geração complementar via IA (se necessário) → atribuição de IDs sequenciais `Q1..Qn`.
3. Entrega ao candidato (GET sem gabarito).
4. Recebimento de respostas (POST submissão).
5. Análise: correção local + (opcional) enriquecimento via IA.

## Detalhes Internos Relevantes

- Chaves de cache: `BANK::<track>::<lang>`.
- Mistura de dificuldade default: `{easy:4,medium:4,hard:2}`.
- Questões geradas pela IA são validadas (devem conter exatamente opções A/B/C/D) antes de entrar no banco.
- Tokens IA rastreados em `HttpContext.Items` (`AI_tokens_in`, `AI_tokens_out`).
- Fallback de geração: se nada disponível, adiciona questão placeholder.

## Limitações

| Aspecto | Limitação |
|---------|-----------|
| Persistência | Volátil; tudo se perde ao reiniciar. |
| Escalabilidade | Armazenamento em memória e dicionários concorrentes simples. |
| Segurança | Sem autenticação, autorização ou rate limiting. |
| Internacionalização | Apenas `pt-BR` nos seeds padrão. |
| Qualidade IA | Depende de resposta JSON válida; possui fallback local. |
| Monitoramento | Telemetria limitada a tokens e middleware simples. |

## Configuração de IA (Azure OpenAI)

Para que a geração de questões e análises enriquecidas funcione, é necessário configurar os secrets (ou variáveis de ambiente) referentes ao Azure OpenAI.

Chaves esperadas (hierarquia em `appsettings` / user-secrets):
```json
{
  "AzureOpenAI:UseAzure": true,
  "AzureOpenAI:Endpoint": "https://<nome-do-recurso>.openai.azure.com/",
  "AzureOpenAI:Deployment": "<nome-do-deployment-do-modelo>",
  "AzureOpenAI:ApiVersion": "<versao-da-api>",
  "AzureOpenAI:ApiKey": "<chave-secreta>"
}
```

Descrição de cada campo:
- AzureOpenAI:UseAzure (bool): Define se o provedor é Azure (true) ou OpenAI público (false). Mantém true para este projeto.
- AzureOpenAI:Endpoint (string): URL base do recurso Azure OpenAI (ex.: `https://meu-recurso-openai.openai.azure.com/`).
- AzureOpenAI:Deployment (string): Nome do deployment criado no Azure (ex.: `gpt-4o-mini`, `gpt-4o`, `o3-mini`). Deve existir no recurso.
- AzureOpenAI:ApiVersion (string): Versão da API usada (ex.: `2024-08-01-preview`). Use uma versão suportada pelo modelo.
- AzureOpenAI:ApiKey (string): Chave de acesso do recurso Azure OpenAI (NUNCA commitar). Obtenha em: Azure Portal → Recurso OpenAI → Keys & Endpoint.

### Como configurar localmente (User Secrets)
No diretório do projeto (onde está o `.csproj`):
```bash
dotnet user-secrets init

dotnet user-secrets set "AzureOpenAI:UseAzure" "true"
dotnet user-secrets set "AzureOpenAI:Endpoint" "https://meu-recurso.openai.azure.com/"
dotnet user-secrets set "AzureOpenAI:Deployment" "gpt-4o-mini"
dotnet user-secrets set "AzureOpenAI:ApiVersion" "2024-08-01-preview"
dotnet user-secrets set "AzureOpenAI:ApiKey" "<sua-chave>"
```

### Variáveis de Ambiente (alternativa)
```bash
export AzureOpenAI__UseAzure=true
export AzureOpenAI__Endpoint="https://meu-recurso.openai.azure.com/"
export AzureOpenAI__Deployment="gpt-4o-mini"
export AzureOpenAI__ApiVersion="2024-08-01-preview"
export AzureOpenAI__ApiKey="<sua-chave>"
```
(Em Windows PowerShell use: `$Env:AzureOpenAI__ApiKey="<sua-chave>"` etc.)

### GitHub Actions / CI
Defina os secrets no repositório (Settings → Secrets and variables → Actions → New repository secret):
- AZURE_OPENAI_ENDPOINT
- AZURE_OPENAI_DEPLOYMENT
- AZURE_OPENAI_API_VERSION
- AZURE_OPENAI_API_KEY

No workflow:
```yaml
env:
  AzureOpenAI__UseAzure: "true"
  AzureOpenAI__Endpoint: "${{ secrets.AZURE_OPENAI_ENDPOINT }}"
  AzureOpenAI__Deployment: "${{ secrets.AZURE_OPENAI_DEPLOYMENT }}"
  AzureOpenAI__ApiVersion: "${{ secrets.AZURE_OPENAI_API_VERSION }}"
  AzureOpenAI__ApiKey: "${{ secrets.AZURE_OPENAI_API_KEY }}"
```

### Boas Práticas
- Nunca commitar `appsettings.Production.json` com chave secreta.
- Rotacionar a chave periodicamente no Azure.
- Validar se o deployment suporta o modelo escolhido (limites de tokens, versões).
- Tratar erros de quota / rate limit (não implementado ainda — sugestão futura).

## Extensão / Próximos Passos (Sugestões)

- Implementar `IExamRepository` persistente.
- Adicionar autenticação (JWT / API Key).
- Endpoint para última submissão (`GetLatestSubmissionAsync` já existe no repositório).
- Seeds multilíngues.
- Métricas agregadas (ex.: acertos por objetivo).
- Export de relatórios (CSV / PDF).
- Tratamento de exceções específicas da Azure OpenAI (throttling / quota).

## Contato / Contribuição

- Email: dantaslucas337@gmail.com  
- LinkedIn: [Lucas Dantas](https://www.linkedin.com/in/lucas-dantas-6837b9227/)  

---
