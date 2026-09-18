FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build

WORKDIR /src

COPY ["src/DoctorPayment.Api/DoctorPayment.Api.csproj", "src/DoctorPayment.Api/"]
RUN dotnet restore "src/DoctorPayment.Api/DoctorPayment.Api.csproj"

COPY . .
RUN dotnet publish "src/DoctorPayment.Api/DoctorPayment.Api.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final

WORKDIR /app
COPY --from=build /app/publish .

EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080

ENTRYPOINT ["dotnet", "DoctorPayment.Api.dll"]
