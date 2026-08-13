# Demo host for BaryoDev.Umbraco.Pwa: a stock Umbraco with the package added and nothing else,
# so anything that works here works because of the package rather than the site around it.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY src/BaryoDev.Umbraco.Pwa/ ./src/BaryoDev.Umbraco.Pwa/
COPY tests/TestSite/ ./tests/TestSite/
COPY README.md icon.png ./

RUN dotnet publish tests/TestSite/TestSite.csproj -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app ./

# SQLite, so the demo carries no database dependency of its own.
RUN mkdir -p /app/umbraco/Data
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "TestSite.dll"]
