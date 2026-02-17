#!/bin/sh

if [ $# -ne 1 ]; then
    echo "Error: Specify a build configuration - either Debug or Release"
    echo "Usage: $0 <build config>"
    exit 1
fi

BASEDIR="$(git rev-parse --show-toplevel)"
cd $BASEDIR

mode="$1"

# Only accept specific exact values
case "$mode" in
    Debug|Release)
        dotnet run --project src/BirthdayBot.Registration/BirthdayBot.Registration.csproj -c $mode -- -c debug.json
        ;;
    *)
        echo "Error: Invalid parameter '$mode'. Specify a build configration - either Debug or Release"
        echo "Usage: $0 <build config>"
        exit 1
        ;;
esac
