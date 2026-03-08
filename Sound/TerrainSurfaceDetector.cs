using UnityEngine;

// ─────────────────────────────────────────────────────────
// TerrainSurfaceDetector.cs — Detects surface type under a position
//
// Static utility. Raycasts down, reads terrain splat map,
// or falls back to SurfaceTag on non-terrain colliders.
// Used by FootstepSoundPlayer to pick the right clip set.
// ─────────────────────────────────────────────────────────

public static class TerrainSurfaceDetector
{
    private static readonly RaycastHit[] _hitBuffer = new RaycastHit[4];

    /// <summary>
    /// Detect the surface type at the given world position.
    /// Raycasts downward to find terrain or SurfaceTag.
    /// Returns the surface type string, or the database's default if nothing found.
    /// </summary>
    public static string GetSurfaceType(Vector3 worldPosition, SoundDatabase database, float rayDistance = 3f)
    {
        if (database == null) return "Grass";

        Ray ray = new Ray(worldPosition + Vector3.up * 0.5f, Vector3.down);
        int hitCount = Physics.RaycastNonAlloc(ray, _hitBuffer, rayDistance, ~0, QueryTriggerInteraction.Ignore);

        for (int i = 0; i < hitCount; i++)
        {
            var hit = _hitBuffer[i];
            if (hit.collider == null) continue;

            // Check for SurfaceTag first (overrides terrain)
            var surfaceTag = hit.collider.GetComponent<SurfaceTag>();
            if (surfaceTag != null)
                return surfaceTag.SurfaceType;

            // Check for terrain
            var terrain = hit.collider.GetComponent<Terrain>();
            if (terrain != null)
            {
                int dominantLayer = GetDominantTerrainLayer(terrain, hit.point);
                return database.GetSurfaceTypeForTerrainLayer(dominantLayer);
            }
        }

        return database.DefaultSurfaceType;
    }

    /// <summary>
    /// Get the dominant terrain layer index at a world position.
    /// Reads the terrain's alpha map (splat map) and returns the
    /// layer with the highest weight.
    /// </summary>
    public static int GetDominantTerrainLayer(Terrain terrain, Vector3 worldPosition)
    {
        if (terrain == null || terrain.terrainData == null) return 0;

        TerrainData terrainData = terrain.terrainData;
        Vector3 terrainPos = terrain.transform.position;

        // Convert world position to terrain-local normalized coords (0-1)
        float normalizedX = (worldPosition.x - terrainPos.x) / terrainData.size.x;
        float normalizedZ = (worldPosition.z - terrainPos.z) / terrainData.size.z;

        // Clamp to valid range
        normalizedX = Mathf.Clamp01(normalizedX);
        normalizedZ = Mathf.Clamp01(normalizedZ);

        // Convert to alpha map coordinates
        int mapX = Mathf.RoundToInt(normalizedX * (terrainData.alphamapWidth - 1));
        int mapZ = Mathf.RoundToInt(normalizedZ * (terrainData.alphamapHeight - 1));

        // Get the splat weights at this position
        // alphamaps[z, x, layer] — note the z,x order
        float[,,] alphamaps = terrainData.GetAlphamaps(mapX, mapZ, 1, 1);

        int layerCount = terrainData.alphamapLayers;
        int dominantIndex = 0;
        float maxWeight = 0f;

        for (int i = 0; i < layerCount; i++)
        {
            float weight = alphamaps[0, 0, i];
            if (weight > maxWeight)
            {
                maxWeight = weight;
                dominantIndex = i;
            }
        }

        return dominantIndex;
    }
}
