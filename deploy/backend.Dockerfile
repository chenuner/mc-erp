FROM mcr.microsoft.com/dotnet/aspnet:10.0
RUN apt-get update && apt-get install -y --no-install-recommends curl && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY backend/ ./
RUN mkdir -p storage-files logs && chown -R app:app /app
USER app
ENTRYPOINT ["dotnet", "MC.Erp.dll"]