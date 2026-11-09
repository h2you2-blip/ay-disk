# AY Disk Relay — local/private development only
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY src/AyDisk.Relay/AyDisk.Relay.csproj src/AyDisk.Relay/
RUN dotnet restore src/AyDisk.Relay/AyDisk.Relay.csproj
COPY src/AyDisk.Relay/ src/AyDisk.Relay/
RUN dotnet publish src/AyDisk.Relay/AyDisk.Relay.csproj -c Release --no-restore -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_URLS=http://0.0.0.0:5000
EXPOSE 5000
USER $APP_UID
ENTRYPOINT ["dotnet", "AyDisk.Relay.dll"]
