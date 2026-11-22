#!/usr/bin/env bash
set -euo pipefail

if [[ "$(uname -s)" != "Darwin" ]]; then
    printf '%s\n' 'Building the app requires macOS and the Xcode command line tools.' >&2
    exit 1
fi

root="$(cd "$(dirname "$0")/.." && pwd)"
output="${1:-$root/dist/macos}"
mkdir -p "$output"
output="$(cd "$output" && pwd)"
stage="$(mktemp -d "$output/.build.XXXXXXXX")"
trap 'rm -rf -- "$stage"' EXIT

app="$output/Health Reminder.app"
bundle="$app/Contents"
mkdir -p "$bundle/MacOS" "$bundle/Resources"
sdk="$(xcrun --sdk macosx --show-sdk-path)"
sources=("$root/macos/Schedule.swift" "$root/macos/Pet.swift" "$root/macos/SettingsView.swift" "$root/macos/main.swift")

for arch in arm64 x86_64; do
    xcrun swiftc -parse-as-library -swift-version 5 -O -module-name HealthReminder \
        -target "$arch-apple-macos13.0" -sdk "$sdk" \
        -o "$stage/HealthReminder-$arch" "${sources[@]}"
done
xcrun lipo -create "$stage/HealthReminder-arm64" "$stage/HealthReminder-x86_64" \
    -output "$bundle/MacOS/HealthReminder"
cp "$root/macos/Info.plist" "$bundle/Info.plist"
cp "$root/src/default-pet.webp" "$bundle/Resources/default-pet.webp"
cp "$root/LICENSE" "$bundle/Resources/LICENSE.txt"
codesign --force --sign - --timestamp=none "$app"
codesign --verify --strict "$app"

if [[ "${HEALTH_REMINDER_BUILD_TESTS:-0}" == "1" ]]; then
    "$bundle/MacOS/HealthReminder" --self-test
    "$bundle/MacOS/HealthReminder" --smoke-test
fi

ditto -c -k --sequesterRsrc --keepParent "$app" "$stage/HealthReminder-macOS.zip"
mv -f "$stage/HealthReminder-macOS.zip" "$output/HealthReminder-macOS.zip"
printf '%s\n' "$output/HealthReminder-macOS.zip"
