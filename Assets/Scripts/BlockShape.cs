using UnityEngine;

[CreateAssetMenu(fileName = "NewBlockShape", menuName = "TowerDefense/BlockShape")]
public class BlockShape : ScriptableObject
{
    public string shapeName;

    [Tooltip("Budget cost to place this shape. O is the only fully solid 2x2 shape (guaranteed gapless barrier) so it costs a premium; S/Z/J/L bend into tight coils efficiently so they're cheap; I and T are generalists.")]
    public int buildCost = 4;

    [Header("Wall Material")]
    [Tooltip("Pathfinding weight of one wall tile from this shape. Roughly: 'digging through this tile is as bad as a detour of this many open tiles'. Higher = enemies almost always route around instead of chewing.")]
    public int digCost = 15;

    [Tooltip("Seconds a single enemy must chew to destroy one tile of this wall. Several enemies on the same tile chew in parallel.")]
    public float wallHealth = 6f;

    public Color shapeColor = Color.white;

    // Tile offsets relative to the click/origin point (0,0)
    public Vector2Int[] localTiles;

    // Rotates the shape 90 degrees clockwise by remapping every tile offset:
    // (x, y) -> (y, -x). Mutates this instance, which is why BlockManager only
    // ever hands out runtime clones of the library assets.
    public void Rotate()
    {
        for (int i = 0; i < localTiles.Length; i++)
        {
            int oldX = localTiles[i].x;
            int oldY = localTiles[i].y;
            localTiles[i] = new Vector2Int(oldY, -oldX);
        }
    }
}
