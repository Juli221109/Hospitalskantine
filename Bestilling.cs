namespace Hospitalskantine;

public sealed record Bestilling(
    string PatientId,
    string Date,
    int? BreakfastChoice,
    int? LunchChoice,
    int? DinnerChoice,
    string? BestillingsReference = null,
    string? Betalingsmetode = null);
