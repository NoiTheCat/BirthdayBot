#!/bin/sh

if [ $# -ne 1 ]; then
    echo "Error: Specify a build configuration - either Debug or Release"
    echo "Usage: $0 <build config>"
    exit 1
fi

BASEDIR="$(git rev-parse --show-toplevel)"
cd $BASEDIR

PROJECT="src/BirthdayBot.Registration/BirthdayBot.Registration.csproj"
mode="$1"

# Only accept specific exact values
case "$mode" in
    Debug|Release)
        dotnet clean $PROJECT
        dotnet build -c Debug $PROJECT
        dotnet run --no-build --project $PROJECT -c $mode -- -c debug.json
        ;;
    *)
        echo "Error: Invalid parameter '$mode'. Specify a build configration - either Debug or Release"
        echo "Usage: $0 <build config>"
        exit 1
        ;;
esac
