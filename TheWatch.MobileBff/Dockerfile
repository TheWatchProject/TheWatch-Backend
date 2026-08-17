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
COPY ["src/Libraries/TheWatch.Client.Core/TheWatch.Client.Core.csproj", "src/Libraries/TheWatch.Client.Core/"]
COPY ["src/Libraries/Generated/TheWatch.Catalog.Operations/TheWatch.Catalog.Operations.csproj", "src/Libraries/Generated/TheWatch.Catalog.Operations/"]
COPY ["src/Libraries/Generated/TheWatch.Catalog.Infrastructure/TheWatch.Catalog.Infrastructure.csproj", "src/Libraries/Generated/TheWatch.Catalog.Infrastructure/"]
COPY ["src/Libraries/Generated/TheWatch.Catalog.Foundation/TheWatch.Catalog.Foundation.csproj", "src/Libraries/Generated/TheWatch.Catalog.Foundation/"]
COPY ["src/Libraries/Generated/TheWatch.Catalog.Tooling/TheWatch.Catalog.Tooling.csproj", "src/Libraries/Generated/TheWatch.Catalog.Tooling/"]
COPY ["src/Libraries/Generated/TheWatch.Catalog.Security/TheWatch.Catalog.Security.csproj", "src/Libraries/Generated/TheWatch.Catalog.Security/"]
COPY ["src/Libraries/Generated/TheWatch.Catalog.Experience/TheWatch.Catalog.Experience.csproj", "src/Libraries/Generated/TheWatch.Catalog.Experience/"]
COPY ["src/Services/TheWatch.MobileBff/TheWatch.MobileBff.csproj", "src/Services/TheWatch.MobileBff/"]

RUN dotnet restore "src/Services/TheWatch.MobileBff/TheWatch.MobileBff.csproj"
COPY . .
WORKDIR "/src/src/Services/TheWatch.MobileBff"
RUN dotnet build "TheWatch.MobileBff.csproj" -c Release -o /app/build
RUN dotnet publish "TheWatch.MobileBff.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
EXPOSE 8080
EXPOSE 8081
ENTRYPOINT ["dotnet", "TheWatch.MobileBff.dll"]
