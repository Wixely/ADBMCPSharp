FROM mcr.microsoft.com/dotnet/sdk:10.0-noble AS build
WORKDIR /source

COPY Directory.Build.props Directory.Packages.props global.json ./
COPY src/ADBMCPSharp/ADBMCPSharp.csproj src/ADBMCPSharp/
RUN dotnet restore src/ADBMCPSharp/ADBMCPSharp.csproj

COPY src/ADBMCPSharp/ src/ADBMCPSharp/
RUN dotnet publish src/ADBMCPSharp/ADBMCPSharp.csproj \
    --configuration Release \
    --no-restore \
    --output /publish \
    -p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble AS runtime
USER root
RUN apt-get update \
    && apt-get install --yes --no-install-recommends adb ca-certificates \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY --from=build /publish/ ./
COPY LICENSE THIRD-PARTY-NOTICES.md /licenses/
RUN mkdir -p /app/logs /var/lib/adbmcp/adb \
    && chown -R app:app /app /var/lib/adbmcp

ENV Server__Host=0.0.0.0 \
    Server__Port=5719 \
    ADB_VENDOR_KEYS=/var/lib/adbmcp/adb/adbkey

VOLUME ["/app/logs", "/var/lib/adbmcp/adb"]
EXPOSE 5719
USER app
ENTRYPOINT ["dotnet", "ADBMCPSharp.dll"]
