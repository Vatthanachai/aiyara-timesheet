FROM oven/bun:1.3.11 AS build
WORKDIR /app
COPY package.json bun.lock ./
RUN bun install --frozen-lockfile
COPY . .
RUN bun run build

FROM oven/bun:1.3.11 AS final
WORKDIR /app
ENV NODE_ENV=production \
    NITRO_HOST=0.0.0.0 \
    NITRO_PORT=3000
COPY --from=build /app/.output ./.output
EXPOSE 3000
CMD ["bun", ".output/server/index.mjs"]
