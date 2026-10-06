FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /source

# Restore reads only these files, so copying them before the sources lets Docker reuse the restore layer
# until one of them changes.
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/GameOfLife.Core/GameOfLife.Core.csproj src/GameOfLife.Core/
COPY src/GameOfLife.Api/GameOfLife.Api.csproj src/GameOfLife.Api/
RUN dotnet restore src/GameOfLife.Api/GameOfLife.Api.csproj

# Analyzers read their settings from .editorconfig, so the image build checks the same rules as a local build.
COPY .editorconfig ./
COPY src/ src/
# The ENTRYPOINT runs the app with dotnet, so the native launcher is left out: the SDK image builds it for glibc,
# and it cannot start on Alpine.
RUN dotnet publish src/GameOfLife.Api/GameOfLife.Api.csproj -c Release --no-restore -p:UseAppHost=false -o /publish

FROM mcr.microsoft.com/dotnet/aspnet:8.0-alpine
WORKDIR /app
COPY --from=build /publish .
ENV ASPNETCORE_HTTP_PORTS=8080
# By default Npgsql first tries GSS (Kerberos) encryption. This image has no Kerberos library, so the attempt
# only makes .NET print a library load error before Npgsql connects without it.
ENV PGGSSENCMODE=disable
EXPOSE 8080
# The base image creates the non-root user app and sets APP_UID to its id.
USER $APP_UID
ENTRYPOINT ["dotnet", "GameOfLife.Api.dll"]
