FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
ENV ASPNETCORE_URLS=http://0.0.0.0:10000
EXPOSE 10000

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY GeometryDashPlace.Web.csproj ./
RUN dotnet restore GeometryDashPlace.Web.csproj
COPY . ./
RUN dotnet publish GeometryDashPlace.Web.csproj -c Release -o /out /p:UseAppHost=false

FROM base AS final
WORKDIR /app
RUN apt-get update \
    && apt-get install -y --no-install-recommends python3 \
    && rm -rf /var/lib/apt/lists/*
COPY --from=build /out ./
ENTRYPOINT ["dotnet", "GeometryDashPlace.Web.dll"]
