import { useQueryClient } from '@tanstack/react-query'
import { useEffect, useId, useRef, useState, type KeyboardEvent } from 'react'

import { LedgerError } from '../api/client'
import { queryKeys, usePurchaseCount } from '../api/queries'
import { clearLedger } from '../api/writes'

import styles from './ClearLedgerDialog.module.css'

interface Props {
  onClose: () => void
}

const COUNT_MISMATCH = 'purchase.clear.count_mismatch'

function messageOf(error: unknown): string {
  return error instanceof LedgerError ? error.message : 'The ledger could not be reached.'
}

function purchases(count: number): string {
  return count === 1 ? '1 purchase' : `${count} purchases`
}

/**
 * The one step between "Clear all" and an empty ledger. It reads the whole ledger's count when it
 * opens, because Home only knows the current month, and the clear is then told to expect exactly
 * that number: if the ledger moved in between, the ledger refuses, the new count is shown, and the
 * user is asked again (design D6). Focus starts on Cancel, the choice that loses nothing.
 */
export function ClearLedgerDialog({ onClose }: Props) {
  const client = useQueryClient()
  const count = usePurchaseCount()
  const [failure, setFailure] = useState<string | null>(null)
  const [isClearing, setIsClearing] = useState(false)
  const cancel = useRef<HTMLButtonElement>(null)
  const headingId = useId()
  const bodyId = useId()

  useEffect(() => {
    cancel.current?.focus()
  }, [])

  async function confirm(expected: number) {
    setFailure(null)
    setIsClearing(true)

    try {
      await clearLedger(expected)
    } catch (error) {
      setFailure(messageOf(error))

      if (error instanceof LedgerError && error.code === COUNT_MISMATCH) {
        await count.refetch()
      }

      return
    } finally {
      setIsClearing(false)
    }

    await Promise.all([
      client.invalidateQueries({ queryKey: queryKeys.allPurchases }),
      client.invalidateQueries({ queryKey: queryKeys.purchaseCount }),
    ])

    onClose()
  }

  function closeOnEscape(event: KeyboardEvent<HTMLDivElement>) {
    if (event.key === 'Escape' && !isClearing) {
      onClose()
    }
  }

  const total = count.data

  return (
    <div className={styles.backdrop}>
      <div
        role="alertdialog"
        aria-modal="true"
        aria-labelledby={headingId}
        aria-describedby={bodyId}
        className={styles.dialog}
        onKeyDown={closeOnEscape}
      >
        <h2 id={headingId} className={styles.title}>
          Clear all purchases?
        </h2>

        <div id={bodyId} className={styles.body}>
          {count.isPending ? <p role="status">Counting purchases…</p> : null}

          {count.isError ? <p role="alert">{messageOf(count.error)}</p> : null}

          {total === 0 ? <p>There is nothing to clear: no purchases have been recorded.</p> : null}

          {total !== undefined && total > 0 ? (
            <>
              <p>
                This deletes all {total} recorded {total === 1 ? 'purchase' : 'purchases'}, in every
                month, not only this one.
              </p>
              <p>Categories, units and merchants are kept.</p>
              <p className={styles.warning}>This cannot be undone.</p>
            </>
          ) : null}

          {failure === null ? null : (
            <p role="alert" className={styles.failure}>
              {failure}
            </p>
          )}
        </div>

        <div className={styles.actions}>
          <button
            ref={cancel}
            type="button"
            className={styles.cancel}
            disabled={isClearing}
            onClick={onClose}
          >
            Cancel
          </button>

          {total !== undefined && total > 0 && !count.isError ? (
            <button
              type="button"
              className={styles.confirm}
              disabled={isClearing || count.isFetching}
              onClick={() => void confirm(total)}
            >
              Delete {purchases(total)}
            </button>
          ) : null}
        </div>
      </div>
    </div>
  )
}
