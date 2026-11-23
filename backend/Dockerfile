# The API image (PLAN.md Phase 6). Also runs one-shot commands: `migrate`, `import-wikidata`, ...
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
# Restore first, so package downloads are cached until a project file changes.
COPY Directory.Build.props ./
COPY src/ArmoryWiki.Domain/ArmoryWiki.Domain.csproj src/ArmoryWiki.Domain/
COPY src/ArmoryWiki.Infrastructure/ArmoryWiki.Infrastructure.csproj src/ArmoryWiki.Infrastructure/
COPY src/ArmoryWiki.Api/ArmoryWiki.Api.csproj src/ArmoryWiki.Api/
RUN dotnet restore src/ArmoryWiki.Api/ArmoryWiki.Api.csproj -r linux-x64
COPY src/ src/
# linux-x64 only: without it every platform's SkiaSharp native library is published (about 700 MB more).
RUN dotnet publish src/ArmoryWiki.Api/ArmoryWiki.Api.csproj -c Release -r linux-x64 --self-contained false --no-restore -o /app

# Debian-based, so SkiaSharp's Linux native assets load (ADR-0006).
FROM mcr.microsoft.com/dotnet/aspnet:10.0
# curl only for the container health check.
RUN apt-get update && apt-get install -y --no-install-recommends curl && rm -rf /var/lib/apt/lists/* \
    && mkdir -p /data/media /data/keys /app/import-reports && chown -R app:app /data /app
WORKDIR /app
COPY --from=build --chown=app:app /app .
USER app
ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    Media__Root=/data/media \
    DataProtection__KeysPath=/data/keys
EXPOSE 8080
HEALTHCHECK --interval=30s --timeout=5s --start-period=20s --retries=3 CMD curl -fsS http://localhost:8080/healthz || exit 1
ENTRYPOINT ["dotnet", "ArmoryWiki.Api.dll"]
