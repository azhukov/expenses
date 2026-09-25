import { expect, test } from '@playwright/test'

import { confirmByHand, receipt, uniqueAmount } from './steps.js'

/**
 * QR-first capture with a live camera (D40): the code is read, sent on its own, and — because the
 * e2e stack's portal never answers — comes back with no invoice, so the screen falls back to a
 * photograph that carries the payload with it. Its own file because a fake camera is a launch
 * option, which Playwright applies per worker, not per test.
 *
 * The path where the invoice is fetched and no photograph is taken needs a portal that answers;
 * the e2e stack has none, so that path is covered against the real HTTP host and a stub portal in
 * BE/tests/Expenses.Integration.Tests/Api/FiscalCaptureTests.
 */
// A fake camera stream, and permission to use it without a prompt. What the camera "sees" is
// decided by the detector installed below, not by the stream.
test.use({
  permissions: ['camera'],
  launchOptions: { args: ['--use-fake-device-for-media-stream', '--use-fake-ui-for-media-stream'] },
})

test('a code whose invoice cannot be fetched falls back to a photograph that carries it', async ({
  page,
}) => {
  const payload =
    `https://mapr.tax.gov.me/ic/#/verify?iic=E2E${Date.now()}` +
    '&tin=02365928&crtd=2026-08-29T14:59:22+02:00&prc=12.40'

  // The scanner library reads through the browser's own BarcodeDetector where there is one, so a
  // detector that "sees" this payload in every frame drives the real scanner module end to end,
  // with nothing in the client replaced (D40).
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

  // What the ledger received on each capture, as it echoes it back: the payload it was handed and
  // the identifiers it parsed from it. The response rather than the request, because the browser
  // streams a multipart body that the test cannot read.
  const captured = new Map<
    string,
    { fiscalPayload: string | null; supplied: { ikof: string | null } }
  >()
  page.on('response', async response => {
    const path = new URL(response.url()).pathname
    if (response.request().method() === 'POST' && path.startsWith('/receipts/capture')) {
      captured.set(path, (await response.json()) as never)
    }
  })

  await page.goto('/')
  await page.getByRole('link', { name: 'Capture a receipt' }).click()

  // docker-compose.e2e.yml points the portal at an address that never answers, so the code is
  // read, sent on its own, and comes back with no invoice ("The invoice is not fetched").
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
