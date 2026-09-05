using System;
using System.Collections.Generic;
using VRPG.Worlds.Generation.Onboarding;

namespace VRPG.Realms.Onboarding;

/// <summary>Persist with the realm manifest; physical mapping is deliberately not part of identity.</summary>
public sealed record TavernInstanceIdentity(string RealmId, string OwnerUid, long Seed,
    int GeneratorVersion = TavernLayout.GeneratorVersion,
    string DefinitionCode = TavernLayout.DefinitionCode)
{
    public string ArrivalId => RealmId + "/arrival";
    public string RecoveryId => RealmId + "/recovery";
}

/// <summary>
/// Exclusive, unoccupied generation reservation supplied by Realm Runtime and World Mutation.
/// Implementations must keep the reservation until commit or quarantine, suppress all forbidden
/// activities through OnboardingInstancePolicy, and report authoritative loaded-world state.
/// </summary>
public interface ITavernGenerationReservation
{
    TavernInstanceIdentity Identity { get; }
    bool IsExclusiveAndEmpty { get; }
    bool IsLoaded { get; }
    bool IsPolicyEnforced { get; }
    void Write(TavernCell cell, TavernBlock block);
    TavernBlock Read(int x, int y, int z);
    void Flush();
    void CommitValidated(TavernAnchor arrival, TavernAnchor recovery);
    void Quarantine(string reason);
}

public sealed record TavernPreparationResult(bool Ready, string Failure,
    IReadOnlyList<TavernValidationIssue> Issues);

/// <summary>Prepares only reserved private space. Never allocates, teleports, releases protection or grants items.</summary>
public static class TavernInstancePreparation
{
    public static TavernPreparationResult Prepare(ITavernGenerationReservation reservation)
    {
        ArgumentNullException.ThrowIfNull(reservation);
        TavernInstanceIdentity identity = reservation.Identity;
        if (string.IsNullOrWhiteSpace(identity.RealmId) || string.IsNullOrWhiteSpace(identity.OwnerUid)
            || identity.GeneratorVersion != TavernLayout.GeneratorVersion
            || identity.DefinitionCode != TavernLayout.DefinitionCode)
            return Reject(reservation, "unsupported-identity");

        try
        {
            if (!CanPrepare(reservation)) return Reject(reservation, "reservation-not-safe");
            TavernLayout layout = TavernGenerator.Generate(identity.Seed);
            var issues = TavernStructuralValidator.Validate(layout);
            if (issues.Count != 0) return Reject(reservation, "invalid-layout", issues);

            // Include air: replay after a partial write must clear stale interior blocks too.
            foreach (TavernCell cell in layout.Cells())
                reservation.Write(cell, layout.GetBlock(cell.X, cell.Y, cell.Z));
            reservation.Flush();
            if (!CanPrepare(reservation)) return Reject(reservation, "reservation-lost");

            issues = TavernStructuralValidator.Validate(layout, reservation.Read);
            if (issues.Count != 0) return Reject(reservation, "invalid-world", issues);
            foreach (TavernCell cell in layout.Cells())
                if (reservation.Read(cell.X, cell.Y, cell.Z) != layout.GetBlock(cell.X, cell.Y, cell.Z))
                    return Reject(reservation, "world-mismatch");

            if (!CanPrepare(reservation)) return Reject(reservation, "reservation-lost");
            reservation.CommitValidated(layout.GetAnchor("arrival"), layout.GetAnchor("recovery"));
            return new(true, "", Array.Empty<TavernValidationIssue>());
        }
        catch (Exception exception)
        {
            // Partial writes remain isolated. Recovery belongs to the realm owner, never a public spawn fallback.
            return Reject(reservation, "generation-failed:" + exception.GetType().Name);
        }
    }

    private static bool CanPrepare(ITavernGenerationReservation reservation) =>
        reservation.IsExclusiveAndEmpty && reservation.IsLoaded && reservation.IsPolicyEnforced;

    private static TavernPreparationResult Reject(ITavernGenerationReservation reservation, string reason,
        IReadOnlyList<TavernValidationIssue>? issues = null)
    {
        reservation.Quarantine(reason);
        return new(false, reason, issues ?? Array.Empty<TavernValidationIssue>());
    }
}
