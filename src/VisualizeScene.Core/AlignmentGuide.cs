namespace VisualizeScene.Core;

/// <summary>A world-space alignment hint. Consumers choose presentation styling independently.</summary>
public sealed record AlignmentGuide(PointD Start, PointD End, string TargetId);
