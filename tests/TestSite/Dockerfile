# Demo host for BaryoDev.Umbraco.Pwa: a stock Umbraco with the package added and nothing else,
# so anything that works here works because of the package rather than the site around it.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY src/BaryoDev.Umbraco.Pwa/ ./src/BaryoDev.Umbraco.Pwa/
# Central Package Management: TestSite.csproj declares PackageReferences without versions,
# so without this the restore fails with NU1015.
COPY tests/Directory.Packages.props ./tests/
COPY tests/TestSite/ ./tests/TestSite/
COPY README.md icon.png ./
COPY scripts/ ./scripts/

# Computed from the source this image is built from, and served by /build-info so the publish
# workflow can refuse to release a build the playground has not run. An assembly MVID cannot do
# that job: this image builds without .git present while CI packs with SourceLink metadata, so
# the same source produces two different assemblies. The source itself is the thing both sides
# can agree on.
RUN sh scripts/source-hash.sh > /source-hash

RUN dotnet publish tests/TestSite/TestSite.csproj -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app ./
COPY --from=build /source-hash ./source-hash

# SQLite, so the demo carries no database dependency of its own.
RUN mkdir -p /app/umbraco/Data
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "TestSite.dll"]
