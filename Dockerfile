# ============================================
# Stage 1: Build
# ============================================
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copiar arquivo de projeto e restaurar dependências
COPY ["AspecCapturaApi.csproj", "./"]
RUN dotnet restore "AspecCapturaApi.csproj"

# Copiar todo o código fonte e compilar
COPY . .
RUN dotnet build "AspecCapturaApi.csproj" -c Release -o /app/build

# ============================================
# Stage 2: Publish
# ============================================
FROM build AS publish
RUN dotnet publish "AspecCapturaApi.csproj" -c Release -o /app/publish /p:UseAppHost=false

# ============================================
# Stage 3: Runtime
# ============================================
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app

# Criar usuário não-root para segurança
RUN addgroup --system --gid 1001 appuser && \
    adduser --system --uid 1001 --ingroup appuser appuser

# Copiar arquivos publicados do stage anterior
COPY --from=publish /app/publish .

# Configurar permissões
RUN chown -R appuser:appuser /app

# Mudar para usuário não-root
USER appuser

# Expor porta (Render usa a variável de ambiente PORT)
EXPOSE 8080

# Configurar variáveis de ambiente
ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_RUNNING_IN_CONTAINER=true \
    DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false

# Entrypoint
ENTRYPOINT ["dotnet", "AspecCapturaApi.dll"]
