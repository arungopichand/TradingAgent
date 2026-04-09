FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY KrishAgent.csproj ./
RUN dotnet restore KrishAgent.csproj

COPY . ./
RUN dotnet publish KrishAgent.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app

ENV ASPNETCORE_ENVIRONMENT=Production
COPY --from=build /app/publish ./

EXPOSE 10000
ENTRYPOINT ["dotnet", "KrishAgent.dll"]
