FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY TranXuanThanh_2380614806_N7_API.csproj ./
RUN dotnet restore ./TranXuanThanh_2380614806_N7_API.csproj
COPY . ./
RUN dotnet publish ./TranXuanThanh_2380614806_N7_API.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
COPY --from=build /app/publish ./
USER $APP_UID
ENTRYPOINT ["dotnet", "TranXuanThanh_2380614806_N7_API.dll"]
