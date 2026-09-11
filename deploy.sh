#!/bin/sh -ex

# DreamTech Leaderboard - UPM Deployment Script
# Creates a clean UPM tag with only the package contents via git subtree split.
# Usage: ./deploy.sh --semver "0.2.0"

# Parse arguments
while [ $# -gt 0 ]; do
  case "$1" in
    --semver=*)
      SEMVER="${1#*=}"
      ;;
    --semver)
      SEMVER="$2"
      shift
      ;;
    *)
      echo "Usage: ./deploy.sh --semver \"0.2.0\""
      exit 1
      ;;
  esac
  shift
done

# Validate semver argument
if [ -z "$SEMVER" ]; then
  echo "Error: --semver argument is required"
  echo "Usage: ./deploy.sh --semver \"0.2.0\""
  exit 1
fi

# Configuration
PREFIX="Packages/com.dreamtech.leaderboard"
BRANCH="upm"

# Guard: the tag must match the version Unity will read from package.json
PACKAGE_VERSION=$(sed -n 's/.*"version": *"\([^"]*\)".*/\1/p' "$PREFIX/package.json" | head -1)
if [ "$PACKAGE_VERSION" != "$SEMVER" ]; then
  echo "Error: $PREFIX/package.json has version $PACKAGE_VERSION, not $SEMVER. Bump it and commit first."
  exit 1
fi

# Guard: subtree split only sees committed history
if [ -n "$(git status --porcelain -- "$PREFIX")" ]; then
  echo "Error: uncommitted changes under $PREFIX. Commit them first."
  exit 1
fi

echo "================================"
echo "Deploying DreamTech Leaderboard"
echo "Version: $SEMVER"
echo "Prefix:  $PREFIX"
echo "Branch:  $BRANCH"
echo "================================"

# Step 1: Split the package folder into a separate branch
echo "Step 1/5: Splitting package from main branch..."
git subtree split --prefix="$PREFIX" --branch $BRANCH

# Step 2: Tag the version on the UPM branch
echo "Step 2/5: Creating tag $SEMVER..."
git tag $SEMVER $BRANCH

# Step 3: Push the UPM branch and tags to remote
echo "Step 3/5: Pushing to origin..."
git push origin $BRANCH --tags

# Step 4: Clean up remote branch (keeps tags)
echo "Step 4/5: Cleaning up remote branch..."
git push origin --delete $BRANCH || true

# Step 5: Clean up local branch
echo "Step 5/5: Cleaning up local branch..."
git branch -D $BRANCH

echo "================================"
echo "✅ Deployment Complete!"
echo ""
echo "Installation URL for users:"
echo "https://github.com/TeamAcMong/unity-leaderboard.git#$SEMVER"
echo ""
echo "Or in manifest.json:"
echo "{"
echo "  \"dependencies\": {"
echo "    \"com.dreamtech.leaderboard\": \"https://github.com/TeamAcMong/unity-leaderboard.git#$SEMVER\""
echo "  }"
echo "}"
echo "================================"
