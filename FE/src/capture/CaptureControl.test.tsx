import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { createMemoryRouter, RouterProvider } from 'react-router'
import { afterEach, describe, expect, it, vi } from 'vitest'

import { CaptureControl } from './CaptureControl'

/**
 * Home's capture action. The camera is no longer opened here: the capture screen scans for the
 * fiscal code first, and holds the photograph control for when that produces no invoice (D40).
 * The photograph's own scenarios — the rear camera, a direct tap, untouched bytes, an unusual
 * format — moved with that control, to `routes/Capture.test.tsx`.
 */
function renderAtHome() {
  const router = createMemoryRouter(
    [
      { path: '/', element: <CaptureControl /> },
      { path: '/capture', element: <p>Capture screen</p> },
    ],
    { initialEntries: ['/'] },
  )

  render(<RouterProvider router={router} />)

  return router
}

afterEach(() => {
  vi.unstubAllGlobals()
})

describe('Camera opens on activation', () => {
  it('takes the user straight to the capture screen, which opens the camera', async () => {
    const router = renderAtHome()

    await userEvent.click(screen.getByRole('link', { name: /capture a receipt/i }))

    expect(router.state.location.pathname).toBe('/capture')
    expect(await screen.findByText('Capture screen')).toBeInTheDocument()
  })

  it('carries no camera input of its own', () => {
    renderAtHome()

    expect(document.querySelector('input[type="file"]')).toBeNull()
  })
})

describe('Home does not submit the image', () => {
  it('writes nothing to the ledger when the capture action is activated', async () => {
    const fetchMock = vi.fn()
    vi.stubGlobal('fetch', fetchMock)
    renderAtHome()

    await userEvent.click(screen.getByRole('link', { name: /capture a receipt/i }))

    expect(fetchMock).not.toHaveBeenCalled()
  })
})
