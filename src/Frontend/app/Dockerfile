FROM oven/bun:1.3.11 AS build
ARG PROJECT_DIR=app
WORKDIR /frontend
COPY ${PROJECT_DIR}/package.json ${PROJECT_DIR}/bun.lock ./${PROJECT_DIR}/
WORKDIR /frontend/${PROJECT_DIR}
RUN bun install --frozen-lockfile
WORKDIR /frontend
COPY ${PROJECT_DIR}/ ./${PROJECT_DIR}/
COPY shared/ ./shared/
WORKDIR /frontend/${PROJECT_DIR}
RUN bun run build

FROM oven/bun:1.3.11 AS final
ARG PROJECT_DIR=app
WORKDIR /app
ENV NODE_ENV=production \
    NITRO_HOST=0.0.0.0 \
    NITRO_PORT=3000
COPY --from=build /frontend/${PROJECT_DIR}/.output ./.output
EXPOSE 3000
CMD ["bun", ".output/server/index.mjs"]
