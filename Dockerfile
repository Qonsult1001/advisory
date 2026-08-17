FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY src/Advisory.Api/Advisory.Api.csproj ./Advisory.Api/
RUN dotnet restore ./Advisory.Api/Advisory.Api.csproj
COPY src/Advisory.Api/ ./Advisory.Api/
RUN dotnet publish ./Advisory.Api/Advisory.Api.csproj -c Release -o /app

# Reachability analyzer deps (acorn) installed in a node stage, copied into runtime.
FROM docker.io/library/node:20-alpine AS reach
WORKDIR /reach
COPY tools/reachability/package.json ./
RUN npm install --omit=dev
COPY tools/reachability/ ./

# Runtime image based on the SDK (not the aspnet runtime) so the in-container Groq cycle can
# `dotnet build` + `dotnet test` the change it generates IN THE CLONE before opening a PR. Without the
# SDK the cycle couldn't self-verify and opened drafts that looked fine but could be non-compiling
# (a bad surgical anchor once split Program.cs). With the SDK, a change that doesn't build/test can
# never reach a mergeable PR. The SDK image is larger but that's the price of trustworthy autonomy.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS runtime
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
# .said project brain + the LINUX said binaries (v0.11.1), so the in-container mutation cycle runs
# entirely in the container — no host script / WSL. `said` recalls code (sym/get/ask); `said-orchestrate`
# is the closed-loop driver (plan→design→code→test→repair→learn) the Mutate button runs with the routed
# MAF agent's LLM. x86-64 ELF builds (the Windows .exe can't run here). Baked at build; refreshed on release.
COPY tools/said/said-linux /app/said
COPY tools/said/said-orchestrate-linux /app/said-orchestrate
COPY Advisory.said /app/Advisory.said
RUN chmod +x /app/said /app/said-orchestrate
ENV SAID_BIN=/app/said SAID_FILE=/app/Advisory.said ORCH_BIN=/app/said-orchestrate
ENV ASPNETCORE_URLS=http://+:5000
EXPOSE 5000
EXPOSE 8090
ENTRYPOINT ["dotnet", "Advisory.Api.dll"]
