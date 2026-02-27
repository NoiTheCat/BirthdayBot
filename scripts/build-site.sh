#!/usr/bin/env bash
set -euo pipefail
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

# Requires (Arch): ruby-bundler

cd $SCRIPT_DIR"/../docs"

# Delete any previous output
rm -rf _site

# Jekyll install/update and run
export BUNDLE_PATH__SYSTEM=false
export BUNDLE_PATH=.bundle
bundle install
bundle exec jekyll build

# External theme assumes site will be on domain root, even if configured paths are relative
sed -i 's|/assets/|./assets/|g' _site//index.html
sed -i '/class="img-circle"/s|src="/|src="./|g' _site/index.html

echo
echo "Complete."
