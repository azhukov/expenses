import { useQueryClient } from '@tanstack/react-query'
import { useCallback, useEffect, useRef, useState } from 'react'
import { useNavigate, useLocation } from 'react-router'

import { LedgerError } from '../api/client'
import type { CaptureResult } from '../api/types'
import { captureReceipt, recordPurchase } from '../api/writes'
import { confirmationOf, type Edits } from '../capture/review'
import { CaptureReview } from './CaptureReview'

function messageOf(error: unknown): string {
  return error instanceof LedgerError ? error.message : 'The receipt could not be uploaded.'
}

/**
 * The capture screen: it uploads the image it was handed the moment it arrives, waits out the
 * extraction the endpoint runs synchronously, and hands the result to review.
 *
 * The upload is guarded by a ref keyed on the file rather than by an effect dependency alone. A
 * `useEffect` runs twice under StrictMode in development and again on any re-render that changes
 * the dependency's identity, and each of those would be a second receipt uploaded for one
 * photograph — a duplicate the user never asked for and would only discover later.
 */
export function Capture() {
  const state = useLocation().state as { file?: File } | null
  const file = state?.file

  const [result, setResult] = useState<CaptureResult | null>(null)
  const [failure, setFailure] = useState<string | null>(null)
  const [isUploading, setIsUploading] = useState(false)
  const [rejection, setRejection] = useState<string | null>(null)
  const [isConfirming, setIsConfirming] = useState(false)

  const client = useQueryClient()
  const navigate = useNavigate()

  const uploaded = useRef<File | null>(null)

  const upload = useCallback(async (image: File) => {
    uploaded.current = image
    setFailure(null)
    setIsUploading(true)

    try {
      setResult(await captureReceipt(image))
    } catch (error) {
      setFailure(messageOf(error))
    } finally {
      setIsUploading(false)
    }
  }, [])

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
        setRejection(
          error instanceof LedgerError ? error.message : 'The purchase could not be recorded.',
        )

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

  useEffect(() => {
    if (!file || uploaded.current === file) {
      return
    }

    void upload(file)
  }, [file, upload])

  if (!file) {
    return (
      <main>
        <p>No photograph was handed over.</p>
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

  if (failure !== null) {
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
  return result === null ? null : (
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
