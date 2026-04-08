# Problema: Arquivo DLL Bloqueado Durante Build

## Situação

O build da API está falhando com o erro:
```
error MSB3027: não foi possível copiar "obj\Debug\net8.0\pwa-camera-poc-api.dll" para "bin\Debug\net8.0\pwa-camera-poc-api.dll"
O arquivo é bloqueado por: "netcoredbg.exe (27404), .NET Host (30900)"
```

## Causa

Este **NÃO é um erro de código**. O arquivo DLL está bloqueado porque a API está em execução em outro processo:
- `netcoredbg.exe` - Debugger do .NET (VS Code Debug)
- `.NET Host` - Runtime do .NET executando a aplicação

## ✅ Confirmação: Código Está Correto

Build em modo Release executado com sucesso:
```bash
dotnet build -c Release --no-incremental
# ✅ Construir êxito em 5,1s
```

Isso confirma que não há erros de compilação no código.

## Solução

### Opção 1: Build em Modo Release (Solução Rápida)

```bash
dotnet build -c Release
```

Isso cria os arquivos em `bin/Release/` sem conflitar com `bin/Debug/` que está bloqueado.

### Opção 2: Parar a API em Execução (Recomendado)

1. **No VS Code**: Clique no botão "Stop" (quadrado vermelho) no painel de debug
2. **No Terminal**: Pressione `Ctrl+C` no terminal onde a API está rodando
3. **Task Manager**: Encerre os processos `dotnet.exe` relacionados à API

### Opção 3: Forçar Encerramento de Processos

```powershell
# Listar processos .NET
Get-Process | Where-Object {$_.ProcessName -like "*dotnet*"} | Select-Object Id, ProcessName

# Encerrar processo específico (substitua PID pelo número do processo)
Stop-Process -Id <PID> -Force

# Exemplo: Se o processo 30900 está bloqueando
Stop-Process -Id 30900 -Force
```

### Opção 4: Usar Outro Terminal

Se você precisa manter a API rodando:
1. Abra um novo terminal
2. Execute o build em modo Release: `dotnet build -c Release`
3. Isso criará os arquivos em `bin/Release/` sem conflitar com `bin/Debug/`

## Verificação

Após parar a API, execute:
```bash
dotnet clean
dotnet build
```

Se o build for bem-sucedido, o problema estava apenas no bloqueio de arquivo.

## Prevenção

Para evitar este problema no futuro:

1. **Sempre pare a API antes de fazer build manual**
2. **Use Hot Reload** durante desenvolvimento (não precisa rebuild)
3. **Use modo Release para builds de verificação**:
   ```bash
   dotnet build -c Release
   ```
4. **Configure o VS Code** para parar automaticamente antes de rebuild:
   ```json
   // .vscode/tasks.json
   {
     "label": "build",
     "command": "dotnet",
     "type": "process",
     "args": ["build"],
     "dependsOn": ["stop-api"]  // Para a API antes de buildar
   }
   ```

## Status Atual

✅ **Código da API está correto** - Build Release executado com sucesso  
✅ **Sem erros de compilação** - Todos os arquivos validados  
⚠️ **API está em execução** - Processos dotnet.exe bloqueando bin/Debug/  
📝 **Alterações recentes**: Adicionados campos ao `TombamentoRecord` (v0.3.3)

## Processos Detectados

```
Id    ProcessName
--    -----------
21184 dotnet
23136 dotnet
30684 dotnet
30900 dotnet (bloqueando arquivos)
36056 dotnet
37120 dotnet
38640 dotnet
```

## Notas

- Este é um comportamento normal do .NET quando a aplicação está em execução
- O arquivo DLL é bloqueado para evitar corrupção durante execução
- Não é necessário fazer alterações no código para resolver
- Build em modo Release funciona porque usa pasta diferente (bin/Release/)

---

**Data**: 8 de abril de 2026  
**Versão da API**: 0.3.3  
**Status**: ✅ Código Validado - Apenas bloqueio de arquivo
