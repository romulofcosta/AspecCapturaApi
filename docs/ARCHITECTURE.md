# Arquitetura do Backend (Authentication Broker)

Este documento descreve a arquitetura do serviço de autenticação implementado na Minimal API.

## Visão Geral

O sistema atua como um **Broker de Autenticação**. Em vez de manter um banco de dados centralizado de usuários, ele delega o armazenamento para repositórios JSON individuais hospedados no AWS S3, organizados por prefixos (geralmente representando prefeituras ou unidades gestoras).

## Fluxo de Autenticação

```mermaid
sequenceDiagram
    participant App as PWA (Frontend)
    participant API as Minimal API (Broker)
    participant S3 as AWS S3 (Storage)

    App->>API: POST /api/auth/login { Username, Password }
    Note over API: Parse Username: CE305.joao.silva<br/>Prefix = CE305
    API->>S3: GET usuarios/CE305.json
    S3-->>API: Stream JSON (User List + Tabelas)
    Note over API: Deserializa e Busca 'joao.silva'
    Note over API: Valida Password
    Note over API: Filtra Tombamentos por Esfera
    Note over API: Monta hierarquia Órgão > UO > Área > Subárea
    API-->>App: 200 OK (stream) { NomeCompleto, Orgaos, Tombamentos, Token }
```

## Estrutura de Dados (S3)

Os arquivos devem estar localizados no bucket configurado sob o path:
`usuarios/{PREFIXO}.json`

### Exemplo de Path
`usuarios/CE305.json`

## Segurança (MVP)

> [!IMPORTANT]
> Para esta fase de MVP, as senhas estão sendo comparadas em texto plano. Em versões futuras, recomenda-se a implementação de hashing (BCrypt/Argon2) e o uso de JWT para tokens de sessão.

## Considerações de Implementação

- A desserialização usa `JsonSerializerOptions` com:
  - Case-insensitive + nomes camelCase
  - `AllowTrailingCommas = true` e `ReadCommentHandling = Skip`
- O endpoint de login escreve a resposta via `Results.Stream(...)` para reduzir consumo de memória.
- A compressão HTTP para respostas está habilitada quando em HTTPS.
- Na inicialização, a API tenta aplicar uma configuração de CORS no bucket S3 para permitir uploads via Pre-Signed URL.
