# Build and serve the authoring surface. Multi-stage so the served image carries no toolchain,
# no source and no node_modules - only the static files and something to hand them out.
FROM node:22.20.0-alpine AS build
WORKDIR /source

# Restore against the manifests alone first, so a source-only change reuses the cached layer.
# The repository root is the npm workspace, so the root manifests come too.
COPY package.json package-lock.json ./
COPY apps/authoring-ui/package.json ./apps/authoring-ui/
RUN npm ci

COPY apps/authoring-ui ./apps/authoring-ui
RUN npm --prefix apps/authoring-ui run build

# nginx is BSD-2-Clause, which the Apache-2.0 compatibility rule in CLAUDE.md accepts, and it is
# here to hand out static files rather than to be a gateway. The gateway is Kong, under the
# optional profile, and it is a different job.
FROM nginx:1.29.3-alpine AS serve

# Single-page application routing: an address the surface owns is not a file on disk, so anything
# that is not a file falls back to index.html. Without this, opening a label by its URL - which
# is how the surface is meant to be linked to - answers 404.
COPY apps/authoring-ui/nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /source/apps/authoring-ui/dist /usr/share/nginx/html

# The development server's copy of the configuration is removed rather than shipped. It carries
# localhost addresses, and an image with an address in it is one that starts happily in the wrong
# environment - the exact silent misconfiguration the surface refuses to make (ADR-049). Without
# a mount, this image serves nothing and says why.
RUN rm -f /usr/share/nginx/html/config.json

# Where the surface is pointed is read at start-up from config.json, mounted rather than copied
# (ADR-049). The addresses in it are the browser's, not the container network's: a browser
# resolves localhost, and it cannot reach "epi-api" or "keycloak" whatever this container can.
#
# Deliberately not baked into the image. An image with a hostname compiled into it cannot be
# promoted between environments, only rebuilt for each - and then what runs is not what CI
# proved.

EXPOSE 80
