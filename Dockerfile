# syntax=docker/dockerfile:1.7
#
# Areg backend — Docker image.
#
# Multi-stage: build with the .NET 10 SDK image, publish to a minimal
# /app/publish layer, then run on the .NET 10 ASP.NET Core runtime
# image as a non-root user. The image bundles only the Api project's
# publish output; tests, tools, docs, ESP32 firmware, dev DB files,
# audio blobs, and .claude/ are all excluded by .dockerignore.
#
# Build context: repository root. From the repo root run:
#   docker build -t areg-backend:dev .
#
# See docs/deploy.md for required env vars (OpenAI__ApiKey, JWT key,
# optional Google + metrics tokens), volume mount layout, and a
# concrete `docker run` invocation that wires SQLite + audio blobs
# to a single host volume.

# ---------- Stage 1: build ----------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy project manifests first so `dotnet restore` is cacheable
# independent of source edits. The path layout mirrors the repo so
# project-to-project references resolve identically inside the
# container.
COPY backend/src/ArmenianAiToy.Domain/ArmenianAiToy.Domain.csproj backend/src/ArmenianAiToy.Domain/
COPY backend/src/ArmenianAiToy.Application/ArmenianAiToy.Application.csproj backend/src/ArmenianAiToy.Application/
COPY backend/src/ArmenianAiToy.Infrastructure/ArmenianAiToy.Infrastructure.csproj backend/src/ArmenianAiToy.Infrastructure/
COPY backend/src/ArmenianAiToy.Api/ArmenianAiToy.Api.csproj backend/src/ArmenianAiToy.Api/

# Restore only the Api project graph — this transitively restores
# Domain / Application / Infrastructure but skips the test projects
# and the tools/ tree (neither belongs in the runtime image).
RUN dotnet restore backend/src/ArmenianAiToy.Api/ArmenianAiToy.Api.csproj

# Now bring in the full source for the four production projects.
# Anything outside these four directories is filtered by
# .dockerignore (which keeps tests/, tools/, docs/, esp32/,
# audio-blobs/, *.db, .git, .claude out of the build context).
COPY backend/src/ArmenianAiToy.Domain/ backend/src/ArmenianAiToy.Domain/
COPY backend/src/ArmenianAiToy.Application/ backend/src/ArmenianAiToy.Application/
COPY backend/src/ArmenianAiToy.Infrastructure/ backend/src/ArmenianAiToy.Infrastructure/
COPY backend/src/ArmenianAiToy.Api/ backend/src/ArmenianAiToy.Api/

# Publish a self-contained-by-runtime, framework-dependent build.
# /p:UseAppHost=false skips emitting the platform-specific native
# launcher; we invoke via `dotnet ArmenianAiToy.Api.dll` so the
# launcher is dead weight that would only inflate the layer.
RUN dotnet publish backend/src/ArmenianAiToy.Api/ArmenianAiToy.Api.csproj \
    -c Release \
    -o /app/publish \
    /p:UseAppHost=false \
    --no-restore

# ---------- Stage 2: runtime --------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime

# The base aspnet image already creates a non-root user identified
# by $APP_UID (1654 at time of writing) and a /app workdir. We
# stay on that user — never run as root. /data is the single
# volume mount point for ALL persistent state the app writes:
# SQLite DB files and the C1 audio-blob store. Owned by APP_UID so
# the non-root user can write to a mounted host directory.
USER root
RUN mkdir -p /data /data/audio-blobs && \
    chown -R $APP_UID:$APP_UID /data
USER $APP_UID

WORKDIR /app
COPY --from=build --chown=$APP_UID:$APP_UID /app/publish .

# Bind on 0.0.0.0:8080 (container-internal; the host port is the
# operator's choice via `docker run -p <host>:8080`). ASPNETCORE_URLS
# overrides the `Urls` key in appsettings.json without us having to
# patch the file. The base aspnet:10 image already defaults to
# 8080 in recent revisions, but we set it explicitly so the
# behavior is the same on older base-image patch levels.
ENV ASPNETCORE_URLS=http://0.0.0.0:8080 \
    DOTNET_RUNNING_IN_CONTAINER=true \
    DOTNET_USE_POLLING_FILE_WATCHER=false \
    Database__ConnectionString="Data Source=/data/armenian_ai_toy.db" \
    Audio__BlobStoreRoot=/data/audio-blobs

EXPOSE 8080
VOLUME ["/data"]

# Note on healthchecks: the API exposes GET /api/health (200 ok /
# 503 unhealthy). We deliberately do NOT bake a HEALTHCHECK
# directive into the image — health-probing strategy is an
# operator concern (Docker, k8s, an external monitor); the image
# stays neutral.

ENTRYPOINT ["dotnet", "ArmenianAiToy.Api.dll"]
