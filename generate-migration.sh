#!/bin/bash
dotnet tool install --global dotnet-ef
export PATH="$PATH:/root/.dotnet/tools"
dotnet restore src/Cato.API/Cato.API.csproj
dotnet-ef migrations add AddPicsChangeHistory --project src/Cato.Infrastructure --startup-project src/Cato.API
