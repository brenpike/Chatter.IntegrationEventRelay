FROM mcr.microsoft.com/dotnet/runtime:6.0-bullseye-slim AS base
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:6.0-bullseye-slim AS publish
WORKDIR /src
COPY ["*.sln", "./"]
COPY ["./Chatter.IntegrationEventRelay.Worker/src/Chatter.IntegrationEventRelay.Worker.csproj", "./Chatter.IntegrationEventRelay.Worker/src/Chatter.IntegrationEventRelay.Worker.csproj"]
COPY ["./Chatter.IntegrationEventRelay.Consumer/src/Chatter.IntegrationEventRelay.Consumer.csproj", "./Chatter.IntegrationEventRelay.Consumer/src/Chatter.IntegrationEventRelay.Consumer.csproj"]
COPY ["./Chatter.IntegrationEventRelay.Core/src/Chatter.IntegrationEventRelay.Core.csproj", "./Chatter.IntegrationEventRelay.Core/src/Chatter.IntegrationEventRelay.Core.csproj"]
COPY ["./Chatter.IntegrationEventRelay.Core/tests/Chatter.IntegrationEventRelay.Core.Tests.csproj", "./Chatter.IntegrationEventRelay.Core/tests/Chatter.IntegrationEventRelay.Core.Tests.csproj"]
RUN dotnet restore

COPY ["./Chatter.IntegrationEventRelay.Worker/src/", "./Chatter.IntegrationEventRelay.Worker/src/"]
COPY ["./Chatter.IntegrationEventRelay.Core/src/", "./Chatter.IntegrationEventRelay.Core/src/"]
COPY ["./Chatter.IntegrationEventRelay.Core/tests/", "./Chatter.IntegrationEventRelay.Core/tests/"]

RUN dotnet test "./Chatter.IntegrationEventRelay.Core/tests/Chatter.IntegrationEventRelay.Core.Tests.csproj" -c Release --no-build --no-restore

RUN dotnet publish "./Chatter.IntegrationEventRelay.Worker/src/Chatter.IntegrationEventRelay.Worker.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet","Chatter.IntegrationEventRelay.Worker.dll"]

