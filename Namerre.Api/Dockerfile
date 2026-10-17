FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
USER $APP_UID
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

FROM node:24-alpine AS frontend
WORKDIR /src/Namerre.Front
COPY Namerre.Front/package.json Namerre.Front/package-lock.json ./
RUN npm ci
COPY Namerre.Front/ ./
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS publish
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY Namerre.Api/Namerre.Api.csproj Namerre.Api/
RUN dotnet restore Namerre.Api/Namerre.Api.csproj
COPY Namerre.Api/ Namerre.Api/
COPY --from=frontend /src/Namerre.Api/wwwroot/ Namerre.Api/wwwroot/
RUN dotnet publish Namerre.Api/Namerre.Api.csproj -c $BUILD_CONFIGURATION \
    -o /app/publish --no-restore /p:UseAppHost=false /p:BuildFrontend=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Namerre.Api.dll"]
