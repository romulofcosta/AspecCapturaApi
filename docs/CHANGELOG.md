# Registro de alterações

## [0.3.1] - 2026-03-04

### Documentação
- **PERFORMANCE_OPTIMIZATION.md**: Análise técnica completa da otimização de chunking
  - Documentada redução de 14 minutos para 15 segundos (56x mais rápido)
  - Comparação de 3 estratégias: Batching Fixo (recomendada), Estimativa de Tamanho, Escrita Incremental
  - Benchmarks detalhados e análise de complexidade (O(n²) → O(n))
  - Guia de configuração e tuning de `MaxItemsPerChunk`
- **TROUBLESHOOTING_SYNC.md**: Guia de resolução de problemas de sincronização
  - Documentação do erro 500 na tela "Baixando Lotes de Tombamentos"
  - Análise de causa raiz (NullReferenceException, falta de tratamento de erro)
  - Correções aplicadas com exemplos de código antes/depois
  - Testes de validação e checklist de verificação
- **MIGRATION_GUIDE_CHUNKING.md**: Guia de migração para batching fixo
  - Instruções passo a passo para migração
  - Comparação de abordagens antigas vs novas

### Melhorias
- Atualizado `.gitignore` para ignorar arquivos do Visual Studio (`.vs/`, `*.user`, `*.suo`)
- Criado `pwa-camera-poc-api.sln` para suporte completo ao Visual Studio

### Correções Anteriores (já aplicadas)
- `BuildOrGetChunkIndexAsync`: Validação de null e uso de `await` ao invés de `.Result`
- `SerializeChunkPayloadAsync`: Uso correto de `UnifiedDataRecord` ao invés de `DeserializeAsyncEnumerable`
- Endpoints `/api/tombamentos/sync-info` e `/api/tombamentos/lote/{id}`: Try-catch completo com logs detalhados

## [0.3.0] - 2026-02-27

### Adicionado
- Resposta do endpoint `POST /api/auth/login` via streaming para maior eficiência.
- Compressão de resposta habilitada (HTTPS) através de `AddResponseCompression`.
- Desserialização mais tolerante: `AllowTrailingCommas` e `ReadCommentHandling.Skip`.
- Auto-configuração de CORS do bucket S3 na inicialização.

### Alterado
- Ajustes nas opções JSON dos Controllers (profundidade ilimitada e buffer aumentado).
- Atualização da documentação para refletir as mudanças acima.

## [0.2.0] - 2026-02-18

### Adicionado
- **Hierarquia Contábil**: Implementada estrutura de dados `Órgão > UO > Área > Subárea` no endpoint de login.
- **S3 Dynamic Key Construction**: 
  - Geração de Pre-signed URLs agora utiliza a hierarquia `{Prefixo}/{IdUO}/` para isolamento de dados.
  - Removidos parâmetros `Username` e `UnitName` do DTO de requisição, utilizando `folderPrefix` dinâmico.
- **Robustez**: Adicionada verificação de placeholders AWS (`AWS__Region`) para evitar falhas catastróficas na inicialização em ambientes de desenvolvimento.

### Alterado
- **Nomenclatura em Português**: Refatorados modelos de dados para usar `Usuario`, `Orgao`, `UnidadeOrcamentaria`, etc., alinhando com o domínio de negócio.
- Implementado o **Authentication Broker** no endpoint `POST /api/auth/login`.
  - Suporte para usernames compostos (ex: `PREFIXO.nome.sobrenome`).
  - Integração com AWS S3 para busca dinâmica de listas de usuários por prefixo de município.
