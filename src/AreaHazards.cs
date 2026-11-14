using System.Collections.Generic;
using UnityEngine;

namespace LethalFakeClient;

internal sealed class AreaHazards : System.IDisposable
{
    private const float CellSize = 4f;
    private const float BeeRiskRadius = 5f;
    private readonly Dictionary<Vector3Int, List<Vector3>> indoor = new Dictionary<Vector3Int, List<Vector3>>();
    private readonly Dictionary<Vector3Int, List<Vector3>> outdoor = new Dictionary<Vector3Int, List<Vector3>>();
    private readonly Dictionary<Vector3Int, List<Vector3>> cadaver = new Dictionary<Vector3Int, List<Vector3>>();
    private readonly List<RedLocustBees> bees = new List<RedLocustBees>();
    private readonly List<BlobAI> slimes = new List<BlobAI>();
    private readonly List<CadaverBloomAI> blooms = new List<CadaverBloomAI>();
    private MoldSpreadManager? mold;
    private float nextDiscovery;
    private float nextRefresh;

    internal void Refresh(IEnumerable<EnemyAI> enemies, float now)
    {
        bees.Clear();
        slimes.Clear();
        blooms.Clear();
        foreach (var enemy in enemies)
        {
            if (enemy == null || enemy.isEnemyDead || !enemy.isActiveAndEnabled)
                continue;
            if (enemy is RedLocustBees swarm)
                bees.Add(swarm);
            else if (enemy is BlobAI slime)
                slimes.Add(slime);
            else if (enemy is CadaverBloomAI bloom)
                blooms.Add(bloom);
        }
        if (now < nextRefresh)
            return;
        nextRefresh = now + 2f;
        if (now >= nextDiscovery)
        {
            nextDiscovery = now + 10f;
            mold = UnityEngine.Object.FindObjectOfType<MoldSpreadManager>();
        }
        ClearCells(indoor);
        ClearCells(outdoor);
        ClearCells(cadaver);
        if (mold != null && mold.isActiveAndEnabled)
        {
            if (mold.grassInstancer != null && mold.grassInstancer.batchedPositions != null && mold.grassInstancer.batchedPositions.Count > 0)
                foreach (var point in mold.grassInstancer.batchedPositions)
                    Add(outdoor, point);
            else if (mold.generatedMold != null)
                foreach (var plant in mold.generatedMold)
                    if (plant != null && plant.activeInHierarchy)
                        Add(outdoor, plant.transform.position);
        }
        foreach (var enemy in enemies)
        {
            if (!(enemy is CadaverGrowthAI growth) || growth.isEnemyDead || !growth.isActiveAndEnabled)
                continue;
            bool hasPlants = false;
            if (growth.plantBatchers != null && growth.plantBatchers.Length > 0)
            {
                foreach (var batch in growth.plantBatchers)
                    if (batch != null && batch.batchedPositions != null)
                        foreach (var point in batch.batchedPositions)
                        {
                            Add(cadaver, point);
                            hasPlants = true;
                        }
            }
            if (!hasPlants && growth.GrowthTiles != null)
                foreach (var tile in growth.GrowthTiles)
                    if (tile != null && !tile.eradicated && tile.plantPositions != null)
                        foreach (var point in tile.plantPositions)
                            Add(cadaver, point);
        }
    }

    private static Vector3Int Cell(Vector3 point) => Vector3Int.FloorToInt(point / CellSize);

    private static void ClearCells(Dictionary<Vector3Int, List<Vector3>> cells)
    {
        if (cells.Count > 4096)
        {
            cells.Clear();
            return;
        }
        foreach (var list in cells.Values)
            list.Clear();
    }

    private static void Add(Dictionary<Vector3Int, List<Vector3>> cells, Vector3 point)
    {
        var cell = Cell(point);
        if (!cells.TryGetValue(cell, out var points))
        {
            points = new List<Vector3>();
            cells.Add(cell, points);
        }
        points.Add(point);
    }

    internal float Risk(Vector3 point, bool inside)
    {
        float risk = 0f;
        var cells = inside ? indoor : outdoor;
        risk = Mathf.Max(risk, BeeRisk(point, inside));
        risk = Mathf.Max(risk, RiskFromCells(point, cells));
        risk = Mathf.Max(risk, CadaverRisk(point, inside));
        foreach (var slime in slimes)
            if (slime != null && !slime.isEnemyDead && slime.isActiveAndEnabled && slime.isOutside != inside)
                risk = Mathf.Max(risk, Proximity(point, slime.centerPoint != null ? slime.centerPoint.position : slime.transform.position,
                    Mathf.Clamp(slime.currentSlimeRange + 2f, 4f, 10f)));
        foreach (var bloom in blooms)
            if (bloom != null && !bloom.isEnemyDead && bloom.isActiveAndEnabled && bloom.isOutside != inside)
                risk = Mathf.Max(risk, Proximity(point, bloom.transform.position, 4f));
        return risk;
    }

    internal float RiskWithoutCadaver(Vector3 point, bool inside)
    {
        float risk = 0f;
        var cells = inside ? indoor : outdoor;
        risk = Mathf.Max(risk, BeeRisk(point, inside));
        risk = Mathf.Max(risk, RiskFromCells(point, cells));
        foreach (var slime in slimes)
            if (slime != null && !slime.isEnemyDead && slime.isActiveAndEnabled && slime.isOutside != inside)
                risk = Mathf.Max(risk, Proximity(point, slime.centerPoint != null ? slime.centerPoint.position : slime.transform.position,
                    Mathf.Clamp(slime.currentSlimeRange + 2f, 4f, 10f)));
        return risk;
    }

    private float BeeRisk(Vector3 point, bool inside)
    {
        float risk = 0f;
        foreach (var swarm in bees)
            if (swarm != null && !swarm.isEnemyDead && swarm.isActiveAndEnabled && swarm.isOutside != inside)
                risk = Mathf.Max(risk, Proximity(point, swarm.transform.position, BeeRiskRadius));
        return risk;
    }

    internal float CadaverRisk(Vector3 point, bool inside)
    {
        float risk = RiskFromCells(point, cadaver);
        foreach (var bloom in blooms)
            if (bloom != null && !bloom.isEnemyDead && bloom.isActiveAndEnabled && bloom.isOutside != inside)
                risk = Mathf.Max(risk, Proximity(point, bloom.transform.position, 4f));
        return risk;
    }

    private static float RiskFromCells(Vector3 point, Dictionary<Vector3Int, List<Vector3>> cells)
    {
        float risk = 0f;
        var origin = Cell(point);
        for (int x = -1; x <= 1; x++)
            for (int y = -1; y <= 1; y++)
                for (int z = -1; z <= 1; z++)
                {
                    if (!cells.TryGetValue(new Vector3Int(origin.x + x, origin.y + y, origin.z + z), out var points))
                        continue;
                    foreach (var plant in points)
                        risk = Mathf.Max(risk, Proximity(point, plant, 4f));
                }
        return risk;
    }

    private static float Proximity(Vector3 point, Vector3 hazard, float radius)
    {
        Vector3 offset = point - hazard;
        float height = Mathf.Abs(offset.y);
        offset.y = 0f;
        return AreaHazardPolicy.Proximity(offset.magnitude, height, radius);
    }

    public void Dispose()
    {
        indoor.Clear();
        outdoor.Clear();
        cadaver.Clear();
        bees.Clear();
        slimes.Clear();
        blooms.Clear();
    }
}
