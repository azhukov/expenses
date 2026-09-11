/**
 * The API's read shapes, written by hand rather than generated: three endpoints and five shapes do
 * not pay for a codegen step and a build-time dependency on a running API (D14). The cost is that
 * these can drift from the backend silently; revisit once capture and confirm land.
 *
 * Property names are camelCase because that is what ASP.NET Core serialises by default, and enums
 * travel as their names.
 */

/** Where an attached receipt's extraction has got to. `Pending` and `Extracting` no longer exist. */
export type ExtractionState = 'Extracted' | 'NeedsReview' | 'Failed'

/** One expense line. `discountPercentage` is derived by the backend, never stored. */
export interface ExpenseView {
  id: number
  description: string
  quantity: number
  amount: number
  unitId: number | null
  unitPrice: number | null
  categoryId: number | null
  categoryRaw: string | null
  unitRaw: string | null
  listUnitPrice: number | null
  discountAmount: number | null
  discountPercentage: number | null
}

/**
 * A purchase as it is read back. `merchantRaw` travels beside `merchantId` always, because a later
 * match must never erase what was printed — which is why the display name has to be resolved here
 * rather than read off the purchase (D6).
 *
 * `occurredAt` is an offset-less timestamp: wall-clock time, parsed as local (D12).
 */
export interface PurchaseView {
  id: number
  occurredAt: string
  amount: number
  merchantId: number | null
  merchantRaw: string | null
  hasReceipt: boolean
  expenses: ExpenseView[]
  totalSaving: number
  savingPercentage: number | null
  /** Null when the purchase carries no receipt. */
  extractionState: ExtractionState | null
}

/** A merchant in the dictionary the ledger learns. It has no code; its identity is its tax id. */
export interface MerchantView {
  id: number
  name: string
  taxId: string | null
  parentId: number | null
  parentName: string | null
  isActive: boolean
}

/** A category in the seeded and user-extended dictionary. */
export interface CategoryView {
  id: number
  code: string
  name: string
  parentId: number | null
  parentCode: string | null
  isSystem: boolean
  isActive: boolean
}

/**
 * The single error shape every failure of the interface produces. Its `message` is written for a
 * person, so it is what the client shows rather than a vocabulary of its own.
 */
export interface ErrorResponse {
  code: string
  message: string
  fields: Record<string, unknown>
  correlationId: string | null
}

/** Where a fiscal identifier came from. `None` is the absence of any (D20 in the API). */
export type FiscalSource =
  'None' | 'SuppliedAtUpload' | 'DecodedFromCode' | 'ReadAsText' | 'RetrievedFromService'

/** How one arithmetic check came out. */
export type CheckOutcome = 'Passed' | 'Failed' | 'NotApplicable'

/**
 * One arithmetic check, carrying the values that disagreed so the reason survives to the user
 * rather than being reduced to a boolean.
 */
export interface ArithmeticCheck {
  name: string
  outcome: CheckOutcome
  description: string
  values: Record<string, number | null>
}

export interface ArithmeticValidationReport {
  checks: ArithmeticCheck[]
}

/**
 * The fiscal identifiers known from one source. A decoded QR also states the issuer, when the
 * invoice was created and what it came to; the creation timestamp is what lets a purchase be
 * recorded without the user entering a date.
 */
export interface FiscalIdentifiers {
  ikof: string | null
  jikr: string | null
  issuerTaxNumber: string | null
  createdAt: string | null
  total: number | null
}

/**
 * One proposed line. It carries raw numbers the domain would reject — which is why candidates are
 * held apart from a purchase — beside the stage that produced each value and the confidence that
 * stage reported about it.
 */
export interface ExtractionCandidateView {
  lineNumber: number
  description: string
  amount: number
  quantity: number | null
  unitPrice: number | null
  listUnitPrice: number | null
  discountAmount: number | null
  taxRatePercent: number | null
  categoryRaw: string | null
  unitRaw: string | null
  categoryId: number | null
  unitId: number | null
  provenance: Record<string, string>
  reportedConfidence: Record<string, number>
}

/** What one run of the extraction cascade produced, with the engine that produced it. */
export interface ExtractionResultView {
  engineName: string
  engineVersion: string
  stepsRun: string[]
  candidates: ExtractionCandidateView[]
  total: number | null
  taxRatePercent: number | null
  taxAmount: number | null
  merchantName: string | null
  merchantTaxId: string | null
  provenance: Record<string, string>
  reportedConfidence: Record<string, number>
}

/**
 * What capturing an image produced. Nothing here is held server-side: the client carries this
 * forward and resubmits the parts confirmation needs, which is why the review screen keeps it
 * untouched beside the user's edits.
 */
export interface CaptureResult {
  tempKey: string
  state: ExtractionState
  failureReason: string | null
  /** Null where extraction failed. */
  result: ExtractionResultView | null
  validation: ArithmeticValidationReport | null
  supplied: FiscalIdentifiers
  extracted: FiscalIdentifiers
  fiscalSource: FiscalSource
  fiscalPayload: string | null
}

/** What a unit is measured in. Like a category, it is addressed by code rather than by name. */
export type UnitKind = 'Count' | 'Mass' | 'Volume'

/** A unit in the seeded dictionary. */
export interface UnitView {
  id: number
  code: string
  name: string
  symbol: string
  kind: UnitKind
  isActive: boolean
}
