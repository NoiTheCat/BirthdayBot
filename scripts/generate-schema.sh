#!/bin/sh
set -e

BASEDIR="$(git rev-parse --show-toplevel)"
TARGET="/schema/config.schema.json"

cd $BASEDIR
dotnet run --project src/WorldTime.SchemaGen | tee $BASEDIR$TARGET
echo
echo Current configuration schema written to $BASEDIR$TARGET
