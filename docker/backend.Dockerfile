ARG PROJECT_PATH
ARG APP_DLL

FROM mcr.microsoft.com/dotnet/sdk:10.0.401 AS build
ARG PROJECT_PATH
WORKDIR /src
COPY . .
RUN dotnet restore "${PROJECT_PATH}"
RUN dotnet publish "${PROJECT_PATH}" --configuration Release --no-restore --output /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0.12 AS final
ARG APP_DLL
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080 \
    APP_DLL=${APP_DLL}
RUN apt-get update \
    && apt-get install --yes --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*
COPY --from=build /app/publish .
EXPOSE 8080
ENTRYPOINT ["sh", "-c", "dotnet \"$APP_DLL\""]
