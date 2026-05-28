# --- DEVELOPMENT STAGE ---
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS dev
WORKDIR /src
# VS Code Dev Containers will use this stage and mount your code here

# --- BUILD STAGE ---
FROM dev AS build
COPY . .
RUN dotnet publish "apiplatform.csproj" --configuration Release --os linux --self-contained false -o /app/publish

# --- RUNTIME STAGE (Production) ---
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "apiplatform.dll"]
