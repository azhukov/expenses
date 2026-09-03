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
