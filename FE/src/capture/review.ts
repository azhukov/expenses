/**
 * What the review screen is about, apart from how it is drawn: the shapes the user edits, the
 * reasons a capture wants a human, and the request confirming one produces. Kept out of the
 * component so that each can be read — and tested — without rendering anything.
 */
import type { CaptureResult, ExtractionCandidateView } from '../api/types'
import type { RecordPurchaseRequest } from '../api/writes'

/**
 * One line as the user is editing it. Every numeric field is held as the string the user typed:
 * a half-typed "3." is not a number, and parsing on every keystroke would fight the typing. The
 * matched identifiers are resolved to codes here because that is how the API addresses them, while
 * `categoryRaw`/`unitRaw` travel through untouched — a match must never erase what was printed.
 */
export interface EditableLine {
  key: number
  description: string
  amount: string
  quantity: string
  categoryCode: string
  unitCode: string
  categoryRaw: string | null
  unitRaw: string | null
  unitPrice: number | null
  listUnitPrice: number | null
  discountAmount: number | null
}

export function asText(value: number | null | undefined): string {
  return value === null || value === undefined ? '' : String(value)
}

/**
 * The occurred-at date as a `datetime-local` input wants it. The API's timestamps are offset-less
 * wall-clock time (D12), so the value is trimmed rather than converted through a time zone.
 */
export function asLocalInput(timestamp: string | null): string {
  return timestamp === null ? '' : timestamp.replace(' ', 'T').slice(0, 16)
}

export function emptyLine(key: number): EditableLine {
  return {
    key,
    description: '',
    amount: '',
    quantity: '',
    categoryCode: '',
    unitCode: '',
    categoryRaw: null,
    unitRaw: null,
    unitPrice: null,
    listUnitPrice: null,
    discountAmount: null,
  }
}

export function lineOf(
  candidate: ExtractionCandidateView,
  codeOfCategory: (id: number | null) => string,
  codeOfUnit: (id: number | null) => string,
): EditableLine {
  return {
    key: candidate.lineNumber,
    description: candidate.description,
    amount: asText(candidate.amount),
    quantity: asText(candidate.quantity),
    categoryCode: codeOfCategory(candidate.categoryId),
    unitCode: codeOfUnit(candidate.unitId),
    categoryRaw: candidate.categoryRaw,
    unitRaw: candidate.unitRaw,
    unitPrice: candidate.unitPrice,
    listUnitPrice: candidate.listUnitPrice,
    discountAmount: candidate.discountAmount,
  }
}

/** The date the receipt itself established, where a fiscal code decoded one. */
export function fiscalCreatedAt(capture: CaptureResult): string | null {
  return capture.extracted.createdAt ?? capture.supplied.createdAt
}

/**
 * The values no arithmetic can decide, and so the only ones extraction's own reported confidence
 * is consulted for — everything numeric is proved by the arithmetic checks instead. The keys and
 * the threshold are the API's (D20); a value it did not consider low is not flagged here either.
 */
const UNVERIFIABLE: Record<string, string> = {
  description: 'the description',
  merchant_name: 'the merchant name',
  category_guess: 'the category',
  unit_guess: 'the unit',
}

const CONFIDENCE_THRESHOLD = 0.7

/** The names of the values a run reported low confidence in, in the words a reader knows them by. */
export function lowConfidence(reported: Record<string, number> | undefined): string[] {
  return Object.entries(reported ?? {})
    .filter(([value, confidence]) => value in UNVERIFIABLE && confidence < CONFIDENCE_THRESHOLD)
    .map(([value]) => UNVERIFIABLE[value])
}

/**
 * Why this capture wants a human, in the response's own words. Three independent reasons, each
 * stated as itself: a generic "please check this" is exactly what the API took care not to report.
 */
export function reviewReasons(capture: CaptureResult): string[] {
  const reasons: string[] = []

  for (const check of capture.validation?.checks ?? []) {
    if (check.outcome === 'Failed') {
      reasons.push(check.description)
    }
  }

  const unsure = [
    ...lowConfidence(capture.result?.reportedConfidence),
    ...(capture.result?.candidates ?? []).flatMap(each => lowConfidence(each.reportedConfidence)),
  ]

  for (const value of new Set(unsure)) {
    reasons.push(`Extraction was unsure of ${value}.`)
  }

  // There is no disagreement to report any more. A payload supplied at capture is preferred
  // outright and the image is never read for a second opinion, so the two readings that used to be
  // compared here are now one reading (D31).
  return reasons
}

/**
 * The longest description the ledger stores — `Expense`'s own limit, restated here so the user
 * hears it on the field rather than as a rejection after a round trip. The duplication is
 * deliberate (D2): if the two ever disagree, the ledger's refusal still reaches the screen.
 */
const DESCRIPTION_MAX_LENGTH = 200

/** The fields of a line the screen can refuse, and what is wrong with each. */
export type LineErrors = Partial<Record<'description' | 'amount' | 'quantity' | 'unitCode', string>>

/**
 * Everything wrong with what the user has entered, all of it at once: a first fault is no reason
 * to stop looking, since the user would then correct one value per attempt.
 */
export interface Errors {
  amount?: string
  occurredAt?: string
  /** The purchase has no lines at all, which is not a fault of any one input. */
  lines?: string
  byLine: Map<number, LineErrors>
}

/**
 * A number as the user typed it. Absence, nonsense and a negative are each refused; zero is not,
 * because a line can legitimately come to nothing and the ledger accepts it.
 */
function numberProblem(value: string, name: string): string | undefined {
  if (value.trim() === '') {
    return `${name} is required.`
  }

  const parsed = Number(value)

  if (!Number.isFinite(parsed)) {
    return `${name} must be a number.`
  }

  return parsed < 0 ? `${name} cannot be negative.` : undefined
}

function lineProblems(line: EditableLine): LineErrors {
  const errors: LineErrors = {}
  const description = line.description.trim()

  if (description === '') {
    errors.description = 'A description is required.'
  } else if (description.length > DESCRIPTION_MAX_LENGTH) {
    errors.description = `A description is at most ${DESCRIPTION_MAX_LENGTH} characters.`
  }

  const amount = numberProblem(line.amount, 'An amount')
  if (amount !== undefined) {
    errors.amount = amount
  }

  const quantity = numberProblem(line.quantity, 'A quantity')
  if (quantity !== undefined) {
    errors.quantity = quantity
  }

  if (line.unitCode === '') {
    errors.unitCode = 'A unit is required.'
  }

  return errors
}

/**
 * What the review screen refuses to submit, and why. `fiscalDate` is the date the receipt itself
 * established, if any: where there is one and the user has not overridden it, confirmation carries
 * no date at all and the ledger resolves it, so an empty date input is not a fault.
 *
 * The rules the ledger enforces are restated here rather than shared with it (D2). The merchant
 * and a line's category are absent on purpose: both are optional, and nothing decides that twice.
 */
export function validate(edits: Edits, fiscalDate: string | null = null): Errors {
  const errors: Errors = { byLine: new Map() }

  const amount = numberProblem(edits.amount, 'A total amount')
  if (amount !== undefined) {
    errors.amount = amount
  }

  if (edits.occurredAt.trim() === '' && (edits.dateEdited || fiscalDate === null)) {
    errors.occurredAt = 'A date is required.'
  }

  if (edits.lines.length === 0) {
    errors.lines = 'At least one expense line is required.'
  }

  for (const line of edits.lines) {
    const problems = lineProblems(line)

    if (Object.keys(problems).length > 0) {
      errors.byLine.set(line.key, problems)
    }
  }

  return errors
}

/** Whether anything at all is wrong — what the screen asks before submitting. */
export function isValid(errors: Errors): boolean {
  return (
    errors.amount === undefined &&
    errors.occurredAt === undefined &&
    errors.lines === undefined &&
    errors.byLine.size === 0
  )
}

/** What the user is asserting about the receipt, as against what extraction proposed. */
export interface Edits {
  lines: EditableLine[]
  amount: string
  merchant: string
  occurredAt: string
  /** False while the date is still the one the receipt established, which the API resolves itself. */
  dateEdited: boolean
}

function asNumber(value: string): number | null {
  if (value.trim() === '') {
    return null
  }

  const parsed = Number(value)

  // Left to the API to refuse: rejecting it here would be a second place that decides what a
  // purchase may contain, and the ledger's own message is the better one for the user.
  return Number.isNaN(parsed) ? null : parsed
}

/**
 * The confirmation request: the user's values for everything they can edit, and the capture's own
 * outcome echoed back exactly as it arrived. The echo is taken from the capture, never rebuilt
 * from the edits — the server holds nothing to correct it against.
 *
 * `occurredAt` is omitted where the receipt established a date the user left alone, which is the
 * one case the API can resolve a date by itself.
 */
export function confirmationOf(capture: CaptureResult, edits: Edits): RecordPurchaseRequest {
  const fiscalDate = fiscalCreatedAt(capture)

  return {
    amount: asNumber(edits.amount) ?? 0,
    merchant: edits.merchant.trim() === '' ? null : { text: edits.merchant },
    ...(edits.dateEdited || fiscalDate === null ? { occurredAt: edits.occurredAt } : {}),
    expenses: edits.lines.map(line => ({
      description: line.description,
      amount: asNumber(line.amount) ?? 0,
      quantity: asNumber(line.quantity),
      unitCode: line.unitCode === '' ? null : line.unitCode,
      unitPrice: line.unitPrice,
      categoryCode: line.categoryCode === '' ? null : line.categoryCode,
      categoryRaw: line.categoryRaw,
      unitRaw: line.unitRaw,
      listUnitPrice: line.listUnitPrice,
      discountAmount: line.discountAmount,
    })),
    capture: {
      tempKey: capture.tempKey,
      state: capture.state,
      failureReason: capture.failureReason,
      // Three members where there were six: the payload states the invoice code, the issuer tax
      // number, the creation timestamp and the total, so the client hands back the string it was
      // given rather than fields parsed out of it (D30, D32).
      jikr: capture.extracted.jikr ?? capture.supplied.jikr,
      fiscalSource: capture.fiscalSource,
      fiscalPayload: capture.fiscalPayload,
    },
  }
}
