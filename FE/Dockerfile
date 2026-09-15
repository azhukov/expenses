# The browser client as a deployable image (Railway). Build context is FE/.
#
# Served by `vite preview`, not a static file server: preview already writes /config.js from API_URL
# when it starts and refuses to start without a usable one, and the e2e suite drives exactly that
# server on every CI run. A second host would have to reimplement and retest the contract.
#
# One stage with the full install: vite.config.ts loads its plugins (devDependencies) when the
# preview server starts, so there is nothing to prune without splitting the config.
#
#   docker build -t expenses-fe FE
#   docker run -e PORT=8080 -e API_URL=http://localhost:5082 -e PREVIEW_ALLOWED_HOSTS=localhost -p 8080:8080 expenses-fe

# The Node major CI builds and tests with.
FROM node:22-slim

WORKDIR /app

# Manifests first, so a source-only change does not re-run the install.
COPY package.json package-lock.json ./
RUN npm ci

COPY . .
RUN npm run build

ENV NODE_ENV=production

# API_URL and PREVIEW_ALLOWED_HOSTS come from the environment the container starts with, never from
# the build, so one image can be pointed at any API. PORT is Railway's; 4173 is preview's default.
# The shell form expands $PORT; exec hands the process signals directly.
CMD ["sh", "-c", "exec node node_modules/vite/bin/vite.js preview --host 0.0.0.0 --port ${PORT:-4173} --strictPort"]
