# MsFundamentals.Trainer – Documentacao Tecnica

## Visao geral
API minimalista em .NET 8 (Minimal API) para criar exames das trilhas AZ-900, AI-900 e DP-900, receber respostas e gerar analises com apoio de LLM (Azure OpenAI ou OpenAI). Armazenamento em memoria (IMemoryCache + repositório em memoria).

## Arquitetura e modulos
- **Program.cs**: bootstrap Minimal API, injecoes de dependencia, Swagger, TelemetryMiddleware, carga de seeds, endpoints REST.
- **Services**
  - `ExamService`: monta provas a partir do banco em cache; aciona LLM apenas para completar lacunas; garante IDs sequenciais; usa schema compacto (toon) na geracao.
  - `FeedbackService`: corrige localmente, aplica bypass de IA em scores altos, limita questoes erradas enviadas conforme orcamento de tokens, chama LLM para analise enriquecida e faz fallback seguro.
  - `AiPromptBuilder`: construcao de prompts curtos e minificados para geracao de questoes e analise.
  - `CacheService`: wrapper de IMemoryCache com TTL configuravel.
  - `SeedLoader`: localiza e carrega seeds de questoes (AZ/AI/DP) de caminhos candidatos, sanitiza e coloca no cache.
  - `QuestionBankUtilities`: saneamento (opcoes A-D, alternativa correta valida, dedupe por stem+objetivo) e deduplicacao.
  - `TokenEstimator`: estimativa grosseira de tokens (~4 chars/token) para budget antes de chamar LLM.
- **Infrastructure**
  - `Gpt5Client`: cliente HTTP para Azure OpenAI/OpenAI; escolhe response_format por chamada (schema toon somente para geracao de questoes); coleta tokens in/out.
  - `TelemetryMiddleware`: registra tokens e tempo de requisicao; placeholder de custo por milhao.
  - `ToonCodec`: schema compacto (chaves curtas) e normalizacao de respostas toon para modelo interno.
- **Repositories**
  - `IExamRepository` + `InMemoryExamRepository`: armazenamento volatil de exames e submissões; recupera ultima submissao.
- **Models/DTOs/Validation**: contratos de dominio, DTOs de requisicao/resposta e validadores FluentValidation.

## Fluxos principais
- **Carga de seeds**: `SeedLoader` procura arquivos em `seed/` ou raiz, sanitiza e coloca no cache com chave `BANK::<track>::pt-BR`.
- **Criar exame (POST /exams)**: `ExamService` amostra questoes balanceadas por dificuldade; se faltar, chama LLM com schema toon e normaliza; ID sequencial Q1..Qn; resposta inclui gabarito (para administracao).
- **Obter exame (GET /exams/{id})**: entrega prova sem gabarito.
- **Submeter respostas (POST /exams/{id}/submissions)**: valida, salva submissao em memoria.
- **Analise (POST /submissions/{id}/analysis)**: corrige localmente, calcula score, aplica bypass se >=90 em modo light; seleciona subconjunto de erradas respeitando orcamento; chama LLM (response_format json_object); reforca respostas e fallback local.
- **Ping IA (GET /ai/ping)**: health-check da integracao LLM.
- **Seeds diagnostics**: `/seed/status` e `/seed/diag`.

## Padroes e decisoes
- Minimal API + DI nativo; validação com FluentValidation; DTOs simples e modelos imutaveis com required.
- Cache centralizado com TTL; dedupe de questoes por stem+objetivo para evitar repeticao.
- Cliente LLM unico com separacao de schema de resposta (toon so para geracao, json object para analise/ping).
- Opt-out de IA quando possivel (scores altos) para economizar tokens.

## Estrategias de performance
- Uso de `IMemoryCache` para seeds e provas; clones ao usar o banco para evitar mutacao concorrente.
- Balanceamento de dificuldades na amostra; fallback local se IA falhar.
- Limite de tempo de HTTP client (60s) e reuse de HttpClient via factory.

## Estrategias de economia de tokens
- Prompt de geracao: schema toon (chaves curtas) via `response_format` no Azure para reduzir saida.
- Prompt de analise: JSON minificado sem indentacao; instrucoes curtas e declarativas.
- Orcamento preventivo: `TokenEstimator` soma tokens estimados do prompt e reduz quantidade de questoes erradas enviada (limite dinamico) ate caber no budget.
- Bypass de IA: quando score >= 90 e modo nao for deep, retorna analise local sem tokens.
- Normalizacao e validacao de respostas: evita re-chamadas em caso de JSON invalido, usando fallback local.
- Modelos: configuravel; usar deployments mais baratos (ex.: gpt-4o-mini) para analises curtas.

## Boas praticas aplicadas
- Sem armazenamento duravel; superficie pequena e clara.
- Validação antecipada de entradas.
- Logs de telemetria com tokens in/out por rota.
- Separacao de responsabilidades (servicos, infra, repositorio, utilitarios).
- Strings e prompts em portugues-BR com foco em concisao.

## Pontos de extensao
- **Persistencia**: implementar outro `IExamRepository` (EF Core/Postgres, Mongo, Redis) e registrar no Program.
- **Seguranca**: adicionar autenticacao/autorizacao e rate limiting.
- **Internacionalizacao**: novos idiomas de seeds; chave de cache já leva idioma.
- **Observabilidade**: ajustar precificacao de tokens e enviar metrica para APM.
- **Melhorias de IA**: schemas dedicados para analise, limites configuraveis de questoes erradas, reuso de prompts no cache.

## Referencias de configuracao
- `appsettings.json` espera AzureOpenAI (UseAzure, Endpoint, Deployment, ApiVersion, ApiKey) e OpenAI fallback.
- `Cache:DefaultTtlMinutes` controla TTL padrao.
- Uso de User Secrets em desenvolvimento.

## Execucao local
```bash
dotnet restore
dotnet run --project src/MsFundamentals.Trainer.csproj
```
Acesse Swagger em `http://localhost:6074/swagger`.
