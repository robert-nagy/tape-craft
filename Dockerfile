# Build the React frontend.
FROM node:22-alpine AS web
WORKDIR /src
COPY package.json package-lock.json ./
COPY web/package.json web/
RUN npm ci --workspace web
COPY web web
RUN npm run build --workspace web

# Publish the ASP.NET Core backend with the built frontend in wwwroot.
FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS api
WORKDIR /src
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/TapeCraft.Domain/TapeCraft.Domain.csproj src/TapeCraft.Domain/
COPY src/TapeCraft.Api/TapeCraft.Api.csproj src/TapeCraft.Api/
RUN dotnet restore src/TapeCraft.Api/TapeCraft.Api.csproj
COPY src src
COPY --from=web /src/src/TapeCraft.Api/wwwroot src/TapeCraft.Api/wwwroot
RUN dotnet publish src/TapeCraft.Api -c Release -o /app --no-restore -p:SkipFrontend=true

FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine
WORKDIR /app
ENV HOST=0.0.0.0 PORT=3000
COPY --from=api /app .
# Mount a volume here for DATA_PROTECTION_KEYS_DIR. A new named volume takes this folder's owner, the app user.
RUN mkdir /keys && chown "$APP_UID" /keys && chmod 700 /keys
USER $APP_UID
EXPOSE 3000
# Run the migrations with the same image: docker run ... tape-craft migrate
ENTRYPOINT ["dotnet", "TapeCraft.Api.dll"]
