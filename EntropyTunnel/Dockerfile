FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

COPY ["EntropyTunnel.Server/EntropyTunnel.Server.csproj", "EntropyTunnel.Server/"]
COPY ["EntropyTunnel.Core/EntropyTunnel.Core.csproj", "EntropyTunnel.Core/"]

RUN dotnet restore "EntropyTunnel.Server/EntropyTunnel.Server.csproj"

COPY . .

WORKDIR "/src/EntropyTunnel.Server"
RUN dotnet build "EntropyTunnel.Server.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "EntropyTunnel.Server.csproj" -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app
COPY --from=publish /app/publish .

ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "EntropyTunnel.Server.dll"]