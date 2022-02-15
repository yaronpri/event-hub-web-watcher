FROM mcr.microsoft.com/dotnet/core/aspnet:3.1.1-alpine AS base
WORKDIR /app
EXPOSE 80
EXPOSE 443

FROM mcr.microsoft.com/dotnet/core/sdk:3.1.101-alpine AS build
WORKDIR /src
COPY ["event-hub-web-watcher.csproj", "event-hub-web-watcher/"]
RUN dotnet restore "event-hub-web-watcher/event-hub-web-watcher.csproj"
WORKDIR "/src/event-hub-web-watcher"
COPY . .
RUN dotnet build "event-hub-web-watcher.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "event-hub-web-watcher.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "event-hub-web-watcher.dll"]