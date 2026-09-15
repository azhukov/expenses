import { failureOf, LedgerError, ledgerUrl, send } from './client'
import type {
  CaptureResult,
  ExtractionState,
  FiscalSource,
  MerchantView,
  PurchaseView,
} from './types'

/**
 * What a client read from a receipt's fiscal QR before uploading it: the payload verbatim, not the
 * identifiers parsed out of it. The server parses it, so this format's two traps — parameters in
 * the URL fragment, and a '+' in the timestamp's offset that any form decoder eats — are handled in
 * one place rather than two (D30).
 */
export type SuppliedFiscalPayload = string

/**
 * Uploads one image and waits for extraction, which the endpoint runs before it answers. It does
 * not go through {@link send}: the body is multipart, and the browser has to set the boundary
 * itself, so a content type must not be named here.
 */
export async function captureReceipt(
  file: File,
  fiscalQr?: SuppliedFiscalPayload,
): Promise<CaptureResult> {
  const form = new FormData()
  form.append('file', file)

  // Absent rather than empty: an empty form field would assert a payload that was never read.
  if (fiscalQr) {
    form.append('fiscalQr', fiscalQr)
  }

  const url = ledgerUrl('/receipts/capture')
  let response: Response

  try {
    response = await fetch(url, {
      method: 'POST',
      headers: { accept: 'application/json' },
      body: form,
    })
  } catch {
    throw new LedgerError('The ledger could not be reached.')
  }

  if (!response.ok) {
    throw await failureOf(response)
  }

  return (await response.json()) as CaptureResult
}

/**
 * One line of a purchase as it is submitted. `categoryRaw`/`unitRaw` travel beside the matched
 * codes always, because a match must never erase what the receipt printed.
 */
export interface ExpenseRequest {
  description: string
  amount: number
  quantity?: number | null
  unitCode?: string | null
  unitPrice?: number | null
  categoryCode?: string | null
  categoryRaw?: string | null
  unitRaw?: string | null
  listUnitPrice?: number | null
  discountAmount?: number | null
}

/** A merchant as free text, matched or created server-side. */
export interface MerchantRequest {
  text: string
  taxId?: string | null
}

/**
 * What a client resubmits from a capture response in order to confirm it. Nothing about a capture
 * is held server-side, so this echo is the only way the server learns the extraction outcome
 * again — which is why none of it may be re-derived from what the user edited.
 */
export interface CapturedReceiptRequest {
  tempKey: string
  state: ExtractionState
  failureReason?: string | null

  /** Absent from the fiscal code and printed nowhere the server can read it (D24). */
  jikr?: string | null

  fiscalSource?: FiscalSource

  /**
   * The payload verbatim. The server parses the invoice code, issuer tax number, creation timestamp
   * and total out of it, so none of those is sent as a field of its own (D30, D32).
   */
  fiscalPayload?: string | null
}

/**
 * `occurredAt` may be omitted only when `capture` is supplied and its fiscal QR decoded an invoice
 * creation timestamp; the API rejects the request otherwise.
 */
export interface RecordPurchaseRequest {
  amount: number
  expenses: ExpenseRequest[]
  merchant?: MerchantRequest | null
  occurredAt?: string | null
  capture?: CapturedReceiptRequest | null
}

/** A recorded purchase, with `alreadyRecorded` telling the two success outcomes apart (D3). */
export interface RecordPurchaseResponse extends PurchaseView {
  alreadyRecorded: boolean
  merchant: MerchantView | null
  merchantNewlyAdded: boolean
}

/** Records a purchase, with its captured receipt attached where one is carried. */
export function recordPurchase(command: RecordPurchaseRequest): Promise<RecordPurchaseResponse> {
  return send<RecordPurchaseResponse>('/purchases', command)
}
