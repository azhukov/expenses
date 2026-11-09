import { read, readBlob } from './client'
import type { CategoryView, MerchantView, PurchaseView, UnitView } from './types'

/** A range of occurrence dates, as the API's `DateOnly` query parameters: `yyyy-MM-dd`. */
export interface DateRange {
  from: string
  to: string
}

/** Purchases occurring in a date range, most recent first — the order the API already returns. */
export function listPurchases(range: DateRange): Promise<PurchaseView[]> {
  const query = new URLSearchParams({ from: range.from, to: range.to })

  return read<PurchaseView[]>(`/purchases?${query.toString()}`)
}

/** The merchant dictionary. Active entries only, which is what a display name is resolved from. */
export function listMerchants(): Promise<MerchantView[]> {
  return read<MerchantView[]>('/merchants')
}

/** The category dictionary. */
export function listCategories(): Promise<CategoryView[]> {
  return read<CategoryView[]>('/categories')
}

/** The unit dictionary, addressed by code the way categories are (D8 in the API). */
export function listUnits(): Promise<UnitView[]> {
  return read<UnitView[]>('/units')
}

/** How many purchases the ledger holds, across every date, not only the month Home shows. */
export async function countPurchases(): Promise<number> {
  const { count } = await read<{ count: number }>('/purchases/count')

  return count
}

/**
 * The ledger as a CSV file, for every purchase or an inclusive range: the bytes the ledger wrote,
 * untouched, for the caller to save (ledger-export).
 */
export function exportPurchases(range?: DateRange): Promise<Blob> {
  const query = range
    ? `?${new URLSearchParams({ from: range.from, to: range.to }).toString()}`
    : ''

  return readBlob(`/purchases/export${query}`, 'text/csv')
}
