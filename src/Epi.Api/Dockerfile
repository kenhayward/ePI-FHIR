# Build and run the ePI API. Multi-stage so the runtime image carries no SDK or source.
# The same image runs under Docker Compose and on Kubernetes (D3 Section 10, ADR-014).
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /source

# Restore against the manifests alone first, so a source-only change reuses the cached layer.
COPY global.json Directory.Build.props EpiPlatform.sln ./
COPY src/ ./src/
RUN dotnet restore src/Epi.Api/Epi.Api.csproj
RUN dotnet publish src/Epi.Api/Epi.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app
COPY --from=build /app ./

# Configuration and pinned conformance packages are read at start-up. They are mounted rather
# than copied so that changing a market, or the identifier authority, is a configuration change
# and not a rebuild (ADR-012, ADR-017).
ENV Epi__MarketsPath=/config/markets \
    Epi__IdentifiersPath=/config/identifiers.json \
    Epi__Lifecycle__StatesPath=/config/lifecycle/label-states.json \
    Epi__Lifecycle__MarketStatesPath=/config/lifecycle/market-approval-states.json \
    Epi__Validation__PackagesPath=/app/profiles/packages \
    ASPNETCORE_HTTP_PORTS=8080

# Never root: the process needs no privileges it cannot be given explicitly.
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "Epi.Api.dll"]
