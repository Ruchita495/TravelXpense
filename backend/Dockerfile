FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY src/TravelXpense.Api/TravelXpense.Api.csproj src/TravelXpense.Api/
RUN dotnet restore src/TravelXpense.Api/TravelXpense.Api.csproj
COPY src/TravelXpense.Api/ src/TravelXpense.Api/
RUN dotnet publish src/TravelXpense.Api/TravelXpense.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_URLS=http://+:8080
USER app
EXPOSE 8080
ENTRYPOINT ["dotnet", "TravelXpense.Api.dll"]
