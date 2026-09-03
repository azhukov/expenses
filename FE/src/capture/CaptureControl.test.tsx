import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import { CaptureControl } from './CaptureControl'

const navigate = vi.fn()

vi.mock('react-router', () => ({
  useNavigate: () => navigate,
}))

beforeEach(() => {
  navigate.mockReset()
})

afterEach(() => {
  vi.restoreAllMocks()
})

function captureInput() {
  return document.querySelector('input[type="file"]') as HTMLInputElement
}

describe('Camera opens on activation', () => {
  it('asks the browser for the rear-facing camera rather than a file browser', () => {
    render(<CaptureControl />)

    const input = captureInput()

    expect(input).toHaveAttribute('accept', 'image/*')
    expect(input).toHaveAttribute('capture', 'environment')
  })

  it('labels the control so that it can be activated by name', () => {
    render(<CaptureControl />)

    expect(screen.getByLabelText(/capture a receipt/i)).toBe(captureInput())
  })
})

describe('No intermediate step precedes the camera', () => {
  it('activates the file input directly, dispatching no navigation first', async () => {
    render(<CaptureControl />)

    const input = captureInput()
    const opened = vi.fn()
    input.addEventListener('click', opened)

    await userEvent.click(screen.getByLabelText(/capture a receipt/i))

    expect(opened).toHaveBeenCalled()
    expect(navigate).not.toHaveBeenCalled()
  })

  it('does not click the input programmatically', () => {
    const clickSpy = vi.spyOn(HTMLInputElement.prototype, 'click')

    render(<CaptureControl />)

    expect(clickSpy).not.toHaveBeenCalled()
  })
})

describe('Original bytes are preserved', () => {
  it('hands the capture screen the exact File the camera produced', async () => {
    render(<CaptureControl />)

    const photograph = new File(['original-bytes'], 'receipt.jpg', { type: 'image/jpeg' })

    await userEvent.upload(captureInput(), photograph)

    expect(navigate).toHaveBeenCalledWith('/capture', { state: { file: photograph } })
    expect(navigate.mock.calls[0][1].state.file).toBe(photograph)
  })
})

describe('An unusual format is not pre-judged', () => {
  it('carries a format it cannot display forward without reporting an error', async () => {
    render(<CaptureControl />)

    const photograph = new File(['heic-bytes'], 'IMG_0001.HEIC', { type: 'image/heic' })

    await userEvent.upload(captureInput(), photograph)

    expect(navigate).toHaveBeenCalledWith('/capture', { state: { file: photograph } })
    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
  })
})

describe('Capture is abandoned', () => {
  it('navigates nowhere and submits nothing when the camera is dismissed', async () => {
    const fetchMock = vi.fn()
    vi.stubGlobal('fetch', fetchMock)

    render(<CaptureControl />)

    const input = captureInput()
    await userEvent.click(screen.getByLabelText(/capture a receipt/i))
    input.dispatchEvent(new Event('change', { bubbles: true }))

    expect(navigate).not.toHaveBeenCalled()
    expect(fetchMock).not.toHaveBeenCalled()

    vi.unstubAllGlobals()
  })
})

describe('Home does not submit the image', () => {
  it('writes nothing to the ledger when a photograph is taken', async () => {
    const fetchMock = vi.fn()
    vi.stubGlobal('fetch', fetchMock)

    render(<CaptureControl />)

    await userEvent.upload(
      captureInput(),
      new File(['bytes'], 'receipt.jpg', { type: 'image/jpeg' }),
    )

    expect(fetchMock).not.toHaveBeenCalled()

    vi.unstubAllGlobals()
  })
})
