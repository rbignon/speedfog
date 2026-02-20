# Logarithmic Cluster Weight Aggregation

## Problem

Cluster weights are computed as the sum of zone weights. Multi-zone clusters
(Leyndell 7 zones = 43, Volcano Manor 7 zones = 40) have disproportionately
high weights compared to single-zone clusters (most at 10). This causes:

1. Balance validation failures when large clusters appear on one path but not
   parallel ones (spread 33 vs tolerance 5)
2. Combined with the excess-exits problem, these clusters are rarely selected

The `legacy_dungeon` default weight of 10 is also too high — it overestimates
traversal time in a fog randomizer context where players navigate between
fog gates, not explore entire areas.

## Design

### 1. Lower legacy_dungeon default: 10 → 5

Zone overrides scaled proportionally (ratio preserved). Fallback in
`load_metadata()` synchronized.

### 2. Logarithmic aggregation formula

Replace `sum(zone_weights)` with:

```
avg_zone_weight * (1 + 0.5 * ln(n_zones))
```

Properties:
- n=1: identity (no change)
- n=2: ~1.35x average
- n=7: ~1.97x average

Rationale: players traverse a path through the cluster, not all zones.
Additional zones have diminishing impact on traversal time.

### Result

Legacy dungeon weight spread: 10-43 (33 pts) → 5-11 (6 pts).
Compatible with balance tolerance of 5.

## Files changed

- `data/zone_metadata.toml` — defaults and overrides
- `tools/generate_clusters.py` — aggregation formula + fallback defaults
