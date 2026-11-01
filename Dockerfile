# ---- Stage 1: build the .NET app ----
# This stage has the full SDK (compilers, build tools) but is only used
# temporarily — it never ships in the final image.
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy only the csproj first so Docker can cache the slow "restore" step
# as long as your dependencies don't change, even if your code does.
COPY PixousDocIntelligence.csproj .
RUN dotnet restore PixousDocIntelligence.csproj

# Now copy everything else and build
COPY . .
RUN dotnet publish PixousDocIntelligence.csproj -c Release -o /app/publish --no-restore

# ---- Stage 2: the actual runtime image ----
# This is just the ASP.NET runtime, much smaller than the SDK image,
# because it doesn't need compilers at runtime.
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

COPY --from=build /app/publish .

# Folders the app writes to at runtime
RUN mkdir -p /app/output /app/uploads

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "PixousDocIntelligence.dll"]
