import { useQuery } from '@tanstack/react-query'

import {
  countPurchases,
  listCategories,
  listMerchants,
  listPurchases,
  listUnits,
  type DateRange,
} from './reads'

const HOUR = 60 * 60 * 1000

export const queryKeys = {
  /** Every month's purchases share this prefix, so one invalidation refreshes them all. */
  allPurchases: ['purchases'] as const,
  purchases: (range: DateRange) => ['purchases', range.from, range.to] as const,
  purchaseCount: ['purchase-count'] as const,
  merchants: ['merchants'] as const,
  categories: ['categories'] as const,
  units: ['units'] as const,
}

/**
 * The month in one round trip. Home's three questions — the total, the review count and the recent
 * few — are all derived from this one response rather than from an aggregation endpoint that does
 * not exist (D5).
 */
export function usePurchases(range: DateRange) {
  return useQuery({
    queryKey: queryKeys.purchases(range),
    queryFn: () => listPurchases(range),
  })
}

/**
 * The dictionaries, each its own query with a long stale time. Separate on purpose: reference data
 * failing must degrade a row to its verbatim receipt text, never fail the page (D6).
 */
export function useMerchants() {
  return useQuery({
    queryKey: queryKeys.merchants,
    queryFn: listMerchants,
    staleTime: HOUR,
  })
}

export function useCategories() {
  return useQuery({
    queryKey: queryKeys.categories,
    queryFn: listCategories,
    staleTime: HOUR,
  })
}

export function useUnits() {
  return useQuery({
    queryKey: queryKeys.units,
    queryFn: listUnits,
    staleTime: HOUR,
  })
}

/**
 * The whole ledger's count, read only while a clear is being confirmed and never served stale: the
 * number shown is the number the clear will be told to expect.
 */
export function usePurchaseCount() {
  return useQuery({
    queryKey: queryKeys.purchaseCount,
    queryFn: countPurchases,
    staleTime: 0,
  })
}
