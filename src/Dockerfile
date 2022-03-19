FROM mcr.microsoft.com/dotnet/sdk:6.0 as builder

COPY ./ /src/

WORKDIR /src

RUN dotnet publish --configuration Release -o out/ ./postgres-operator.csproj

FROM mcr.microsoft.com/dotnet/runtime:6.0 as runtime

LABEL maintainer="Raki Rahman<mdrakiburrahman@gmail.com>"

ARG BUILD
ENV APPVERSION=$BUILD

WORKDIR /postgres-controller

COPY --from=builder /src/out ./

CMD ["dotnet", "./postgres-operator.dll"]
