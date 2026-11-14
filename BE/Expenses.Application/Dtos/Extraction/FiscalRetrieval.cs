namespace Expenses.Application.Dtos;

/// <summary>
/// What asking the verification service produced: the invoice when there was one, and in every case
/// the reason for the outcome. A pair rather than a nullable result, because a bare null can't say
/// which of the service's failures happened (D43).
/// </summary>
public sealed record FiscalRetrieval(ExtractionStepResult? Invoice, FiscalDiagnosis Diagnosis);
