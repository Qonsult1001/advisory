FROM docker.io/library/node:20-alpine AS build
WORKDIR /web
COPY web/package*.json ./
RUN npm install
COPY web/ ./
# Splash login SSO default (build-time). Unset/false → "Continue" testing button; true → "Sign in with SSO".
ARG SSO_ENABLED=false
ENV SSO_ENABLED=${SSO_ENABLED}
RUN npm run build

FROM docker.io/library/nginx:alpine
COPY --from=build /web/dist /usr/share/nginx/html
EXPOSE 80
