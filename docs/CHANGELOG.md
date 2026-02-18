# Registro de alterações

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
