# syntax=docker/dockerfile:1.7
ARG SDK_VERSION=10.0
ARG RUNTIME_VERSION=10.0

FROM mcr.microsoft.com/dotnet/sdk:${SDK_VERSION}-alpine AS build
WORKDIR /src
COPY . .
RUN dotnet restore "TriageService.csproj"
RUN dotnet publish "TriageService.csproj" -c Release -o /app/publish /p:UseAppHost=false \
    /p:ContinuousIntegrationBuild=true \
    /p:Deterministic=true \
    --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:${RUNTIME_VERSION}-alpine AS final
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8005
ENV DOTNET_RUNNING_IN_CONTAINER=true
ENV DOTNET_NOLOGO=true
ENV ASPNETCORE_FORWARDEDHEADERS_ENABLED=true
RUN addgroup -g 10001 -S appuser && adduser -u 10001 -S appuser -G appuser
USER 10001:10001
COPY --from=build --chown=appuser:appuser /app/publish .
EXPOSE 8005
HEALTHCHECK --interval=15s --timeout=3s --start-period=10s --retries=3 \
    CMD wget --quiet --tries=1 --spider http://localhost:8005/health || exit 1
ENTRYPOINT ["dotnet", "TriageService.dll"]
