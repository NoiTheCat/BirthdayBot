FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build

WORKDIR /build
COPY . .

ARG PUBLISH_PROFILE=Release

# Restore and publish
# Give it the csproj, not the sln. See NETSDK1194
RUN dotnet publish src/BirthdayBot/BirthdayBot.csproj -c ${PUBLISH_PROFILE} -o /output

FROM mcr.microsoft.com/dotnet/runtime:10.0 AS final

WORKDIR /app
COPY --from=build /output .

# to do: healthcheck
USER nobody:nogroup
ENTRYPOINT ["/app/BirthdayBot"]
