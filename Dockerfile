FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

COPY ["src/CompanyEnrichment.Api/CompanyEnrichment.Api.csproj", "src/CompanyEnrichment.Api/"]
COPY ["src/Cvr.Client/Cvr.Client.csproj", "src/Cvr.Client/"]
COPY ["src/StatBank.Client/StatBank.Client.csproj", "src/StatBank.Client/"]
COPY ["src/CompanyEnrichment.Shared/CompanyEnrichment.Shared.csproj", "src/CompanyEnrichment.Shared/"]
RUN dotnet restore "src/CompanyEnrichment.Api/CompanyEnrichment.Api.csproj"

COPY . .
WORKDIR /src/src/CompanyEnrichment.Api
RUN dotnet build "CompanyEnrichment.Api.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "CompanyEnrichment.Api.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
USER $APP_UID
ENTRYPOINT ["dotnet", "CompanyEnrichment.Api.dll"]
