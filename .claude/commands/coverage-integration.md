---
description: Run the integration tests with code coverage and open a browsable HTML report
allowed-tools: Bash(powershell -NoProfile -ExecutionPolicy Bypass -File BE/scripts/coverage-integration.ps1)
---

!`powershell -NoProfile -ExecutionPolicy Bypass -File BE/scripts/coverage-integration.ps1`

Above is the output of [BE/scripts/coverage-integration.ps1](BE/scripts/coverage-integration.ps1),
which ran `Expenses.Integration.Tests` with coverage, wrote a browsable report to
`BE/TestResults/report/index.html`, and opened it in the browser.

Report back, and nothing more:

1. The pass/fail tally from the `=== Tests ===` block. If any tests failed, list each failing test
   name and its assertion message.
2. A markdown table from the `=== Coverage ===` block: total line and branch coverage first, then
   one row per assembly.
3. The path to the HTML report.

Do not re-run the tests, edit any files, or fix anything unless asked.
