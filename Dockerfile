FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
# The local Compose build supplies this secret. Keep the PAT out of build args,
# image layers, and logs while allowing NuGet to authenticate to GitHub Packages.
RUN --mount=type=secret,id=nuget_credentials \
    NuGetPackageSourceCredentials_github="$(cat /run/secrets/nuget_credentials)" \
    dotnet publish src/HeapCare.Api/HeapCare.Api.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
COPY --from=build /app/publish .
USER $APP_UID
ENTRYPOINT ["dotnet", "HeapCare.Api.dll"]
