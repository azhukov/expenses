import { read } from './client'
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
