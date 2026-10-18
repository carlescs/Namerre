# syntax=docker/dockerfile:1

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
COPY nuget.config ./
COPY Namerre.Api/Namerre.Api.csproj Namerre.Api/
COPY Namerre.Application/Namerre.Application.csproj Namerre.Application/
COPY Namerre.Domain/Namerre.Domain.csproj Namerre.Domain/
COPY Namerre.Infrastructure/Namerre.Infrastructure.csproj Namerre.Infrastructure/
RUN --mount=type=secret,id=github_packages_credentials,env=NuGetPackageSourceCredentials_Github,required=true \
    dotnet restore Namerre.Api/Namerre.Api.csproj
COPY Namerre.Api/ Namerre.Api/
COPY Namerre.Application/ Namerre.Application/
COPY Namerre.Domain/ Namerre.Domain/
COPY Namerre.Infrastructure/ Namerre.Infrastructure/
COPY --from=frontend /src/Namerre.Api/wwwroot/ Namerre.Api/wwwroot/
RUN dotnet publish Namerre.Api/Namerre.Api.csproj -c $BUILD_CONFIGURATION \
    -o /app/publish --no-restore /p:UseAppHost=false /p:BuildFrontend=false

FROM base AS final
WORKDIR /app
USER root
RUN mkdir -p /data && chown "$APP_UID" /data
USER $APP_UID
ENV Namerre__DataPath=/data
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Namerre.Api.dll"]
