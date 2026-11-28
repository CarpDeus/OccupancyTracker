FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Monolithic/OccupancyTracker.Shared/OccupancyTracker.Shared.csproj Monolithic/OccupancyTracker.Shared/
COPY Monolithic/OccupancyTracker.Backend/OccupancyTracker.Backend.csproj Monolithic/OccupancyTracker.Backend/
COPY Monolithic/OccupancyTracker.Api/OccupancyTracker.Api.csproj Monolithic/OccupancyTracker.Api/
COPY Monolithic/OccupancyTracker/OccupancyTracker.csproj Monolithic/OccupancyTracker/
RUN dotnet restore Monolithic/OccupancyTracker.Api/OccupancyTracker.Api.csproj \
    && dotnet restore Monolithic/OccupancyTracker/OccupancyTracker.csproj
COPY Monolithic/ Monolithic/

FROM build AS api-publish
RUN dotnet publish Monolithic/OccupancyTracker.Api/OccupancyTracker.Api.csproj \
    -c Release --no-restore -o /out /p:UseAppHost=false

FROM build AS ui-publish
RUN dotnet publish Monolithic/OccupancyTracker/OccupancyTracker.csproj \
    -c Release --no-restore -o /out /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
RUN apt-get update && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/* \
    && mkdir -p /var/lib/occupancy/keys \
    && chown app:app /var/lib/occupancy/keys
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=8080 \
    DataProtection__KeyPath=/var/lib/occupancy/keys
EXPOSE 8080
USER app

FROM runtime AS api
COPY --from=api-publish /out/ ./
HEALTHCHECK --interval=10s --timeout=5s --start-period=60s --retries=3 \
    CMD curl --fail --silent http://localhost:8080/api/ready || exit 1
ENTRYPOINT ["dotnet", "OccupancyTracker.Api.dll"]

FROM runtime AS ui
COPY --from=ui-publish /out/ ./
HEALTHCHECK --interval=10s --timeout=5s --start-period=15s --retries=3 \
    CMD curl --fail --silent http://localhost:8080/ || exit 1
ENTRYPOINT ["dotnet", "OccupancyTracker.dll"]
