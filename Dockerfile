FROM mcr.microsoft.com/dotnet/sdk:8.0-alpine AS build
WORKDIR /src

COPY CoreFid.Auditoria.sln Directory.Build.props ./
COPY src/Domain/CoreFid.Auditoria.Domain.csproj src/Domain/
COPY src/Application/CoreFid.Auditoria.Application.csproj src/Application/
COPY src/Infrastructure/CoreFid.Auditoria.Infrastructure.csproj src/Infrastructure/
COPY src/Api/CoreFid.Auditoria.Api.csproj src/Api/
RUN dotnet restore src/Api/CoreFid.Auditoria.Api.csproj

COPY src/ src/
RUN dotnet publish src/Api/CoreFid.Auditoria.Api.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0-alpine AS runtime
WORKDIR /app

RUN apk add --no-cache curl \
    && addgroup -S corefid && adduser -S -G corefid -H -s /sbin/nologin corefid

COPY --from=build --chown=corefid:corefid /app/publish .

ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_gcServer=0

USER corefid
EXPOSE 8080

HEALTHCHECK --interval=15s --timeout=3s --start-period=20s --retries=3 \
    CMD curl -fsS http://localhost:8080/healthz || exit 1

ENTRYPOINT ["dotnet", "CoreFid.Auditoria.Api.dll"]
