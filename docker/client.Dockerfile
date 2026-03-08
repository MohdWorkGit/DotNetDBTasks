# Stage 1: Build Angular app
FROM node:22-alpine AS build
WORKDIR /app

# Copy package files for layer caching
COPY client/package.json ./

# Install dependencies
RUN npm install --legacy-peer-deps

# Copy source code
COPY client/ .

# Build production bundle
RUN npm run build:prod

# Stage 2: Serve with nginx
FROM nginx:alpine AS runtime

# Remove default nginx config
RUN rm /etc/nginx/conf.d/default.conf

# Copy custom nginx config
COPY docker/nginx.conf /etc/nginx/conf.d/default.conf

# Copy built Angular app
COPY --from=build /app/dist/dotnet-db-tasks-client/browser /usr/share/nginx/html

EXPOSE 80

HEALTHCHECK --interval=30s --timeout=10s --start-period=10s --retries=3 \
    CMD wget -qO- http://localhost:80/ || exit 1

CMD ["nginx", "-g", "daemon off;"]
