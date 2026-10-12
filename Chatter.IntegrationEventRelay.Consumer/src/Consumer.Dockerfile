FROM mcr.microsoft.com/dotnet/runtime:10.0 AS base
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS publish
WORKDIR /src
COPY ["./Chatter.IntegrationEventRelay.Consumer/src/Chatter.IntegrationEventRelay.Consumer.csproj", "./Chatter.IntegrationEventRelay.Consumer/src/Chatter.IntegrationEventRelay.Consumer.csproj"]
RUN dotnet restore "./Chatter.IntegrationEventRelay.Consumer/src/Chatter.IntegrationEventRelay.Consumer.csproj"
COPY ["./Chatter.IntegrationEventRelay.Consumer/src/","./Chatter.IntegrationEventRelay.Consumer/src/"]
RUN dotnet publish "./Chatter.IntegrationEventRelay.Consumer/src/Chatter.IntegrationEventRelay.Consumer.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet","Chatter.IntegrationEventRelay.Consumer.dll"]

