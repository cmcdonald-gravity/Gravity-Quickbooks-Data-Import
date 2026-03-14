# =========================
# Build stage
# =========================
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# copy everything and restore
COPY . .
RUN dotnet restore

# publish self-contained (trim optional)
RUN dotnet publish -c Release -o /app/out

# =========================
# Runtime stage
# =========================
FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app

# copy app
COPY --from=build /app/out ./

# Render injects PORT; listen on 0.0.0.0:PORT
ENV ASPNETCORE_URLS=http://0.0.0.0:${PORT}

# optional: make timezone predictable
# ENV TZ=UTC

# health endpoint path is /health
EXPOSE 10000
CMD ["dotnet", "QbAuthServr.dll"]