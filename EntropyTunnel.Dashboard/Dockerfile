# ── Stage 1: Build ────────────────────────────────────────────────────────────
FROM node:18-alpine AS build

WORKDIR /app

COPY package.json ./
RUN npm install

COPY . .
# VITE_API_URL is left empty so the browser uses same-origin paths,
# which nginx proxies to the server container.
RUN npm run build

# ── Stage 2: Serve ────────────────────────────────────────────────────────────
FROM nginx:alpine

COPY --from=build /app/dist /usr/share/nginx/html
COPY nginx.conf /etc/nginx/conf.d/default.conf

EXPOSE 80
