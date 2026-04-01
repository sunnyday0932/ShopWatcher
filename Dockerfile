FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ShopWatcher.slnx .
COPY src/ShopWatcher/ShopWatcher.csproj src/ShopWatcher/
RUN dotnet restore src/ShopWatcher/ShopWatcher.csproj
COPY src/ src/
RUN dotnet publish src/ShopWatcher/ShopWatcher.csproj -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .
VOLUME /app/data
ENV ConnectionStrings__Default="Data Source=/app/data/shopwatcher.db"
ENTRYPOINT ["dotnet", "ShopWatcher.dll"]
