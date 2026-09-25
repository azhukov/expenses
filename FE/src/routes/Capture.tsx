import { useQueryClient } from '@tanstack/react-query'
import { type ChangeEvent, useCallback, useEffect, useRef, useState } from 'react'
import { useNavigate } from 'react-router'

import { LedgerError } from '../api/client'
import type { CaptureResult } from '../api/types'
import {
  captureFiscal,
  captureReceipt,
  recordPurchase,
  type SuppliedFiscalPayload,
} from '../api/writes'
import { confirmationOf, type Edits } from '../capture/review'
import { startScanning } from '../capture/scanner'
import styles from './Capture.module.css'
import { CaptureReview } from './CaptureReview'

/** How long scanning runs before the screen suggests a photograph instead. A nudge, not a limit. */
const HINT_AFTER_MS = 10_000

function messageOf(error: unknown, fallback: string): string {
  return error instanceof LedgerError ? error.message : fallback
}

/**
 * The capture screen, QR first (D40). It scans the rear camera for the receipt's fiscal code and
 * sends the first code it reads on its own; the invoice the ledger fetches for it is the whole
 * capture. A photograph is asked for only when that does not produce an invoice — nothing was read,
 * there is no live camera, or the ledger fetched nothing — and then carries the payload with it
 * where the payload was a fiscal code at all.
 *
 * The photograph upload is guarded by a ref keyed on the file, so a re-render never uploads one
 * photograph twice; the scanner is guarded by its effect's own cleanup, which releases the camera
 * that effect started, so StrictMode's second mount leaves exactly one scanner live.
 */
export function Capture() {
  const video = useRef<HTMLVideoElement>(null)

  const [isScanning, setIsScanning] = useState(true)
  const [hint, setHint] = useState(false)

  /** The payload read, while the ledger is fetching its invoice or after it could not be reached. */
  const [read, setRead] = useState<SuppliedFiscalPayload | null>(null)
  const [isFetching, setIsFetching] = useState(false)
  const [notice, setNotice] = useState<string | null>(null)
  const [unreachable, setUnreachable] = useState(false)

  /** What travels with the photograph: a payload that was a fiscal code but fetched no invoice. */
  const [carried, setCarried] = useState<SuppliedFiscalPayload | undefined>(undefined)

  const [file, setFile] = useState<File | null>(null)
  const [isUploading, setIsUploading] = useState(false)
  const [failure, setFailure] = useState<string | null>(null)

  const [result, setResult] = useState<CaptureResult | null>(null)
  const [rejection, setRejection] = useState<string | null>(null)
  const [isConfirming, setIsConfirming] = useState(false)

  const client = useQueryClient()
  const navigate = useNavigate()

  const uploaded = useRef<File | null>(null)

  const fetchInvoice = useCallback(async (payload: SuppliedFiscalPayload) => {
    setRead(payload)
    setNotice(null)
    setUnreachable(false)
    setIsFetching(true)

    try {
      const fetched = await captureFiscal(payload)

      if (fetched.state !== 'Failed') {
        setResult(fetched)
        return
      }

      // A code that yielded identifiers goes with the photograph, so the ledger can still ask the
      // service about it; one that yielded none — a menu, a loyalty card — must not, or it would
      // stop the ledger decoding the receipt's real code from the photograph (D31).
      const wasFiscal = fetched.supplied.ikof !== null || fetched.supplied.issuerTaxNumber !== null
      setCarried(wasFiscal ? payload : undefined)
      setNotice('We could not fetch the invoice for this receipt. Photograph the receipt instead.')
    } catch (error) {
      setUnreachable(true)
      setNotice(messageOf(error, 'The invoice could not be fetched.'))
    } finally {
      setIsFetching(false)
    }
  }, [])

  useEffect(() => {
    if (!isScanning || !video.current) {
      return
    }

    let cancelled = false
    let stop: (() => void) | null = null
    const hintTimer = window.setTimeout(() => setHint(true), HINT_AFTER_MS)

    void startScanning(video.current, payload => {
      setIsScanning(false)
      void fetchInvoice(payload)
    }).then(scan => {
      if (cancelled) {
        scan?.stop()
        return
      }

      if (scan === null) {
        // No live camera: the photograph control is the whole screen, and nothing is wrong.
        setIsScanning(false)
        return
      }

      stop = scan.stop
    })

    return () => {
      cancelled = true
      window.clearTimeout(hintTimer)
      stop?.()
    }
  }, [isScanning, fetchInvoice])

  const upload = useCallback(
    async (image: File) => {
      uploaded.current = image
      setFailure(null)
      setIsUploading(true)

      try {
        setResult(await captureReceipt(image, carried))
      } catch (error) {
        setFailure(messageOf(error, 'The receipt could not be uploaded.'))
      } finally {
        setIsUploading(false)
      }
    },
    [carried],
  )

  function onPhotographed(event: ChangeEvent<HTMLInputElement>) {
    const taken = event.target.files?.[0]

    // Dismissing the camera without taking a photograph leaves the screen as it was.
    if (!taken || uploaded.current === taken) {
      return
    }

    setIsScanning(false)
    setFile(taken)
    void upload(taken)
  }

  /**
   * A rejected confirmation leaves everything the user entered where it is, and the capture with
   * it: the temporary image is still there to confirm once the reason is corrected, and re-entering
   * a receipt because the total was a cent out is not a thing to ask of anyone.
   */
  const confirm = useCallback(
    async (captured: CaptureResult, edits: Edits) => {
      setRejection(null)
      setIsConfirming(true)

      try {
        await recordPurchase(confirmationOf(captured, edits))
      } catch (error) {
        setRejection(messageOf(error, 'The purchase could not be recorded.'))

        return
      } finally {
        setIsConfirming(false)
      }

      // Invalidated rather than patched by hand: Home derives the month's total, its review count
      // and its recent few from this one query, and a hand-rolled insert would have to reproduce
      // all three (D5).
      await client.invalidateQueries({ queryKey: ['purchases'] })
      void navigate('/')
    },
    [client, navigate],
  )

  if (isFetching) {
    return (
      <main>
        <p role="status">Fetching the invoice…</p>
      </main>
    )
  }

  if (isUploading) {
    return (
      <main>
        <p role="status">Reading the receipt…</p>
      </main>
    )
  }

  if (failure !== null && file !== null) {
    return (
      <main>
        <p role="alert">{failure}</p>
        <button type="button" onClick={() => void upload(file)}>
          Try again
        </button>
      </main>
    )
  }

  // The result is held here, not re-fetched: the server keeps nothing about a capture once it has
  // answered, so this response is the only copy of the extraction outcome that exists.
  if (result !== null) {
    return (
      <main>
        <CaptureReview
          capture={result}
          onConfirm={edits => void confirm(result, edits)}
          failure={rejection}
          isConfirming={isConfirming}
        />
      </main>
    )
  }

  return (
    <main className={styles.screen}>
      {isScanning ? (
        <>
          <video ref={video} className={styles.viewfinder} muted playsInline />
          <p className={styles.guidance}>Point the camera at the QR code on the receipt.</p>
          {hint ? <p className={styles.guidance}>No code yet? Try a photograph instead.</p> : null}
        </>
      ) : null}

      {notice !== null ? (
        <p role={unreachable ? 'alert' : undefined} className={styles.notice}>
          {notice}
        </p>
      ) : null}

      {unreachable && read !== null ? (
        <button type="button" onClick={() => void fetchInvoice(read)}>
          Try again
        </button>
      ) : null}

      {/*
       * A real file input the user's finger lands on, wrapped in a label. There is deliberately no
       * programmatic `click()` and nothing between the tap and the browser's own handling of it:
       * iOS Safari opens the camera only for the interaction that asked for it (D3). The `File` is
       * handed on untouched — no resize, no re-encode — because a dense fiscal code on thermal
       * paper does not survive downscaling (D4).
       */}
      <label className={styles.control}>
        <input
          className={styles.input}
          type="file"
          accept="image/*"
          capture="environment"
          onChange={onPhotographed}
        />
        <span className={styles.caption}>Photograph the receipt</span>
      </label>
    </main>
  )
}
