import { render, screen } from '@testing-library/react'
import { createMemoryRouter, RouterProvider } from 'react-router'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import { useCategories, useMerchants, usePurchases } from '../api/queries'
import type { PurchaseView } from '../api/types'
import { Home } from './Home'

vi.mock('../api/queries', () => ({
  usePurchases: vi.fn(),
  useMerchants: vi.fn(),
  useCategories: vi.fn(),
}))

/** What jsdom can see of the layout: declared rules, not rendered geometry. */
const MINIMUM_TOUCH_TARGET = 44

const purchases: PurchaseView[] = [
  {
    id: 1,
    occurredAt: new Date().toISOString().slice(0, 19),
    amount: 10,
    merchantId: null,
    merchantRaw: 'CAFE BAR PEPE',
    hasReceiptImage: false,
    fiscal: null,
    expenses: [],
    totalSaving: 0,
    savingPercentage: null,
    extractionState: null,
  },
]

function query(state: Record<string, unknown>) {
  return {
    data: undefined,
    isPending: false,
    isError: false,
    error: null,
    refetch: vi.fn(),
    ...state,
  } as never
}

beforeEach(() => {
  vi.mocked(usePurchases).mockReturnValue(query({ data: purchases }))
  vi.mocked(useMerchants).mockReturnValue(query({ data: [] }))
  vi.mocked(useCategories).mockReturnValue(query({ data: [] }))
})

function renderHome() {
  return render(
    <RouterProvider
      router={createMemoryRouter([{ path: '/', element: <Home /> }], { initialEntries: ['/'] })}
    />,
  )
}

/**
 * The element a finger actually lands on. For the capture control that is the label, not the
 * visually hidden input inside it.
 */
function touchTargets(container: HTMLElement): HTMLElement[] {
  const targets = new Set<HTMLElement>()

  for (const control of container.querySelectorAll<HTMLElement>(
    'button, a, input, select, textarea, [role="button"]',
  )) {
    targets.add(control.closest('label') ?? control)
  }

  return [...targets]
}

function pixels(value: string): number {
  return value.endsWith('px') ? Number.parseFloat(value) : Number.NaN
}

describe('Touch targets are large enough', () => {
  it('declares at least 44 by 44 CSS pixels on every interactive control', () => {
    const { container } = renderHome()

    const targets = touchTargets(container)

    expect(targets.length).toBeGreaterThan(0)

    for (const target of targets) {
      const style = getComputedStyle(target)

      expect(pixels(style.minHeight)).toBeGreaterThanOrEqual(MINIMUM_TOUCH_TARGET)
      expect(pixels(style.minWidth)).toBeGreaterThanOrEqual(MINIMUM_TOUCH_TARGET)
    }
  })

  it('declares them on the retry control a failed read offers too', () => {
    vi.mocked(usePurchases).mockReturnValue(
      query({ isError: true, error: new Error('The ledger could not be reached.') }),
    )

    const { container } = renderHome()

    const retry = screen.getByRole('button', { name: /try again/i })

    expect(touchTargets(container)).toContain(retry)

    const style = getComputedStyle(retry)

    expect(pixels(style.minHeight)).toBeGreaterThanOrEqual(MINIMUM_TOUCH_TARGET)
    expect(pixels(style.minWidth)).toBeGreaterThanOrEqual(MINIMUM_TOUCH_TARGET)
  })
})

describe('Capture stays reachable while browsing recent purchases', () => {
  it('scrolls the list rather than the page', () => {
    renderHome()

    const recent = screen.getByTestId('recent')

    expect(getComputedStyle(recent).overflowY).toBe('auto')
  })

  it('anchors the capture control outside the region that scrolls', () => {
    renderHome()

    const recent = screen.getByTestId('recent')
    const dock = screen.getByTestId('capture-dock')

    expect(recent.contains(dock)).toBe(false)
    expect(getComputedStyle(dock).flexShrink).toBe('0')
  })

  it('sizes the page to the visible viewport rather than to its content', () => {
    renderHome()

    expect(getComputedStyle(screen.getByTestId('page')).height).toBe('100dvh')
  })
})

describe('The bottom of the screen is not obscured', () => {
  it('pads the capture container by the safe-area inset', () => {
    renderHome()

    const dock = screen.getByTestId('capture-dock')

    expect(dock.className).toBeTruthy()
    expect(styleRuleFor(dock)).toMatch(/env\(safe-area-inset-bottom/)
  })

  it('pads the header by the safe-area inset', () => {
    renderHome()

    expect(styleRuleFor(screen.getByTestId('month-header'))).toMatch(/env\(safe-area-inset-top/)
  })
})

/**
 * jsdom resolves neither `env()` nor a custom property, so the declaration itself is what can be
 * asserted on. The rendered result is a manual device check (D15).
 */
function styleRuleFor(element: HTMLElement): string {
  const rules: string[] = []

  for (const sheet of document.styleSheets) {
    for (const rule of sheet.cssRules) {
      if (rule instanceof CSSStyleRule && element.matches(rule.selectorText)) {
        rules.push(rule.cssText)
      }
    }
  }

  return rules.join('\n')
}

describe('No horizontal scrolling', () => {
  it('constrains a long merchant name rather than letting it widen the row', () => {
    vi.mocked(usePurchases).mockReturnValue(
      query({
        data: [
          {
            ...purchases[0],
            merchantRaw: 'SUPERMERCADO CENTRAL DE ALIMENTACION Y BEBIDAS SOCIEDAD LIMITADA',
          },
        ],
      }),
    )

    renderHome()

    const merchant = screen.getByText(/SUPERMERCADO CENTRAL/)
    const style = getComputedStyle(merchant)

    expect(style.overflow).toBe('hidden')
    expect(style.textOverflow).toBe('ellipsis')
    expect(style.whiteSpace).toBe('nowrap')
  })

  it('lets the identity column shrink below its content width', () => {
    renderHome()

    const identity = screen.getByText('CAFE BAR PEPE').parentElement as HTMLElement

    expect(getComputedStyle(identity).minWidth).toBe('0px')
  })
})
