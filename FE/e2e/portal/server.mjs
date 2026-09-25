// The fiscal verification service, for the end-to-end stack only (docker-compose.e2e.yml). It holds
// exactly one invoice — the response recorded once from the real portal for the fixture receipt,
// mounted read-only from the BE test fixtures so there is one copy of it — and has no record of any
// other, which it says the way the real portal does: 200 and an empty body.
//
// That split is what lets the suite drive both halves of QR-first capture against one stack: a code
// carrying the recorded IIC is fetched and goes straight to review, and any other code is not, and
// falls back to a photograph. No run reaches mapr.tax.gov.me.
import { readFileSync } from 'node:fs'
import { createServer } from 'node:http'

const invoice = readFileSync('/portal/invoice.json', 'utf8')
const { iic: recordedIic } = JSON.parse(invoice)

createServer((request, response) => {
  if (request.method === 'GET' && request.url === '/health') {
    response.writeHead(200).end()
    return
  }

  if (request.method !== 'POST' || request.url !== '/ic/api/verifyInvoice') {
    response.writeHead(404).end()
    return
  }

  let body = ''
  request.setEncoding('utf8')
  request.on('data', chunk => (body += chunk))
  request.on('end', () => {
    // Form-encoded, as the ledger sends it: iic, dateTimeCreated, tin.
    const iic = new URLSearchParams(body).get('iic')

    response.writeHead(200, { 'Content-Type': 'application/json' })
    response.end(iic === recordedIic ? invoice : '')
  })
}).listen(8080)
