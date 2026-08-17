FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["TheWatch.slnx", "./"]
COPY ["src/Common/TheWatch.Domain/TheWatch.Domain.csproj", "src/Common/TheWatch.Domain/"]
COPY ["src/Common/TheWatch.Infrastructure/TheWatch.Infrastructure.csproj", "src/Common/TheWatch.Infrastructure/"]
COPY ["src/Common/TheWatch.ServiceDefaults/TheWatch.ServiceDefaults.csproj", "src/Common/TheWatch.ServiceDefaults/"]
COPY ["src/Common/TheWatch.Application/TheWatch.Application.csproj", "src/Common/TheWatch.Application/"]
COPY ["src/Libraries/TheWatch.Contracts/TheWatch.Contracts.csproj", "src/Libraries/TheWatch.Contracts/"]
COPY ["src/Libraries/TheWatch.Geospatial.Db/TheWatch.Geospatial.Db.csproj", "src/Libraries/TheWatch.Geospatial.Db/"]
COPY ["src/Libraries/TheWatch.Security/TheWatch.Security.csproj", "src/Libraries/TheWatch.Security/"]
COPY ["src/Services/TheWatch.EmergencyService/TheWatch.EmergencyService.csproj", "src/Services/TheWatch.EmergencyService/"]

RUN dotnet restore "src/Services/TheWatch.EmergencyService/TheWatch.EmergencyService.csproj"
COPY . .
WORKDIR "/src/src/Services/TheWatch.EmergencyService"
RUN dotnet publish "TheWatch.EmergencyService.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
EXPOSE 8080
ENTRYPOINT ["dotnet", "TheWatch.EmergencyService.dll"]
