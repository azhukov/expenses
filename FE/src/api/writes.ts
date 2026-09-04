import { failureOf, LedgerError, send } from './client'
import type {
  CaptureResult,
  ExtractionState,
  FiscalSource,
  MerchantView,
  PurchaseView,
} from './types'

/** The dev proxy and a same-origin deployment both put the API here (D9). */
const BASE = '/api'

/** Identifiers a client decoded from a receipt's fiscal QR before uploading it. */
export interface SuppliedFiscalIdentifiers {
  ikof?: string | null
  jikr?: string | null
}

/**
 * Uploads one image and waits for extraction, which the endpoint runs before it answers. It does
 * not go through {@link send}: the body is multipart, and the browser has to set the boundary
 * itself, so a content type must not be named here.
 */
export async function captureReceipt(
  file: File,
  fiscal?: SuppliedFiscalIdentifiers,
): Promise<CaptureResult> {
  const form = new FormData()
  form.append('file', file)

  // Absent rather than empty: an empty form field would assert an identifier that was never read.
  if (fiscal?.ikof) {
    form.append('fiscalIkof', fiscal.ikof)
  }

  if (fiscal?.jikr) {
    form.append('fiscalJikr', fiscal.jikr)
  }

  let response: Response

  try {
    response = await fetch(`${BASE}/receipts/capture`, {
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
  suppliedIkof?: string | null
  suppliedJikr?: string | null
  extractedIkof?: string | null
  extractedJikr?: string | null
  fiscalExtractedSource?: FiscalSource
  fiscalCreatedAt?: string | null
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
