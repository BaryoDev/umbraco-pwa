# Demo host for BaryoDev.Umbraco.Pwa: a stock Umbraco with the package added and nothing else,
# so anything that works here works because of the package rather than the site around it.
#
# Base images are pinned by digest, so a rebuild cannot silently pick up a different image than
# the one this was tested against. The digests are multi-arch indexes, so the arm64 build on the
# VM and the amd64 build in CI both resolve. A digest also stops base-image security patches
# arriving on their own: Dependabot watches this file and opens a PR when the tag moves.
FROM mcr.microsoft.com/dotnet/sdk:10.0@sha256:e1ffd2a92ae84c1291bc1b6887501f8af98e6331e7af6d4c8d37168c5e87a64c AS build
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

FROM mcr.microsoft.com/dotnet/aspnet:10.0@sha256:a4556ed033fa96f984bb7a8d348851cb2d36b1281dd2420070045f664fbb5f94
WORKDIR /app
COPY --from=build /app ./
COPY --from=build /source-hash ./source-hash

# SQLite, so the demo carries no database dependency of its own.
RUN mkdir -p /app/umbraco/Data
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "TestSite.dll"]
