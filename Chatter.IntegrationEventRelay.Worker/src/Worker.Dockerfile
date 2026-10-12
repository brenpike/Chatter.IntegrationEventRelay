FROM mcr.microsoft.com/dotnet/runtime:10.0 AS base
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS publish
WORKDIR /src
COPY ["*.sln", "./"]
COPY ["./Chatter.IntegrationEventRelay.Worker/src/Chatter.IntegrationEventRelay.Worker.csproj", "./Chatter.IntegrationEventRelay.Worker/src/Chatter.IntegrationEventRelay.Worker.csproj"]
COPY ["./Chatter.IntegrationEventRelay.Consumer/src/Chatter.IntegrationEventRelay.Consumer.csproj", "./Chatter.IntegrationEventRelay.Consumer/src/Chatter.IntegrationEventRelay.Consumer.csproj"]
COPY ["./Chatter.IntegrationEventRelay.Core/src/Chatter.IntegrationEventRelay.Core.csproj", "./Chatter.IntegrationEventRelay.Core/src/Chatter.IntegrationEventRelay.Core.csproj"]
COPY ["./Chatter.IntegrationEventRelay.Core/tests/Chatter.IntegrationEventRelay.Core.Tests.csproj", "./Chatter.IntegrationEventRelay.Core/tests/Chatter.IntegrationEventRelay.Core.Tests.csproj"]
COPY ["./Chatter.IntegrationEventRelay.ServiceBroker/src/Chatter.IntegrationEventRelay.ServiceBroker.csproj", "./Chatter.IntegrationEventRelay.ServiceBroker/src/Chatter.IntegrationEventRelay.ServiceBroker.csproj"]
COPY ["./Chatter.IntegrationEventRelay.ServiceBroker/tests/Chatter.IntegrationEventRelay.ServiceBroker.Tests.csproj", "./Chatter.IntegrationEventRelay.ServiceBroker/tests/Chatter.IntegrationEventRelay.ServiceBroker.Tests.csproj"]
COPY ["./Chatter.IntegrationEventRelay.Cdc/src/Chatter.IntegrationEventRelay.Cdc.csproj", "./Chatter.IntegrationEventRelay.Cdc/src/Chatter.IntegrationEventRelay.Cdc.csproj"]
COPY ["./Chatter.IntegrationEventRelay.Cdc/tests/Chatter.IntegrationEventRelay.Cdc.Tests.csproj", "./Chatter.IntegrationEventRelay.Cdc/tests/Chatter.IntegrationEventRelay.Cdc.Tests.csproj"]
RUN dotnet restore

COPY ["./Chatter.IntegrationEventRelay.Worker/src/", "./Chatter.IntegrationEventRelay.Worker/src/"]
COPY ["./Chatter.IntegrationEventRelay.Core/src/", "./Chatter.IntegrationEventRelay.Core/src/"]
COPY ["./Chatter.IntegrationEventRelay.Core/tests/", "./Chatter.IntegrationEventRelay.Core/tests/"]
COPY ["./Chatter.IntegrationEventRelay.ServiceBroker/src/", "./Chatter.IntegrationEventRelay.ServiceBroker/src/"]
COPY ["./Chatter.IntegrationEventRelay.ServiceBroker/tests/", "./Chatter.IntegrationEventRelay.ServiceBroker/tests/"]
COPY ["./Chatter.IntegrationEventRelay.Cdc/src/", "./Chatter.IntegrationEventRelay.Cdc/src/"]
COPY ["./Chatter.IntegrationEventRelay.Cdc/tests/", "./Chatter.IntegrationEventRelay.Cdc/tests/"]

RUN dotnet test "./Chatter.IntegrationEventRelay.Core/tests/Chatter.IntegrationEventRelay.Core.Tests.csproj" -c Release --no-restore
RUN dotnet test "./Chatter.IntegrationEventRelay.ServiceBroker/tests/Chatter.IntegrationEventRelay.ServiceBroker.Tests.csproj" -c Release --no-restore
RUN dotnet test "./Chatter.IntegrationEventRelay.Cdc/tests/Chatter.IntegrationEventRelay.Cdc.Tests.csproj" -c Release --no-restore --filter "Category!=Integration"

RUN dotnet publish "./Chatter.IntegrationEventRelay.Worker/src/Chatter.IntegrationEventRelay.Worker.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet","Chatter.IntegrationEventRelay.Worker.dll"]

