using System.Collections.Generic;

namespace DeepwaterEngagementSuite.VoyagePlannerData;

public record VoyagePuzzle(
    List<MapPiece> AvailablePieces,
    IReadOnlyList<BorderEffect>[,] TileBorders,
    List<LockedPlacement> LockedPlacements,
    bool AllowSacrificeCornerBorderDeadEnds = false,
    bool OptimizeShortestPath = false);
