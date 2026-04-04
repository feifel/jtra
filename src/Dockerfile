FROM mcr.microsoft.com/dotnet/aspnet:8.0.25 AS base
WORKDIR /app
EXPOSE 5000

FROM mcr.microsoft.com/dotnet/sdk:8.0.419 AS build
WORKDIR /src
COPY ["JtraServer/JtraServer.csproj", "JtraServer/"]
COPY ["JtraShared/JtraShared.csproj", "JtraShared/"]
COPY ["JtraClient/JtraClient.csproj", "JtraClient/"]
RUN dotnet restore "JtraServer/JtraServer.csproj"
COPY . .
WORKDIR "/src/JtraServer"
RUN dotnet build "JtraServer.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "JtraServer.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/sdk:8.0.419 AS client-build
WORKDIR /src
COPY ["JtraClient/JtraClient.csproj", "JtraClient/"]
COPY ["JtraShared/JtraShared.csproj", "JtraShared/"]
RUN dotnet restore "JtraClient/JtraClient.csproj"
COPY . .
WORKDIR "/src/JtraClient"
RUN dotnet publish "JtraClient.csproj" -c Release -o /app/client-publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
COPY --from=client-build /app/client-publish/wwwroot ./wwwroot
ENV ASPNETCORE_URLS=http://+:5000
ENTRYPOINT ["dotnet", "JtraServer.dll"]
