import { expect, test } from '@playwright/test'

import { confirmByHand, receipt, uniqueAmount } from './steps.js'

/**
 * The one flow the app exists for: photograph a receipt, correct what the extraction could not
 * read, confirm it, and see it in the month.
 *
 * Nothing in the client or the ledger is stubbed. The image is posted to the real capture endpoint, which stores it and
 * runs extraction before answering; the confirmation becomes a row in a real PostgreSQL; and the
 * assertion at the end is a second read of that database through the client's own query. The
 * seams this covers — the address served as /config.js, the API's CORS policy on a cross-origin
 * call, the multipart upload, the capture echo the server keeps no
 * copy of — are the ones no vitest or xUnit test can see.
 *
 * Extraction is deliberately not under test here: docker-compose.e2e.yml stands a stub in for the
 * fiscal portal, so no run depends on a government service being up, and what comes back is
 * whatever candidate lines the stack produces from it. The test does what a
 * person does when those lines are not the receipt — reduces them to one and types the figures in.
 * That is the path that must never break, and it is where the API's own invariant bites: a
 * purchase whose amount disagrees with the sum of its expenses is rejected.
 */

test('a photographed receipt becomes a purchase in the month', async ({ page }) => {
  await page.goto('/')
  await expect(page.getByTestId('month-header')).toBeVisible()

  await page.getByRole('link', { name: 'Capture a receipt' }).click()
  await expect(page).toHaveURL(/\/capture$/)

  // Headless Chromium offers no camera here, so the screen goes straight to the photograph control
  // ("Live camera refused or unavailable"). That control is a real file input, deliberately: on
  // iOS the camera opens only for the gesture that asked for it (D3). Handing it a file is the
  // closest a browser test gets to that.
  await page.getByLabel('Photograph the receipt').setInputFiles(receipt)

  await confirmByHand(page, `E2E Merchant ${Date.now()}`, uniqueAmount())
})
