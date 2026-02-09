# API Roadmap - ASPEC Capture BFF

Planejamento de evolução da API Backend for Frontend do ASPEC Capture PWA.

## 📋 Status Atual (v1.0.0)

### ✅ Implementado

#### Endpoints
- `POST /api/storage/presigned-url` - Geração de URLs pré-assinadas
- `GET /api/storage/exists/{*filePath}` - Verificação de existência de objetos

#### Funcionalidades
- Geração de URLs pré-assinadas para upload no S3
- Configuração automática de CORS no S3
- Sanitização de nomes de arquivo (remove acentos, espaços)
- Validação de tipos de arquivo
- Logs estruturados
- Swagger UI para documentação

#### Segurança
- CORS configurado para origens específicas
- Credenciais AWS protegidas no servidor
- URLs com expiração de 10 minutos
- Validação de requests

---

## 🎯 Roadmap

### Fase 1: Autenticação e Autorização (v1.1.0) - Q1 2024

#### Objetivos
Implementar autenticação JWT e autorização baseada em roles para proteger endpoints.

#### Tarefas

**1.1 Autenticação JWT**
- [ ] Endpoint `POST /api/auth/login`
  - Valida credenciais contra banco de dados
  - Retorna JWT access token + refresh token
  - Expira em 1 hora (access) e 7 dias (refresh)
- [ ] Endpoint `POST /api/auth/refresh`
  - Valida refresh token
  - Retorna novo access token
- [ ] Endpoint `POST /api/auth/logout`
  - Invalida refresh token
- [ ] Middleware de autenticação JWT
  - Valida token em cada request
  - Extrai claims do usuário

**1.2 Autorização**
- [ ] Implementar roles: `Admin`, `Fiscal`, `Viewer`
- [ ] Policy-based authorization
- [ ] Validação de acesso a unidades gestoras
- [ ] Audit log de acessos

**1.3 Modelos**
```csharp
public record LoginRequest(string Username, string Password);
public record LoginResponse(string AccessToken, string RefreshToken, DateTime ExpiresAt);
public record RefreshRequest(string RefreshToken);
```

**1.4 Segurança**
- [ ] Rate limiting (10 requests/min por IP)
- [ ] Proteção contra brute force
- [ ] Logging de tentativas de login
- [ ] Blacklist de tokens revogados

---

### Fase 2: Gestão de Usuários (v1.2.0) - Q1 2024

#### Objetivos
CRUD completo de usuários e gestão de perfis.

#### Tarefas

**2.1 Endpoints de Usuários**
- [ ] `POST /api/users` - Criar usuário (Admin only)
- [ ] `GET /api/users` - Listar usuários (Admin only)
- [ ] `GET /api/users/{id}` - Obter usuário
- [ ] `PUT /api/users/{id}` - Atualizar usuário
- [ ] `DELETE /api/users/{id}` - Desativar usuário (soft delete)
- [ ] `PUT /api/users/{id}/password` - Alterar senha
- [ ] `PUT /api/users/{id}/units` - Atualizar unidades gestoras

**2.2 Validações**
- [ ] Email único
- [ ] Username único
- [ ] Senha forte (mínimo 8 caracteres, maiúscula, número, especial)
- [ ] Validação de unidades gestoras existentes

**2.3 Modelos**
```csharp
public record CreateUserRequest(
    string Username,
    string Email,
    string FirstName,
    string LastName,
    string Password,
    List<int> UnitIds,
    string Role
);

public record UserResponse(
    long Id,
    string Username,
    string Email,
    string FirstName,
    string LastName,
    List<int> UnitIds,
    string Role,
    DateTime CreatedAt,
    DateTime? LastLogin
);
```

---

### Fase 3: Gestão de Unidades Gestoras (v1.3.0) - Q2 2024

#### Objetivos
CRUD de estados, cidades e unidades gestoras.

#### Tarefas

**3.1 Endpoints de Estados**
- [ ] `GET /api/states` - Listar estados
- [ ] `POST /api/states` - Criar estado (Admin only)
- [ ] `PUT /api/states/{id}` - Atualizar estado
- [ ] `DELETE /api/states/{id}` - Remover estado

**3.2 Endpoints de Cidades**
- [ ] `GET /api/cities` - Listar cidades
- [ ] `GET /api/cities?stateId={id}` - Filtrar por estado
- [ ] `POST /api/cities` - Criar cidade
- [ ] `PUT /api/cities/{id}` - Atualizar cidade
- [ ] `DELETE /api/cities/{id}` - Remover cidade

**3.3 Endpoints de Unidades**
- [ ] `GET /api/units` - Listar unidades
- [ ] `GET /api/units?cityId={id}` - Filtrar por cidade
- [ ] `GET /api/units/{id}` - Obter unidade
- [ ] `POST /api/units` - Criar unidade
- [ ] `PUT /api/units/{id}` - Atualizar unidade
- [ ] `DELETE /api/units/{id}` - Remover unidade

**3.4 Hierarquia**
- [ ] Validação de hierarquia (Estado → Cidade → Unidade)
- [ ] Cascade delete (opcional)
- [ ] Busca hierárquica

---

### Fase 4: Gestão de Inventário (v1.4.0) - Q2 2024

#### Objetivos
CRUD de itens de inventário e sincronização.

#### Tarefas

**4.1 Endpoints de Itens**
- [ ] `GET /api/items` - Listar itens do usuário
- [ ] `GET /api/items/{id}` - Obter item
- [ ] `POST /api/items` - Criar item
- [ ] `PUT /api/items/{id}` - Atualizar item
- [ ] `DELETE /api/items/{id}` - Remover item
- [ ] `POST /api/items/batch` - Criar múltiplos itens

**4.2 Sincronização**
- [ ] `POST /api/items/{id}/sync` - Sincronizar item individual
- [ ] `POST /api/items/sync-batch` - Sincronizar múltiplos itens
- [ ] `GET /api/items/pending` - Listar itens pendentes de sync
- [ ] Webhook para notificar Desktop Harbour

**4.3 Busca e Filtros**
- [ ] `GET /api/items/search?q={query}` - Busca full-text
- [ ] Filtros: categoria, unidade, status, data
- [ ] Ordenação: nome, código, data
- [ ] Paginação (page, pageSize)

**4.4 Validações**
- [ ] Código patrimonial único por unidade
- [ ] Validação contra inventário oficial
- [ ] Limite de fotos por item (5)
- [ ] Tamanho máximo de foto (5MB)

---

### Fase 5: Inventário Oficial (v1.5.0) - Q3 2024

#### Objetivos
Gestão de inventários oficiais das unidades gestoras.

#### Tarefas

**5.1 Endpoints**
- [ ] `GET /api/inventory/{unitId}` - Obter inventário oficial
- [ ] `POST /api/inventory/{unitId}` - Criar/atualizar inventário
- [ ] `POST /api/inventory/{unitId}/items` - Adicionar itens
- [ ] `DELETE /api/inventory/{unitId}/items/{code}` - Remover item
- [ ] `GET /api/inventory/{unitId}/validate/{code}` - Validar código

**5.2 Importação**
- [ ] `POST /api/inventory/{unitId}/import` - Importar CSV/Excel
- [ ] Validação de formato
- [ ] Processamento assíncrono
- [ ] Relatório de importação

**5.3 Sincronização com S3**
- [ ] Upload automático de `inventarios/{unitId}.json`
- [ ] Versionamento de inventários
- [ ] Histórico de alterações

---

### Fase 6: Relatórios e Analytics (v1.6.0) - Q3 2024

#### Objetivos
Endpoints para relatórios e estatísticas.

#### Tarefas

**6.1 Estatísticas**
- [ ] `GET /api/stats/dashboard` - Dashboard geral
- [ ] `GET /api/stats/unit/{id}` - Estatísticas por unidade
- [ ] `GET /api/stats/user/{id}` - Estatísticas por usuário
- [ ] `GET /api/stats/sync` - Status de sincronização

**6.2 Relatórios**
- [ ] `GET /api/reports/items` - Relatório de itens (CSV/PDF)
- [ ] `GET /api/reports/sync` - Relatório de sincronização
- [ ] `GET /api/reports/audit` - Relatório de auditoria
- [ ] Agendamento de relatórios

**6.3 Métricas**
- [ ] Total de itens por unidade
- [ ] Taxa de sincronização
- [ ] Itens pendentes
- [ ] Usuários ativos
- [ ] Tempo médio de captura

---

### Fase 7: Notificações e Webhooks (v1.7.0) - Q4 2024

#### Objetivos
Sistema de notificações e integração via webhooks.

#### Tarefas

**7.1 Notificações**
- [ ] `GET /api/notifications` - Listar notificações
- [ ] `PUT /api/notifications/{id}/read` - Marcar como lida
- [ ] `DELETE /api/notifications/{id}` - Remover notificação
- [ ] Push notifications (Firebase Cloud Messaging)

**7.2 Webhooks**
- [ ] `POST /api/webhooks` - Registrar webhook
- [ ] `GET /api/webhooks` - Listar webhooks
- [ ] `DELETE /api/webhooks/{id}` - Remover webhook
- [ ] Eventos: item_created, item_synced, inventory_updated

**7.3 Eventos**
- [ ] Sistema de eventos assíncrono
- [ ] Retry automático de webhooks
- [ ] Logs de entrega

---

### Fase 8: Performance e Escalabilidade (v2.0.0) - Q4 2024

#### Objetivos
Otimizações de performance e preparação para escala.

#### Tarefas

**8.1 Caching**
- [ ] Redis para cache de inventários
- [ ] Cache de usuários e unidades
- [ ] Invalidação inteligente
- [ ] Cache distribuído

**8.2 Database**
- [ ] Migração para PostgreSQL/SQL Server
- [ ] Índices otimizados
- [ ] Particionamento de tabelas
- [ ] Read replicas

**8.3 Observabilidade**
- [ ] Application Insights
- [ ] Structured logging (Serilog)
- [ ] Distributed tracing (OpenTelemetry)
- [ ] Health checks avançados
- [ ] Métricas de performance

**8.4 Escalabilidade**
- [ ] Containerização (Docker)
- [ ] Kubernetes deployment
- [ ] Auto-scaling
- [ ] Load balancing
- [ ] CDN para assets

---

## 🔧 Melhorias Técnicas Contínuas

### Qualidade de Código
- [ ] Testes unitários (xUnit) - Cobertura 80%+
- [ ] Testes de integração
- [ ] Testes de carga (k6)
- [ ] Code coverage reports
- [ ] Static code analysis (SonarQube)

### Segurança
- [ ] OWASP Top 10 compliance
- [ ] Penetration testing
- [ ] Dependency scanning
- [ ] Secrets management (Azure Key Vault)
- [ ] Security headers

### DevOps
- [ ] CI/CD pipeline (GitHub Actions)
- [ ] Automated deployments
- [ ] Blue-green deployment
- [ ] Rollback automático
- [ ] Infrastructure as Code (Terraform)

### Documentação
- [ ] OpenAPI 3.0 spec completa
- [ ] Postman collection
- [ ] SDK generation (C#, TypeScript)
- [ ] Developer portal
- [ ] Runbooks operacionais

---

## 📊 Métricas de Sucesso

### Performance
- Tempo de resposta < 200ms (p95)
- Throughput > 1000 req/s
- Disponibilidade > 99.9%
- Erro rate < 0.1%

### Qualidade
- Code coverage > 80%
- Zero vulnerabilidades críticas
- Technical debt < 5%
- Documentation coverage 100%

### Adoção
- 100+ usuários ativos
- 10k+ itens sincronizados/dia
- Taxa de erro de sync < 1%
- NPS > 8.0

---

## 🗓️ Timeline

```
Q1 2024: Autenticação + Usuários (v1.1-1.2)
Q2 2024: Unidades + Inventário (v1.3-1.4)
Q3 2024: Inventário Oficial + Relatórios (v1.5-1.6)
Q4 2024: Notificações + Performance (v1.7-2.0)
```

---

## 📝 Notas

### Prioridades
1. **P0 (Crítico)**: Autenticação, Segurança
2. **P1 (Alto)**: CRUD de entidades, Sincronização
3. **P2 (Médio)**: Relatórios, Notificações
4. **P3 (Baixo)**: Otimizações, Nice-to-have

### Dependências
- PostgreSQL/SQL Server para produção
- Redis para caching
- Azure/AWS para infraestrutura
- Application Insights para observabilidade

### Riscos
- Migração de dados do localStorage para banco
- Compatibilidade com Desktop Harbour
- Performance com grande volume de dados
- Custos de infraestrutura AWS

---

**Última Atualização:** 2024-02-09  
**Versão Atual:** 1.0.0  
**Próxima Release:** 1.1.0 (Autenticação JWT)
