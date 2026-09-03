import { describe, expect, it } from 'vitest'

import type { CategoryView, MerchantView, PurchaseView } from '../api/types'
import { categoryName, merchantLabel, UNKNOWN_CATEGORY, UNKNOWN_MERCHANT } from './labels'

const merchants: MerchantView[] = [
  { id: 7, name: 'Mercadona', taxId: 'A46103834', parentId: null, parentName: null, isActive: true },
]

const categories: CategoryView[] = [
  {
    id: 3,
    code: 'groceries',
    name: 'Groceries',
    parentId: null,
    parentCode: null,
    isSystem: true,
    isActive: true,
  },
]

function purchase(overrides: Partial<PurchaseView>): PurchaseView {
  return {
    id: 1,
    occurredAt: '2026-09-03T10:00:00',
    amount: 12.5,
    merchantId: null,
    merchantRaw: null,
    hasReceipt: false,
    expenses: [],
    totalSaving: 0,
    savingPercentage: null,
    extractionState: null,
    ...overrides,
  }
}

describe('A known merchant is named', () => {
  it('shows the display name the dictionary describes', () => {
    const label = merchantLabel(purchase({ merchantId: 7, merchantRaw: 'MERCADONA S.A.' }), merchants)

    expect(label).toBe('Mercadona')
  })
})

describe('An unmatched merchant falls back to what was printed', () => {
  it('shows the verbatim text when no merchant is referenced', () => {
    const label = merchantLabel(purchase({ merchantId: null, merchantRaw: 'CAFE BAR PEPE' }), merchants)

    expect(label).toBe('CAFE BAR PEPE')
  })

  it('shows the verbatim text when the dictionary does not describe the merchant', () => {
    const label = merchantLabel(purchase({ merchantId: 99, merchantRaw: 'CAFE BAR PEPE' }), merchants)

    expect(label).toBe('CAFE BAR PEPE')
  })

  it('falls back when the dictionary could not be read at all', () => {
    const label = merchantLabel(purchase({ merchantId: 7, merchantRaw: 'MERCADONA S.A.' }), [])

    expect(label).toBe('MERCADONA S.A.')
  })
})

describe('Neither a merchant nor verbatim text', () => {
  it('states that the merchant is unknown', () => {
    expect(merchantLabel(purchase({}), merchants)).toBe(UNKNOWN_MERCHANT)
  })

  it('treats blank verbatim text as absent', () => {
    expect(merchantLabel(purchase({ merchantRaw: '   ' }), merchants)).toBe(UNKNOWN_MERCHANT)
  })
})

describe('Identifiers are never displayed', () => {
  it('never resolves a merchant label containing its identifier', () => {
    const labels = [
      merchantLabel(purchase({ merchantId: 7, merchantRaw: 'MERCADONA S.A.' }), merchants),
      merchantLabel(purchase({ merchantId: 99, merchantRaw: 'CAFE BAR PEPE' }), merchants),
      merchantLabel(purchase({ merchantId: 99 }), merchants),
    ]

    for (const label of labels) {
      expect(label).not.toMatch(/\b(7|99)\b/)
    }
  })

  it('never resolves a category name to its identifier', () => {
    expect(categoryName(3, categories)).toBe('Groceries')
    expect(categoryName(41, categories)).toBe(UNKNOWN_CATEGORY)
    expect(categoryName(41, categories)).not.toMatch(/41/)
    expect(categoryName(null, categories)).toBe(UNKNOWN_CATEGORY)
  })
})

describe('A category falls back the same way a merchant does', () => {
  it('prefers the verbatim category text over saying it is unknown', () => {
    expect(categoryName(41, categories, 'FRUTA')).toBe('FRUTA')
    expect(categoryName(null, categories, 'FRUTA')).toBe('FRUTA')
  })
})
