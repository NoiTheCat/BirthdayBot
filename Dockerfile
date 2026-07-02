
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /build
COPY . .
# Give it the csproj, not the sln. See NETSDK1194
RUN dotnet publish src/BirthdayBot/BirthdayBot.csproj -c Release -o /output

FROM mcr.microsoft.com/dotnet/runtime:10.0 AS final
ARG svcid=10382 # an arbitrary value very unlikely to correspond to a host user
RUN mkdir /app && \
    chown $svcid:$svcid /app && \
    groupadd -g $svcid birthdaybot && \
    useradd -Md /app -u $svcid -g $svcid birthdaybot
USER birthdaybot
WORKDIR /app
COPY --from=build /output .

# TODO: healthcheck... somehow
ENTRYPOINT ["/app/BirthdayBot"]
