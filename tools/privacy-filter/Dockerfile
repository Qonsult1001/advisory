# OpenAI Privacy Filter sidecar — runs openai/privacy-filter ONNX directly (onnxruntime-node).
FROM docker.io/library/node:20-slim
WORKDIR /app
# onnxruntime-node ships prebuilt native binaries; no build toolchain needed on glibc/Debian.
COPY package.json ./
RUN npm install --omit=dev
COPY server.mjs ./
ENV PF_CACHE=/cache
EXPOSE 8071
CMD ["node", "server.mjs"]
