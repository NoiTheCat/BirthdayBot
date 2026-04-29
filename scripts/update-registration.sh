#!/bin/sh

if [ $# -ne 1 ]; then
    echo "Error: Specify a build configuration - either Debug or Release"
    echo "Usage: $0 <build config>"
    exit 1
fi

BASEDIR="$(git rev-parse --show-toplevel)"
cd $BASEDIR

PROJECT_NAME=BirthdayBot.Registration
PROJECT="src/$PROJECT_NAME/$PROJECT_NAME.csproj"
mode="$1"

# Only accept specific exact values
case "$mode" in
    Debug|Release)
        dotnet clean $PROJECT
        dotnet build -c Debug $PROJECT
        cd $BASEDIR/src/$PROJECT_NAME/bin/$mode/net10.0
        ./BirthdayBot.Registration -c $BASEDIR/debug.json
        #dotnet run --no-build --project $PROJECT -c $mode -- -c debug.json
        ;;
    *)
        echo "Error: Invalid parameter '$mode'. Specify a build configration - either Debug or Release"
        echo "Usage: $0 <build config>"
        exit 1
        ;;
esac
