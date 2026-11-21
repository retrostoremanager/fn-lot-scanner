# Local/Podman-only image for full-stack testing -- NOT used for the real Azure deploy.
#
# Uses Microsoft's prebuilt dotnet-isolated runtime image rather than func start
# (Core Tools) here: Core Tools' bundled host currently throws a
# "Microsoft.Extensions.Options, Version=10.0.0.0" FileLoadException against a net10.0
# worker (host/worker assembly version skew) -- tested and confirmed broken 2026-11-21.
# The prebuilt runtime image doesn't have that mismatch since Microsoft builds one per
# supported worker version. Config comes from environment variables (docker-compose),
# not a mounted local.settings.json -- this image expects Azure App Settings-style env
# vars, the same way it would running in Azure.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish fn-lot-scanner.csproj -c Release -o /app

FROM mcr.microsoft.com/azure-functions/dotnet-isolated:4-dotnet-isolated10.0
ENV AzureFunctionsJobHost__Logging__Console__IsEnabled=true
COPY --from=build /app /home/site/wwwroot
