import QrScanner from 'qr-scanner'

/** A running scan, released by `stop`. */
export interface Scan {
  stop: () => void
}

/**
 * Scans the rear camera, streaming into `video`, until the first QR code is read — which it hands
 * to `onRead` once and then releases the camera. Resolves to the running scan, or to `null` where
 * there is no live camera to scan: the browser offers none, the user refused it, or there is no
 * camera at all. The screen treats those alike, by offering a photograph instead (D40).
 *
 * The library behind this is the one swappable part: it uses the native `BarcodeDetector` where the
 * browser has one (Android Chrome) and its own worker elsewhere (iOS Safari). Nothing outside this
 * module knows which.
 */
export async function startScanning(
  video: HTMLVideoElement,
  onRead: (payload: string) => void,
): Promise<Scan | null> {
  if (!(await QrScanner.hasCamera())) {
    return null
  }

  let finished = false

  const scanner = new QrScanner(
    video,
    result => {
      // The library keeps decoding frames after a hit; only the first is a reading.
      if (finished) {
        return
      }

      finished = true
      release()
      onRead(result.data)
    },
    {
      preferredCamera: 'environment',
      returnDetailedScanResult: true,
      maxScansPerSecond: 15,
      // The library reads only a centred square of the frame, so a code held off-centre is never
      // read. Outlining that square is what tells the user where to hold it.
      highlightScanRegion: true,
    },
  )

  // Destroying the scanner hides its outline but leaves it in the document, beside a video a later
  // scan may reuse.
  const release = () => {
    scanner.destroy()
    scanner.$overlay?.remove()
  }

  try {
    await scanner.start()
  } catch {
    // Refused, unsupported or unavailable: all mean "no live camera", never an error to show.
    release()
    return null
  }

  return {
    stop: () => {
      finished = true
      release()
    },
  }
}
