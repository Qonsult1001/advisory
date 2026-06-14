FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY src/Advisory.Api/Advisory.Api.csproj ./Advisory.Api/
RUN dotnet restore ./Advisory.Api/Advisory.Api.csproj
COPY src/Advisory.Api/ ./Advisory.Api/
RUN dotnet publish ./Advisory.Api/Advisory.Api.csproj -c Release -o /app

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
# Research backlog read by the Evolution tab (parsed into section-tagged findings). Baked from the
# repo at build time; refreshed when `release` rebuilds the image after a research PR merges to main.
COPY RESEARCH.md /app/RESEARCH.md
# .said project brain + the LINUX said binary, so the in-container Groq cycle RECALLS only the code it
# needs (said sym/get/ask) instead of being force-fed whole files. The Windows said.exe can't run here;
# said-linux is the x86-64 ELF build. Baked at image build; refreshed on release.
COPY tools/said/said-linux /app/said
COPY Advisory.said /app/Advisory.said
RUN chmod +x /app/said
ENV SAID_BIN=/app/said SAID_FILE=/app/Advisory.said
ENV ASPNETCORE_URLS=http://+:5000
EXPOSE 5000
ENTRYPOINT ["dotnet", "Advisory.Api.dll"]
