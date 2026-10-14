FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["src/JobForge.Orchestrator/JobForge.Orchestrator.csproj", "src/JobForge.Orchestrator/"]
COPY ["Directory.Packages.props", "./"]
RUN dotnet restore "src/JobForge.Orchestrator/JobForge.Orchestrator.csproj"
COPY . .
RUN dotnet publish "src/JobForge.Orchestrator/JobForge.Orchestrator.csproj" -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
RUN apt-get update && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app .
ENV Kestrel__Endpoints__http__Url=http://+:8094
ENV Kestrel__Endpoints__grpc__Url=http://+:8097
EXPOSE 8094 8097
USER $APP_UID
ENTRYPOINT ["dotnet", "JobForge.Orchestrator.dll"]
