# Build and publish the API.
#
# Multi-stage so the runtime image carries no SDK, no source and no NuGet
# cache: a smaller image is a smaller thing to keep patched, and source in a
# production image is source an attacker can read.
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Project files first, so a source-only change does not re-run restore.
COPY global.json ./
COPY Directory.Build.props ./
COPY src/Wasta.Ai/Wasta.Ai.csproj                     src/Wasta.Ai/
COPY src/Wasta.CareerCoach/Wasta.CareerCoach.csproj   src/Wasta.CareerCoach/
COPY src/Wasta.SupportChat/Wasta.SupportChat.csproj   src/Wasta.SupportChat/
COPY src/Wasta.Domain/Wasta.Domain.csproj             src/Wasta.Domain/
COPY src/Wasta.Application/Wasta.Application.csproj   src/Wasta.Application/
COPY src/Wasta.Infrastructure/Wasta.Infrastructure.csproj src/Wasta.Infrastructure/
COPY src/Wasta.WebApi/Wasta.WebApi.csproj             src/Wasta.WebApi/
RUN dotnet restore src/Wasta.WebApi/Wasta.WebApi.csproj

COPY src/ src/
RUN dotnet publish src/Wasta.WebApi/Wasta.WebApi.csproj \
    -c Release -o /app --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app

# Non-root. The API writes nothing to its own directory, so it has no reason to
# own one - and a container that cannot write to itself is one less way for a
# file-upload bug to become code execution.
RUN useradd --uid 64198 --create-home --shell /usr/sbin/nologin wasta \
    && mkdir -p /var/lib/wasta/uploads \
    && chown -R wasta:wasta /var/lib/wasta
USER wasta

ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_RUNNING_IN_CONTAINER=true \
    FileStorage__RootPath=/var/lib/wasta/uploads

EXPOSE 8080

# Liveness only. Readiness touches the database, and a container that restarts
# itself every time the database blips makes an outage worse.
HEALTHCHECK --interval=30s --timeout=3s --start-period=20s --retries=3 \
    CMD ["/bin/sh", "-c", "wget -qO- http://127.0.0.1:8080/health/live || exit 1"]

COPY --from=build --chown=wasta:wasta /app ./
ENTRYPOINT ["dotnet", "Wasta.WebApi.dll"]
