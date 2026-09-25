import { expect, test, type Page } from '@playwright/test'

import { confirmByHand, receipt, uniqueAmount } from './steps.js'

/**
 * QR-first capture with a live camera (D40): the code is read and sent on its own. Where the
 * verification service holds the invoice, it goes straight to review with no photograph; where it
 * does not, the screen falls back to a photograph that carries the payload with it. Its own file
 * because a fake camera is a launch option, which Playwright applies per worker, not per test.
 *
 * The service is docker-compose.e2e.yml's stub portal, which holds exactly one invoice — the one
 * recorded from the real portal for the fixture receipt — and has no record of any other.
 */
// A fake camera stream, and permission to use it without a prompt. What the camera "sees" is
// decided by the detector installed below, not by the stream.
test.use({
  permissions: ['camera'],
  launchOptions: { args: ['--use-fake-device-for-media-stream', '--use-fake-ui-for-media-stream'] },
})

/** The one invoice the stub portal holds (BE/tests/.../Fixtures/verify-32AA….json). */
const recordedIkof = '32AA324CFF5030271E16D59F7F8EF636'

function payloadFor(ikof: string): string {
  return (
    `https://mapr.tax.gov.me/ic/#/verify?iic=${ikof}` +
    '&tin=02365928&crtd=2026-08-29T14:59:22+02:00&prc=59.65'
  )
}

type Captured = { state: string; fiscalPayload: string | null; supplied: { ikof: string | null } }

/**
 * Points the camera at a code carrying `payload`, and returns what the ledger answered to each
 * capture, keyed by path.
 *
 * The scanner library reads through the browser's own BarcodeDetector where there is one, so a
 * detector that "sees" this payload in every frame drives the real scanner module end to end, with
 * nothing in the client replaced (D40). The ledger's answer is read rather than the request because
 * the browser streams a multipart body that the test cannot read, and the answer echoes the payload
 * it was handed and the identifiers it parsed from it.
 */
async function aimedAt(page: Page, payload: string): Promise<Map<string, Captured>> {
  await page.addInitScript(read => {
    class Detector {
      static getSupportedFormats() {
        return Promise.resolve(['qr_code'])
      }

      detect() {
        return Promise.resolve([
          {
            rawValue: read,
            format: 'qr_code',
            boundingBox: new DOMRectReadOnly(0, 0, 10, 10),
            cornerPoints: [
              { x: 0, y: 0 },
              { x: 10, y: 0 },
              { x: 10, y: 10 },
              { x: 0, y: 10 },
            ],
          },
        ])
      }
    }

    Object.defineProperty(window, 'BarcodeDetector', { value: Detector })
  }, payload)

  const captured = new Map<string, Captured>()
  page.on('response', async response => {
    const path = new URL(response.url()).pathname
    if (response.request().method() === 'POST' && path.startsWith('/receipts/capture')) {
      captured.set(path, (await response.json()) as never)
    }
  })

  return captured
}

test('a code whose invoice is fetched goes to review and is confirmed with no photograph', async ({
  page,
}) => {
  const payload = payloadFor(recordedIkof)
  const captured = await aimedAt(page, payload)

  await page.goto('/')
  await page.getByRole('link', { name: 'Capture a receipt' }).click()

  // The code is read, sent on its own, and the stub portal answers with the invoice ("The invoice
  // is fetched"): review follows, and no photograph is asked for.
  await expect(page.getByRole('button', { name: 'Confirm' })).toBeVisible({ timeout: 20_000 })
  await expect(page.getByLabel('Photograph the receipt')).toHaveCount(0)

  const fiscal = captured.get('/receipts/capture-fiscal')
  expect(fiscal?.state).toBe('Extracted')
  expect(fiscal?.fiscalPayload).toBe(payload)

  await confirmByHand(page, `E2E Invoice ${Date.now()}`, uniqueAmount())

  // Confirmed from the payload alone: no image was ever uploaded.
  expect(captured.has('/receipts/capture')).toBe(false)
})

test('a code whose invoice cannot be fetched falls back to a photograph that carries it', async ({
  page,
}) => {
  const payload = payloadFor(`E2E${Date.now()}`)
  const captured = await aimedAt(page, payload)

  await page.goto('/')
  await page.getByRole('link', { name: 'Capture a receipt' }).click()

  // The stub portal has no record of this invoice, so the code is read, sent on its own, and comes
  // back with no invoice ("The invoice is not fetched").
  await expect(page.getByText(/could not fetch the invoice/i)).toBeVisible({ timeout: 20_000 })
  expect(captured.get('/receipts/capture-fiscal')?.fiscalPayload).toBe(payload)

  await page.getByLabel('Photograph the receipt').setInputFiles(receipt)

  await confirmByHand(page, `E2E Fiscal ${Date.now()}`, uniqueAmount())

  // The payload the camera read went with the photograph, so the ledger could still ask the
  // service about it rather than decoding the photograph for a second opinion (D31).
  const upload = captured.get('/receipts/capture')
  expect(upload?.fiscalPayload).toBe(payload)
  expect(upload?.supplied.ikof).toMatch(/^E2E/)
})
