# The Webignite sync server, built from this repository. Build from the repository's root:
#   docker build -f deploy/Dockerfile -t webignite-server .
# Only the server and Core are copied in: the desktop app and its tests stay out of the image.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY global.json .editorconfig Directory.Build.props Directory.Packages.props ./
COPY src/Webignite.Core/Webignite.Core.csproj src/Webignite.Core/
COPY src/Webignite.Server/Webignite.Server.csproj src/Webignite.Server/
RUN dotnet restore src/Webignite.Server/Webignite.Server.csproj
COPY src/Webignite.Core/ src/Webignite.Core/
COPY src/Webignite.Server/ src/Webignite.Server/
RUN dotnet publish src/Webignite.Server/Webignite.Server.csproj -c Release --no-restore -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
# The database lives in /data, a folder on the server kept outside the container (see compose.yaml).
# Only warnings and problems are logged (and starting and stopping), not every request.
ENV WEBIGNITE_SERVER_DATA=/data \
    ASPNETCORE_URLS=http://127.0.0.1:5080 \
    DOTNET_gcServer=0 \
    Logging__LogLevel__Default=Warning \
    Logging__LogLevel__Microsoft.Hosting.Lifetime=Information
RUN mkdir -p /data && chown app:app /data
USER app
ENTRYPOINT ["dotnet", "Webignite.Server.dll"]
