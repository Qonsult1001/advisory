# Real VS Code extension exfiltration scanner sidecar — trailofbits/vsix-audit (needs Node 22+).
FROM node:22-slim

# vsix-audit shells the YARA-X `yr` CLI for its rule engine; install the prebuilt binary so YARA
# rules actually run (RAT / credential-harvesting / webhook-exfiltration / self-propagation) rather
# than degrading to a "YARA not installed" informational finding.
ARG YARA_X_VERSION=v1.18.0
RUN apt-get update && apt-get install -y --no-install-recommends ca-certificates curl \
    && rm -rf /var/lib/apt/lists/* \
    && curl -fsSL "https://github.com/VirusTotal/yara-x/releases/download/${YARA_X_VERSION}/yara-x-${YARA_X_VERSION}-x86_64-unknown-linux-gnu.tar.gz" \
       -o /tmp/yara-x.tar.gz \
    && tar -xzf /tmp/yara-x.tar.gz -C /usr/local/bin yr \
    && rm /tmp/yara-x.tar.gz \
    && yr --version

# Install the scanner CLI globally.
RUN npm install -g @trailofbits/vsix-audit@0.3.0

WORKDIR /app
COPY server.mjs ./

ENV PORT=8099
EXPOSE 8099
HEALTHCHECK --interval=30s --timeout=10s --start-period=20s --retries=3 \
  CMD curl -fsS http://localhost:8099/health || exit 1

CMD ["node", "server.mjs"]
