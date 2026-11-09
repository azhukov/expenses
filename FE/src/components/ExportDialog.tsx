import { useEffect, useId, useRef, useState, type KeyboardEvent } from 'react'

import { LedgerError } from '../api/client'
import { exportPurchases } from '../api/reads'
import { formatMonth, type CalendarMonth } from '../format/format'
import {
  currentMonth,
  exportFileName,
  exportRange,
  nextMonth,
  previousMonth,
  type ExportChoice,
} from '../month/month'

import styles from './ExportDialog.module.css'

interface Props {
  onClose: () => void
  /** Where a file goes once exported. Defaults to a browser download; a seam for tests. */
  save?: (file: Blob, fileName: string) => void
  /** The moment the dialog takes as now, which fixes the default month. */
  now?: Date
}

type Kind = ExportChoice['kind']

const INVERTED = 'The end of the range falls before its start.'

function messageOf(error: unknown): string {
  return error instanceof LedgerError ? error.message : 'The ledger could not be reached.'
}

function ordinal({ year, month }: CalendarMonth): number {
  return year * 12 + month
}

/**
 * A download, not a navigation: the ledger lives on another origin, where `download` on a link to
 * it is ignored, and a failed navigation would show raw JSON rather than a message here. The URL is
 * revoked on the next tick, once the click has handed the bytes to the browser.
 */
function download(file: Blob, fileName: string): void {
  const url = URL.createObjectURL(file)
  const link = document.createElement('a')
  link.href = url
  link.download = fileName
  link.style.display = 'none'
  document.body.append(link)
  link.click()
  link.remove()
  setTimeout(() => URL.revokeObjectURL(url), 0)
}

/**
 * The step between "Export" and a saved CSV (design D6). It starts on the current month, which is
 * what is exported most, steps a month at a time but never into a month that has not begun, and
 * checks a range before sending it. A failure stays in the dialog with the choice intact, so the
 * user can retry or change it.
 */
export function ExportDialog({ onClose, save = download, now }: Props) {
  const [thisMonth] = useState(() => currentMonth(now))
  const [kind, setKind] = useState<Kind>('month')
  const [month, setMonth] = useState<CalendarMonth>(thisMonth)
  const [from, setFrom] = useState('')
  const [to, setTo] = useState('')
  const [failure, setFailure] = useState<string | null>(null)
  const [isExporting, setIsExporting] = useState(false)
  const confirmButton = useRef<HTMLButtonElement>(null)
  const headingId = useId()
  const groupId = useId()
  const rangeErrorId = useId()

  useEffect(() => {
    confirmButton.current?.focus()
  }, [])

  const isRangeComplete = from !== '' && to !== ''
  // `yyyy-MM-dd` sorts as it reads, so the strings compare as the dates do.
  const isRangeInverted = isRangeComplete && to < from
  const isAtThisMonth = ordinal(month) >= ordinal(thisMonth)
  const canExport = !isExporting && (kind !== 'range' || (isRangeComplete && !isRangeInverted))

  function choice(): ExportChoice {
    switch (kind) {
      case 'all':
        return { kind: 'all' }
      case 'month':
        return { kind: 'month', month }
      case 'range':
        return { kind: 'range', from, to }
    }
  }

  async function confirm() {
    if (!canExport) {
      return
    }

    const chosen = choice()
    setFailure(null)
    setIsExporting(true)
    let file: Blob

    try {
      file = await exportPurchases(exportRange(chosen))
    } catch (error) {
      setFailure(messageOf(error))
      return
    } finally {
      setIsExporting(false)
    }

    save(file, exportFileName(chosen))
    onClose()
  }

  function closeOnEscape(event: KeyboardEvent<HTMLDivElement>) {
    if (event.key === 'Escape' && !isExporting) {
      onClose()
    }
  }

  return (
    <div className={styles.backdrop}>
      <div
        role="dialog"
        aria-modal="true"
        aria-labelledby={headingId}
        className={styles.dialog}
        onKeyDown={closeOnEscape}
      >
        <h2 id={headingId} className={styles.title}>
          Export expenses
        </h2>

        <fieldset className={styles.choices} aria-labelledby={groupId} disabled={isExporting}>
          <legend id={groupId} className={styles.legend}>
            What to export, as a CSV file with one row per line
          </legend>

          <label className={styles.choice}>
            <input
              type="radio"
              name="export-kind"
              checked={kind === 'month'}
              onChange={() => setKind('month')}
            />
            Month
          </label>

          {kind === 'month' ? (
            <div className={styles.stepper}>
              <button
                type="button"
                className={styles.step}
                aria-label="Previous month"
                onClick={() => setMonth(previousMonth(month))}
              >
                ‹
              </button>
              <span className={styles.month}>{formatMonth(month)}</span>
              <button
                type="button"
                className={styles.step}
                aria-label="Next month"
                disabled={isAtThisMonth}
                onClick={() => setMonth(nextMonth(month))}
              >
                ›
              </button>
            </div>
          ) : null}

          <label className={styles.choice}>
            <input
              type="radio"
              name="export-kind"
              checked={kind === 'range'}
              onChange={() => setKind('range')}
            />
            Date range
          </label>

          {kind === 'range' ? (
            <div className={styles.range}>
              <label className={styles.date}>
                From
                <input
                  type="date"
                  value={from}
                  aria-invalid={isRangeInverted}
                  aria-describedby={isRangeInverted ? rangeErrorId : undefined}
                  onChange={event => setFrom(event.target.value)}
                />
              </label>
              <label className={styles.date}>
                To
                <input
                  type="date"
                  value={to}
                  aria-invalid={isRangeInverted}
                  aria-describedby={isRangeInverted ? rangeErrorId : undefined}
                  onChange={event => setTo(event.target.value)}
                />
              </label>
              {isRangeInverted ? (
                <p id={rangeErrorId} className={styles.failure}>
                  {INVERTED}
                </p>
              ) : null}
            </div>
          ) : null}

          <label className={styles.choice}>
            <input
              type="radio"
              name="export-kind"
              checked={kind === 'all'}
              onChange={() => setKind('all')}
            />
            Everything
          </label>
        </fieldset>

        {isExporting ? <p role="status">Exporting…</p> : null}

        {failure === null ? null : (
          <p role="alert" className={styles.failure}>
            {failure}
          </p>
        )}

        <div className={styles.actions}>
          <button type="button" className={styles.cancel} disabled={isExporting} onClick={onClose}>
            Cancel
          </button>
          <button
            ref={confirmButton}
            type="button"
            className={styles.confirm}
            disabled={!canExport}
            onClick={() => void confirm()}
          >
            Export
          </button>
        </div>
      </div>
    </div>
  )
}
