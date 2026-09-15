import { fileURLToPath } from 'node:url'

import { expect, test } from '@playwright/test'

/**
 * The one flow the app exists for: photograph a receipt, correct what the extraction could not
 * read, confirm it, and see it in the month.
 *
 * Nothing here is stubbed. The image is posted to the real capture endpoint, which stores it and
 * runs extraction before answering; the confirmation becomes a row in a real PostgreSQL; and the
 * assertion at the end is a second read of that database through the client's own query. The
 * seams this covers — the address served as /config.js, the API's CORS policy on a cross-origin
 * call, the multipart upload, the capture echo the server keeps no
 * copy of — are the ones no vitest or xUnit test can see.
 *
 * Extraction is deliberately not under test here: docker-compose.e2e.yml points the fiscal portal
 * at an address that does not answer, so no run depends on a government service being up, and what
 * comes back is whatever candidate lines the stack produces without it. The test does what a
 * person does when those lines are not the receipt — reduces them to one and types the figures in.
 * That is the path that must never break, and it is where the API's own invariant bites: a
 * purchase whose amount disagrees with the sum of its expenses is rejected.
 */

const receipt = fileURLToPath(new URL('./fixtures/receipt.jpg', import.meta.url))

/** A distinct amount per run, so the assertion cannot match a row an earlier run left behind. */
function uniqueAmount(): number {
  return Math.round((10 + Math.random() * 80) * 100) / 100
}

/** What `formatAmount` will render for that value: en-GB, EUR, always two decimals. */
function formatted(value: number): string {
  return new Intl.NumberFormat('en-GB', {
    style: 'currency',
    currency: 'EUR',
    minimumFractionDigits: 2,
    maximumFractionDigits: 2,
  }).format(value)
}

/** `datetime-local` wants wall-clock text, and the ledger stores exactly that (D12). */
function localNow(): string {
  const now = new Date()
  const pad = (value: number) => String(value).padStart(2, '0')

  return `${now.getFullYear()}-${pad(now.getMonth() + 1)}-${pad(now.getDate())}T${pad(now.getHours())}:${pad(now.getMinutes())}`
}

test('a photographed receipt becomes a purchase in the month', async ({ page }) => {
  const amount = uniqueAmount()
  const merchant = `E2E Merchant ${Date.now()}`

  await page.goto('/')
  await expect(page.getByTestId('month-header')).toBeVisible()

  // The capture control is a real file input, deliberately: on iOS the camera opens only for the
  // gesture that asked for it (D3). Handing it a file is the closest a browser test gets to that.
  await page.locator('input[type="file"]').setInputFiles(receipt)

  await expect(page).toHaveURL(/\/capture$/)
  // The upload waits out extraction, which the endpoint runs synchronously before it answers.
  await expect(page.getByRole('button', { name: 'Confirm' })).toBeVisible({ timeout: 45_000 })

  await page.getByLabel('Merchant').fill(merchant)
  await page.getByLabel('Date').fill(localNow())
  await page.getByLabel('Total amount').fill(amount.toFixed(2))

  // Down to a single line, whatever extraction offered. The API rejects a purchase whose amount
  // disagrees with the sum of its expenses, so leaving a candidate line behind would mean the
  // total no longer describes what is on the form — the same correction a person makes by hand.
  const lines = page.getByTestId('line')

  while ((await lines.count()) > 1) {
    await lines.last().getByRole('button', { name: 'Remove' }).click()
  }

  const line = lines.first()
  await line.getByLabel('Description').fill('Groceries')
  await line.getByLabel('Amount', { exact: true }).fill(amount.toFixed(2))
  await line.getByLabel('Quantity').fill('1')

  await page.getByRole('button', { name: 'Confirm' }).click()

  // Confirming navigates home and invalidates the month query, so what renders next is a fresh
  // read of the ledger rather than anything this page held.
  await expect(page).toHaveURL(/\/$/)

  const row = page.getByTestId('recent').getByRole('listitem').filter({ hasText: merchant })
  await expect(row).toBeVisible()
  await expect(row).toContainText(formatted(amount))
  // The receipt travelled with the confirmation; the row says so.
  await expect(row).toContainText('Receipt')
})
