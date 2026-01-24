FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

COPY ["EntropyTunnel.Server/EntropyTunnel.Server.csproj", "EntropyTunnel.Server/"]
COPY ["EntropyTunnel.Core/EntropyTunnel.Core.csproj", "EntropyTunnel.Core/"]

RUN dotnet restore "EntropyTunnel.Server/EntropyTunnel.Server.csproj"

COPY . .

WORKDIR "/src/EntropyTunnel.Server"
RUN dotnet publish "EntropyTunnel.Server.csproj" -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:9.0
WORKDIR /app
COPY --from=build /app/publish .

EXPOSE 8080

ENV ASPNETCORE_URLS=http://+:8080

ENTRYPOINT ["dotnet", "EntropyTunnel.Server.dll"]