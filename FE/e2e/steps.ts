import { fileURLToPath } from 'node:url'

import { expect, type Page } from '@playwright/test'

export const receipt = fileURLToPath(new URL('./fixtures/receipt.jpg', import.meta.url))

/** A distinct amount per run, so the assertion cannot match a row an earlier run left behind. */
export function uniqueAmount(): number {
  return Math.round((10 + Math.random() * 80) * 100) / 100
}

/** What `formatAmount` will render for that value: en-GB, EUR, always two decimals. */
export function formatted(value: number): string {
  return new Intl.NumberFormat('en-GB', {
    style: 'currency',
    currency: 'EUR',
    minimumFractionDigits: 2,
    maximumFractionDigits: 2,
  }).format(value)
}

/** `datetime-local` wants wall-clock text, and the ledger stores exactly that (D12). */
export function localNow(): string {
  const now = new Date()
  const pad = (value: number) => String(value).padStart(2, '0')

  return `${now.getFullYear()}-${pad(now.getMonth() + 1)}-${pad(now.getDate())}T${pad(now.getHours())}:${pad(now.getMinutes())}`
}

/**
 * What a person does when the extraction's lines are not the receipt: reduce them to one, type the
 * figures in, confirm — and then find the purchase in the month, read back from the ledger.
 */
export async function confirmByHand(page: Page, merchant: string, amount: number) {
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

  // The client's own rules first: an incomplete line is refused here, not by the ledger. Nothing
  // is sent, the offending inputs are marked, and the screen stays where it is
  // ("The review screen refuses an incomplete purchase").
  await line.getByLabel('Description').fill('')
  await line.getByLabel('Amount', { exact: true }).fill('')
  await page.getByRole('button', { name: 'Confirm' }).click()

  await expect(page).toHaveURL(/\/capture$/)
  await expect(line.getByLabel('Description')).toHaveAttribute('aria-invalid', 'true')
  await expect(line.getByLabel('Amount', { exact: true })).toHaveAttribute('aria-invalid', 'true')

  await line.getByLabel('Description').fill('Groceries')
  await line.getByLabel('Amount', { exact: true }).fill(amount.toFixed(2))
  await line.getByLabel('Quantity').fill('1')

  // A unit is required on every line, by the client and by the ledger alike. Chosen by position
  // because which units are seeded is the ledger's business, not this test's.
  await line.getByLabel('Unit').selectOption({ index: 1 })

  // Corrected, the marks are gone before anything is sent.
  await expect(line.getByLabel('Description')).not.toHaveAttribute('aria-invalid', 'true')

  await page.getByRole('button', { name: 'Confirm' }).click()

  // Confirming navigates home and invalidates the month query, so what renders next is a fresh
  // read of the ledger rather than anything this page held.
  await expect(page).toHaveURL(/\/$/)

  const row = page.getByTestId('recent').getByRole('listitem').filter({ hasText: merchant })
  await expect(row).toBeVisible()
  await expect(row).toContainText(formatted(amount))
  // The receipt travelled with the confirmation; the row says so.
  await expect(row).toContainText('Receipt')
}
