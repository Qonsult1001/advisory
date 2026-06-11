FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY src/PkgFirewall.Api/PkgFirewall.Api.csproj ./PkgFirewall.Api/
RUN dotnet restore ./PkgFirewall.Api/PkgFirewall.Api.csproj
COPY src/PkgFirewall.Api/ ./PkgFirewall.Api/
RUN dotnet publish ./PkgFirewall.Api/PkgFirewall.Api.csproj -c Release -o /app

# Reachability analyzer deps (acorn) installed in a node stage, copied into runtime.
FROM node:20-alpine AS reach
WORKDIR /reach
COPY tools/reachability/package.json ./
RUN npm install --omit=dev
COPY tools/reachability/ ./

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
# Node runtime for the contextual-analysis (reachability) helper, plus git + the GitHub CLI (gh)
# so the Evolution dashboard can read tickets and dispatch the evolution workflow.
RUN apt-get update && apt-get install -y --no-install-recommends nodejs git curl ca-certificates \
    && curl -fsSL https://cli.github.com/packages/githubcli-archive-keyring.gpg \
       | dd of=/usr/share/keyrings/githubcli-archive-keyring.gpg \
    && echo "deb [arch=$(dpkg --print-architecture) signed-by=/usr/share/keyrings/githubcli-archive-keyring.gpg] https://cli.github.com/packages stable main" \
       > /etc/apt/sources.list.d/github-cli.list \
    && apt-get update && apt-get install -y --no-install-recommends gh \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app ./
COPY --from=reach /reach /app/reachability
ENV ASPNETCORE_URLS=http://+:5000
EXPOSE 5000
ENTRYPOINT ["dotnet", "PkgFirewall.Api.dll"]
