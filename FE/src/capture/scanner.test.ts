import { beforeEach, describe, expect, it, vi } from 'vitest'

/**
 * The library itself is replaced: jsdom has neither a camera nor a worker, and what is being
 * specified here is how the module folds the library's outcomes into the one the screen reads (D40).
 */
const hasCamera = vi.fn<() => Promise<boolean>>()
const start = vi.fn<() => Promise<void>>()
const stop = vi.fn()
const destroy = vi.fn()
let decode: (result: { data: string }) => void = () => {}
let options: { highlightScanRegion?: boolean } = {}

vi.mock('qr-scanner', () => ({
  default: class {
    static hasCamera = hasCamera

    $overlay?: HTMLDivElement

    constructor(
      video: HTMLVideoElement,
      onDecode: (result: { data: string }) => void,
      given: { highlightScanRegion?: boolean },
    ) {
      decode = onDecode
      options = given

      // As the library does: the overlay goes in beside the video, and destroying the scanner
      // hides it but leaves it in the document.
      if (given.highlightScanRegion) {
        this.$overlay = document.createElement('div')
        video.after(this.$overlay)
      }
    }

    start = start
    stop = stop
    destroy = destroy
  },
}))

const { startScanning } = await import('./scanner')

function video() {
  const element = document.createElement('video')
  document.createElement('div').append(element)

  return element
}

beforeEach(() => {
  hasCamera.mockReset().mockResolvedValue(true)
  start.mockReset().mockResolvedValue()
  stop.mockReset()
  destroy.mockReset()
})

describe('Scanning the rear camera for a QR code', () => {
  it('hands on the first payload read, and only the first', async () => {
    const onRead = vi.fn()
    await startScanning(video(), onRead)

    decode({ data: 'first' })
    decode({ data: 'second' })

    expect(onRead).toHaveBeenCalledTimes(1)
    expect(onRead).toHaveBeenCalledWith('first')
  })

  it('releases the camera on the first read', async () => {
    await startScanning(video(), vi.fn())

    decode({ data: 'first' })

    expect(destroy).toHaveBeenCalled()
  })

  it('releases the camera when stopped', async () => {
    const scan = await startScanning(video(), vi.fn())

    scan?.stop()

    expect(destroy).toHaveBeenCalled()
  })
})

describe('The region the scanner reads is shown', () => {
  it('outlines the scan region on the viewfinder', async () => {
    await startScanning(video(), vi.fn())

    expect(options.highlightScanRegion).toBe(true)
  })

  it('removes the outline on the first read', async () => {
    const viewfinder = video()
    await startScanning(viewfinder, vi.fn())

    decode({ data: 'first' })

    expect(viewfinder.parentElement?.childElementCount).toBe(1)
  })

  it('removes the outline when stopped', async () => {
    const viewfinder = video()
    const scan = await startScanning(viewfinder, vi.fn())

    scan?.stop()

    expect(viewfinder.parentElement?.childElementCount).toBe(1)
  })

  it('removes the outline when there turns out to be no live camera', async () => {
    start.mockRejectedValue(new DOMException('Permission denied', 'NotAllowedError'))
    const viewfinder = video()

    await startScanning(viewfinder, vi.fn())

    expect(viewfinder.parentElement?.childElementCount).toBe(1)
  })
})

describe('Live camera refused or unavailable', () => {
  it('reports no live camera when there is none', async () => {
    hasCamera.mockResolvedValue(false)

    await expect(startScanning(video(), vi.fn())).resolves.toBeNull()
    expect(start).not.toHaveBeenCalled()
  })

  it('reports no live camera when permission is refused, and releases what it opened', async () => {
    start.mockRejectedValue(new DOMException('Permission denied', 'NotAllowedError'))

    await expect(startScanning(video(), vi.fn())).resolves.toBeNull()
    expect(destroy).toHaveBeenCalled()
  })

  it('reports no live camera when the browser cannot stream one at all', async () => {
    start.mockRejectedValue(new Error('Camera not found.'))

    await expect(startScanning(video(), vi.fn())).resolves.toBeNull()
  })
})
