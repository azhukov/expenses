import { useRef, useState } from 'react'

import { useCategories, useUnits } from '../api/queries'
import type { CaptureResult } from '../api/types'
import { describeWhen } from '../format/format'
import {
  asLocalInput,
  asText,
  type EditableLine,
  type Edits,
  emptyLine,
  type Errors,
  fiscalCreatedAt,
  isValid,
  lineOf,
  lowConfidence,
  reviewReasons,
  validate,
} from '../capture/review'

import styles from './CaptureReview.module.css'

const NOTHING_MARKED: Errors = { byLine: new Map() }

/**
 * What an input needs in order to be marked: the invalid state and the message, announced together
 * rather than shown only in ink. The id is derived from the field's own name so the message keeps
 * the same identity across renders.
 */
function marked(id: string, message: string | undefined) {
  return {
    'aria-invalid': message !== undefined,
    'aria-describedby': message === undefined ? undefined : id,
  }
}

/**
 * What is wrong with one input, beside the input. Rendered even when there is nothing to say, so
 * that marking a line does not push the lines below it down the screen.
 */
function Message({ id, text }: { id: string; text: string | undefined }) {
  return (
    <span className={styles.message} id={id} data-testid={text === undefined ? undefined : id}>
      {text ?? ' '}
    </span>
  )
}

export interface ReviewProps {
  capture: CaptureResult
  onConfirm: (edits: Edits) => void
  failure: string | null
  isConfirming: boolean
}

/**
 * The review screen. It holds the capture result untouched beside the user's edits and never
 * re-derives one from the other: the server keeps nothing about a capture after answering, so the
 * echoed outcome has to be the response's own, whatever the user has since changed.
 */
export function CaptureReview({ capture, onConfirm, failure, isConfirming }: ReviewProps) {
  const categories = useCategories()
  const units = useUnits()

  const codeOfCategory = (id: number | null) =>
    categories.data?.find(category => category.id === id)?.code ?? ''
  const codeOfUnit = (id: number | null) => units.data?.find(unit => unit.id === id)?.code ?? ''

  const candidates = capture.result?.candidates ?? []

  // Seeded once: after the first render these are the user's values, and re-deriving them from the
  // capture would silently discard an edit.
  const [lines, setLines] = useState<EditableLine[]>(() =>
    candidates.length > 0
      ? candidates.map(each => lineOf(each, codeOfCategory, codeOfUnit))
      : [emptyLine(1)],
  )
  const [amount, setAmount] = useState(() => asText(capture.result?.total))
  const [merchant, setMerchant] = useState(() => capture.result?.merchantName ?? '')
  const [occurredAt, setOccurredAt] = useState(() => asLocalInput(fiscalCreatedAt(capture)))
  const [dateEdited, setDateEdited] = useState(false)

  const nextKey = useRef(candidates.length + 1)

  const reasons = reviewReasons(capture)

  // Per line as well as in the list of reasons: which line the unsure value is on is what the user
  // needs in order to correct it, and a reason on its own does not say.
  const unsureOn = new Map(
    candidates.map(each => [each.lineNumber, lowConfidence(each.reportedConfidence)]),
  )

  const hasAlert =
    capture.failureReason !== null ||
    failure !== null ||
    reasons.length > 0 ||
    capture.alreadyRecorded !== null

  // Marked only once the user has tried to confirm, and recomputed from the current values on
  // every render after that — which is what clears a mark as a value is corrected, with no second
  // mechanism and no state that can fall out of step with what is on the screen (D3).
  const [attempted, setAttempted] = useState(false)
  const edits: Edits = { lines, amount, merchant, occurredAt, dateEdited }
  const errors = validate(edits, fiscalCreatedAt(capture))
  const marks = attempted ? errors : NOTHING_MARKED

  function change(key: number, field: keyof EditableLine, value: string) {
    setLines(current =>
      current.map(line => (line.key === key ? { ...line, [field]: value } : line)),
    )
  }

  return (
    <form
      className={styles.page}
      noValidate
      onSubmit={event => {
        event.preventDefault()
        setAttempted(true)

        if (isValid(errors)) {
          onConfirm(edits)
        }
      }}
    >
      <header className={styles.header}>
        {/*
          Above everything it qualifies: which reading the user is correcting. Taken from the
          response's own engine name and nothing else — where extraction produced no result there
          is no name to infer, and saying so is better than a blank (D7).
        */}
        <p className={styles.engine} data-testid="engine">
          {capture.result === null
            ? 'No extraction engine reading available'
            : `Extracted by ${capture.result.engineName}`}
        </p>

        <label className={styles.field}>
          <span className={styles.caption}>Merchant</span>
          <input
            className={`${styles.input} ${styles.provider}`}
            value={merchant}
            placeholder="Unknown merchant"
            onChange={event => setMerchant(event.target.value)}
          />
        </label>

        <div className={styles.meta}>
          <div className={styles.field}>
            <label className={styles.control}>
              <span className={styles.caption}>Date</span>
              <input
                className={styles.input}
                type="datetime-local"
                value={occurredAt}
                onChange={event => {
                  setDateEdited(true)
                  setOccurredAt(event.target.value)
                }}
                {...marked('occurred-at-error', marks.occurredAt)}
              />
            </label>
            <Message id="occurred-at-error" text={marks.occurredAt} />
          </div>

          <div className={styles.field}>
            <label className={styles.control}>
              <span className={styles.caption}>Total amount</span>
              <input
                className={`${styles.input} ${styles.number}`}
                type="number"
                step="0.01"
                value={amount}
                onChange={event => setAmount(event.target.value)}
                {...marked('amount-error', marks.amount)}
              />
            </label>
            <Message id="amount-error" text={marks.amount} />
          </div>
        </div>
      </header>

      {hasAlert && (
        <div className={styles.alerts}>
          {capture.failureReason !== null && (
            <p className={styles.failure} role="alert">
              {capture.failureReason}
            </p>
          )}

          {failure !== null && (
            <p className={styles.failure} role="alert">
              {failure}
            </p>
          )}

          {/*
           * A warning, not a refusal: the same receipt can be scanned twice by mistake, but two
           * purchases can also share an invoice on purpose, so the user decides (D39).
           */}
          {capture.alreadyRecorded !== null && (
            <p className={styles.reasons} data-testid="already-recorded">
              This invoice appears to be recorded already, on a purchase from{' '}
              {describeWhen(capture.alreadyRecorded.occurredAt)}.
            </p>
          )}

          {reasons.length > 0 && (
            <ul className={styles.reasons} data-testid="review-reasons">
              {reasons.map(reason => (
                <li key={reason}>{reason}</li>
              ))}
            </ul>
          )}
        </div>
      )}

      {marks.lines !== undefined && (
        <p className={styles.failure} role="alert" data-testid="lines-error">
          {marks.lines}
        </p>
      )}

      <ul className={styles.lines}>
        {lines.map((line, index) => (
          <li className={styles.line} key={line.key} data-testid="line">
            <div className={styles.lineHead}>
              <span className={styles.ordinal}>Line {index + 1}</span>

              <button
                className={styles.remove}
                type="button"
                onClick={() => setLines(current => current.filter(each => each.key !== line.key))}
              >
                Remove
              </button>
            </div>

            {(unsureOn.get(line.key)?.length ?? 0) > 0 && (
              <p className={styles.unsure} data-testid="low-confidence">
                Extraction was unsure of {unsureOn.get(line.key)?.join(', ')}.
              </p>
            )}

            <div className={`${styles.field} ${styles.span}`}>
              <label className={styles.control}>
                <span className={styles.caption}>Description</span>
                <input
                  className={styles.input}
                  value={line.description}
                  onChange={event => change(line.key, 'description', event.target.value)}
                  {...marked(
                    `line-${line.key}-description-error`,
                    marks.byLine.get(line.key)?.description,
                  )}
                />
              </label>
              <Message
                id={`line-${line.key}-description-error`}
                text={marks.byLine.get(line.key)?.description}
              />
            </div>

            <div className={styles.field}>
              <label className={styles.control}>
                <span className={styles.caption}>Amount</span>
                <input
                  className={`${styles.input} ${styles.number}`}
                  type="number"
                  step="0.01"
                  value={line.amount}
                  onChange={event => change(line.key, 'amount', event.target.value)}
                  {...marked(`line-${line.key}-amount-error`, marks.byLine.get(line.key)?.amount)}
                />
              </label>
              <Message
                id={`line-${line.key}-amount-error`}
                text={marks.byLine.get(line.key)?.amount}
              />
            </div>

            <div className={styles.field}>
              <label className={styles.control}>
                <span className={styles.caption}>Quantity</span>
                <input
                  className={`${styles.input} ${styles.number}`}
                  type="number"
                  step="0.001"
                  value={line.quantity}
                  onChange={event => change(line.key, 'quantity', event.target.value)}
                  {...marked(
                    `line-${line.key}-quantity-error`,
                    marks.byLine.get(line.key)?.quantity,
                  )}
                />
              </label>
              <Message
                id={`line-${line.key}-quantity-error`}
                text={marks.byLine.get(line.key)?.quantity}
              />
            </div>

            <label className={styles.field}>
              <span className={styles.caption}>Category</span>
              <select
                className={styles.input}
                value={line.categoryCode}
                onChange={event => change(line.key, 'categoryCode', event.target.value)}
              >
                <option value="">Uncategorised</option>
                {(categories.data ?? []).map(category => (
                  <option key={category.id} value={category.code}>
                    {category.name}
                  </option>
                ))}
              </select>
            </label>

            <div className={styles.field}>
              <label className={styles.control}>
                <span className={styles.caption}>Unit</span>
                <select
                  className={styles.input}
                  value={line.unitCode}
                  onChange={event => change(line.key, 'unitCode', event.target.value)}
                  {...marked(`line-${line.key}-unit-error`, marks.byLine.get(line.key)?.unitCode)}
                >
                  <option value="">None</option>
                  {(units.data ?? []).map(unit => (
                    <option key={unit.id} value={unit.code}>
                      {unit.name}
                    </option>
                  ))}
                </select>
              </label>
              <Message
                id={`line-${line.key}-unit-error`}
                text={marks.byLine.get(line.key)?.unitCode}
              />
            </div>
          </li>
        ))}
      </ul>

      <div className={styles.dock}>
        <button
          className={styles.add}
          type="button"
          onClick={() => setLines(current => [...current, emptyLine(nextKey.current++)])}
        >
          Add a line
        </button>

        <button className={styles.confirm} type="submit" disabled={isConfirming}>
          Confirm
        </button>
      </div>
    </form>
  )
}
