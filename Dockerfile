# Imagem opcional (a execução local não exige Docker).
# Build:   docker build -t mercado-no-controle .
# Uso:     docker compose up -d   (veja docker-compose.yml e o README)

FROM node:24-alpine AS web
WORKDIR /src
COPY frontend/mercado-no-controle/package.json frontend/mercado-no-controle/package-lock.json ./
RUN npm ci
COPY frontend/mercado-no-controle/ ./
RUN npx ng build --configuration production

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS api
WORKDIR /src
COPY backend/MercadoNoControle.Api/MercadoNoControle.Api.csproj MercadoNoControle.Api/
RUN dotnet restore MercadoNoControle.Api/MercadoNoControle.Api.csproj
COPY backend/MercadoNoControle.Api/ MercadoNoControle.Api/
RUN dotnet publish MercadoNoControle.Api/MercadoNoControle.Api.csproj --configuration Release --output /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=api /app ./
COPY --from=web /src/dist/mercado-no-controle/browser ./wwwroot
ENV ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_HTTP_PORTS=8080 \
    Storage__DatabasePath=/data/mercado-no-controle.db \
    Storage__UploadsPath=/uploads
RUN mkdir -p /data /uploads && chown "$APP_UID" /data /uploads
VOLUME ["/data", "/uploads"]
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "MercadoNoControle.Api.dll"]
