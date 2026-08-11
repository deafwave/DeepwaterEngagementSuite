using System.Collections.Generic;
using System.Linq;
using DeepwaterEngagementSuite.VoyagePlannerData;

namespace DeepwaterEngagementSuite;

public sealed class VoyageSolve
{
    public VoyageScorer Scorer { get; private set; }
    public VoyagePlacementRules.Result Placement { get; private set; }
    public VoyagePuzzle Puzzle { get; private set; }

    
    public int DroppedLockCount => DroppedLocks.Count;

    
    public List<string> DroppedLocks { get; } = [];

    public void Cancel()
    {
        
    }

    public IEnumerable<VoyageSolutionResult> Run(
        List<MapPiece> pieces,
        IReadOnlyList<BorderEffect>[,] tileBorders,
        VoyagePlannerSettings settings = null,
        VoyageStrategyOptions strategyOptions = null)
    {
        settings ??= new VoyagePlannerSettings();
        DroppedLocks.Clear();

        Placement = VoyagePlacementRules.Apply(pieces, tileBorders, strategyOptions);
        var locks = Placement.Locks.ToList();

        while (true)
        {
            Puzzle = new VoyagePuzzle(
                Placement.Pieces,
                tileBorders,
                locks,
                OptimizeShortestPath: strategyOptions?.ShortestPath == true);
            Scorer = new VoyageScorer(Puzzle);

            VoyageSolutionResult last = null;
            foreach (var result in new VoyagePlannerFast().Solve(Puzzle, settings))
            {
                last = result;
                yield return result;
            }

            if (last is { Solutions.Count: > 0 })
                yield break;

            if (locks.Count == 0)
                yield break;

            
            var dropIdx = IndexOfLockToDrop(locks);
            var dropped = locks[dropIdx];
            locks.RemoveAt(dropIdx);
            DroppedLocks.Add(FormatDroppedLock(dropped, pieces));
            Placement = Placement with { Locks = locks.ToList() };
        }
    }

    private static int IndexOfLockToDrop(IReadOnlyList<LockedPlacement> locks)
    {
        var bestIdx = locks.Count - 1;
        var bestPriority = locks[bestIdx].Priority;
        for (var i = locks.Count - 2; i >= 0; i--)
        {
            if (locks[i].Priority < bestPriority)
            {
                bestPriority = locks[i].Priority;
                bestIdx = i;
            }
        }

        return bestIdx;
    }

    private static string FormatDroppedLock(LockedPlacement lp, IReadOnlyList<MapPiece> pieces)
    {
        var piece = pieces?.FirstOrDefault(p => p.Id == lp.PieceId);
        var room = string.IsNullOrWhiteSpace(piece?.Name) ? null : piece.Name;
        var strategy = string.IsNullOrWhiteSpace(lp.Strategy) ? "lock" : lp.Strategy;
        var pieceBit = room != null ? $"{room} #{lp.PieceId}" : $"piece #{lp.PieceId}";
        return $"{strategy} @ ({lp.Row},{lp.Col}) [{pieceBit}]";
    }
}
