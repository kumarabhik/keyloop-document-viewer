# One Dockerfile for the API and both mocks. Compose picks the project with --build-arg PROJECT
# and the matching final stage with `target:` (api | sales-mock | service-mock).
# Build on the SDK image, run on the chiselled ASP.NET image (no shell, no package manager, non-root).

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG PROJECT=src/DocViewer.Api/DocViewer.Api.csproj
WORKDIR /repo

# Build settings first: they change rarely, so the restore layer is reused.
COPY global.json Directory.Build.props Directory.Packages.props .editorconfig ./
COPY src/ src/
COPY mocks/ mocks/
RUN dotnet restore "$PROJECT"
RUN dotnet publish "$PROJECT" -c Release --no-restore -o /app -p:UseAppHost=false

# -extra adds tzdata (the dealership timezone, A10) and ICU; plain chiselled has neither (E-009).
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled-extra AS runtime
WORKDIR /app
COPY --from=build /app .
EXPOSE 8080

# Exec-form entry points cannot expand variables (and the image has no shell), hence one stage per app.
FROM runtime AS api
ENTRYPOINT ["dotnet", "DocViewer.Api.dll"]

FROM runtime AS sales-mock
ENTRYPOINT ["dotnet", "SalesSystem.Mock.dll"]

FROM runtime AS service-mock
ENTRYPOINT ["dotnet", "ServiceSystem.Mock.dll"]

# --- Demo UI (D-025): Blazor WebAssembly is static files after publish, so nginx serves it. -----------------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS web-build
WORKDIR /repo
COPY global.json Directory.Build.props Directory.Packages.props .editorconfig ./
COPY src/DocViewer.Domain/ src/DocViewer.Domain/
COPY frontend/DocViewer.Web/ frontend/DocViewer.Web/
RUN dotnet publish frontend/DocViewer.Web/DocViewer.Web.csproj -c Release -o /web

FROM nginx:1.29-alpine AS web
COPY docker/nginx-web.conf /etc/nginx/conf.d/default.conf
COPY --from=web-build /web/wwwroot /usr/share/nginx/html
EXPOSE 80
