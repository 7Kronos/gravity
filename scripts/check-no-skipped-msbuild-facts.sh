#!/usr/bin/env bash
# check-no-skipped-msbuild-facts.sh
#
# AC-9c.1 grep guard: assert that no [Fact(Skip=...)] attributes remain
# in Gravity.Dsl.Tests/MsBuild/ and that MsBuildIntegrationFixture is gone.
#
# Run from the repo root:
#   bash scripts/check-no-skipped-msbuild-facts.sh
#
# Exits 0 if both checks pass; exits 1 on any match (documented for PR review;
# not wired into CI by Phase 9c — FR-3040 carry-over).

set -euo pipefail

MSBUILD_TEST_DIR="Gravity.Dsl.Tests/MsBuild"
EXIT=0

echo "==> Checking for Fact(Skip=...) in ${MSBUILD_TEST_DIR}/"
if grep -RE "Fact\(Skip" "${MSBUILD_TEST_DIR}/"; then
    echo "ERROR: Found skipped Facts — these must be converted to harness wrappers (AC-9c.1)."
    EXIT=1
else
    echo "OK: No skipped Facts found."
fi

echo ""
echo "==> Checking for class MsBuildIntegrationFixture in ${MSBUILD_TEST_DIR}/"
if grep -RE "class[[:space:]]+MsBuildIntegrationFixture" "${MSBUILD_TEST_DIR}/"; then
    echo "ERROR: MsBuildIntegrationFixture still exists — it must be deleted (T430)."
    EXIT=1
else
    echo "OK: MsBuildIntegrationFixture not found."
fi

exit ${EXIT}
