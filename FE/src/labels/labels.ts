import type { CategoryView, MerchantView, PurchaseView } from '../api/types'

export const UNKNOWN_MERCHANT = 'Unknown merchant'
export const UNKNOWN_CATEGORY = 'Uncategorised'

function present(text: string | null | undefined): string | null {
  const trimmed = text?.trim()

  return trimmed ? trimmed : null
}

/**
 * The fixed fallback chain (D6):
 *
 *   merchant name  ??  verbatim receipt text  ??  "Unknown merchant"
 *
 * The middle link is not a workaround. The ledger keeps what was printed beside the match on
 * purpose, so showing it for an unmatched merchant is the honest display, and doubles as the
 * visible signal that the merchant has not been learned yet. An identifier is never shown, and a
 * purchase is never withheld because its merchant could not be named.
 */
export function merchantLabel(purchase: PurchaseView, merchants: MerchantView[]): string {
  const matched =
    purchase.merchantId === null
      ? undefined
      : merchants.find(merchant => merchant.id === purchase.merchantId)

  return present(matched?.name) ?? present(purchase.merchantRaw) ?? UNKNOWN_MERCHANT
}

/** The same discipline for a category: the dictionary, then what the receipt printed, then neither. */
export function categoryName(
  categoryId: number | null,
  categories: CategoryView[],
  categoryRaw?: string | null,
): string {
  const matched =
    categoryId === null ? undefined : categories.find(category => category.id === categoryId)

  return present(matched?.name) ?? present(categoryRaw) ?? UNKNOWN_CATEGORY
}
