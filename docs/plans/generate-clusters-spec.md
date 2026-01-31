# Spec : generate_clusters.py

**Objectif** : Générer un fichier `clusters.json` contenant tous les clusters de zones possibles à partir de `fog.txt`, avec leurs entry_fogs et exit_fogs pré-calculés.

**Motivation** : Le générateur de DAG peut ensuite simplement piocher dans les clusters selon le type, le poids, et le nombre d'entrées/sorties, sans avoir à reconstruire la logique des world connections.

---

## 1. Concepts clés

### 1.1 Cluster

Un **cluster** est un groupe de zones liées par des world connections (`To:` dans fog.txt). Une fois qu'un joueur entre dans le cluster par un `entry_fog`, il a accès à tous les `exit_fogs` du cluster.

### 1.2 World connections

Les world connections sont définies dans la section `To:` des Areas dans fog.txt. Elles peuvent être :

| Type | Tags | Bidirectionnel ? | Exemple |
|------|------|------------------|---------|
| Normal | (aucun) | Oui | `stormveil_start ↔ stormveil` |
| Drop | `drop` | Non (A→B seulement) | `academy_courtyard → academy_redwolf` |
| Conditionnel item | `Cond: rustykey` | Oui (on donne les items) | `stormveil_start → stormveil` |
| Conditionnel zone | `Cond: some_zone` | Non pertinent pour clusters | Ignoré |

### 1.3 Random links (fogs)

Les fogs sont définis dans les sections `Entrances` et `Warps` de fog.txt. Chaque fog connecte deux zones (ASide et BSide).

| Tag | ASide peut être... | BSide peut être... |
|-----|--------------------|--------------------|
| (bidirectionnel) | entry + exit | entry + exit |
| `unique` | exit seulement | entry seulement |

### 1.4 Zones d'entrée d'un cluster

Les **zones d'entrée** d'un cluster sont les zones qui n'ont pas de world connection entrante unidirectionnelle depuis une autre zone du cluster.

- Si toutes les world connections sont bidirectionnelles → toutes les zones sont zones d'entrée
- Si A → B (drop) → seul A est zone d'entrée

---

## 2. Input / Output

### 2.1 Input

- `reference/fogrando-data/fog.txt` (YAML)
- `core/data/zone_metadata.toml` (métadonnées supplémentaires)

### 2.2 Output

- `core/data/clusters.json`

### 2.3 Structure de sortie

```json
{
  "version": "1.0",
  "generated_from": "fog.txt",
  "clusters": [
    {
      "id": "academy_entrance_f3a1",
      "zones": ["academy_entrance", "academy"],
      "type": "legacy_dungeon",
      "weight": 15,
      "entry_fogs": [
        {"fog_id": "raya_main_to_south", "zone": "academy_entrance"},
        {"fog_id": "raya_south_to_main", "zone": "academy_entrance"},
        {"fog_id": "red_wolf_front", "zone": "academy"}
      ],
      "exit_fogs": [
        {"fog_id": "raya_main_to_south", "zone": "academy_entrance"},
        {"fog_id": "raya_south_to_main", "zone": "academy_entrance"},
        {"fog_id": "red_wolf_front", "zone": "academy"},
        {"fog_id": "abduction_volcano", "zone": "academy", "unique": true}
      ]
    },
    {
      "id": "academy_redwolf_b7c2",
      "zones": ["academy_redwolf"],
      "type": "legacy_dungeon",
      "weight": 5,
      "entry_fogs": [
        {"fog_id": "red_wolf_front", "zone": "academy_redwolf"},
        {"fog_id": "red_wolf_back", "zone": "academy_redwolf"}
      ],
      "exit_fogs": [
        {"fog_id": "red_wolf_front", "zone": "academy_redwolf"},
        {"fog_id": "red_wolf_back", "zone": "academy_redwolf"}
      ]
    },
    {
      "id": "academy_courtyard_d4e8",
      "zones": ["academy_courtyard", "academy_redwolf"],
      "type": "legacy_dungeon",
      "weight": 12,
      "entry_fogs": [
        {"fog_id": "courtyard_fog_1", "zone": "academy_courtyard"},
        {"fog_id": "courtyard_fog_2", "zone": "academy_courtyard"},
        {"fog_id": "courtyard_to_rooftops", "zone": "academy_courtyard"}
      ],
      "exit_fogs": [
        {"fog_id": "courtyard_fog_1", "zone": "academy_courtyard"},
        {"fog_id": "courtyard_fog_2", "zone": "academy_courtyard"},
        {"fog_id": "courtyard_to_rooftops", "zone": "academy_courtyard"},
        {"fog_id": "waygate_church_vows", "zone": "academy_courtyard", "unique": true},
        {"fog_id": "red_wolf_front", "zone": "academy_redwolf"},
        {"fog_id": "red_wolf_back", "zone": "academy_redwolf"}
      ]
    },
    {
      "id": "academy_rooftops_9f5a",
      "zones": ["academy_rooftops", "academy", "academy_courtyard", "academy_redwolf"],
      "type": "legacy_dungeon",
      "weight": 25,
      "entry_fogs": [
        {"fog_id": "rooftops_entrance", "zone": "academy_rooftops"}
      ],
      "exit_fogs": [
        {"fog_id": "rooftops_entrance", "zone": "academy_rooftops"},
        {"fog_id": "red_wolf_front", "zone": "academy"},
        {"fog_id": "abduction_volcano", "zone": "academy", "unique": true},
        {"fog_id": "courtyard_fog_1", "zone": "academy_courtyard"},
        {"fog_id": "courtyard_fog_2", "zone": "academy_courtyard"},
        {"fog_id": "courtyard_to_rooftops", "zone": "academy_courtyard"},
        {"fog_id": "waygate_church_vows", "zone": "academy_courtyard", "unique": true},
        {"fog_id": "red_wolf_front", "zone": "academy_redwolf"},
        {"fog_id": "red_wolf_back", "zone": "academy_redwolf"}
      ]
    },
    {
      "id": "stormveil_start_c1d3",
      "zones": ["stormveil_start", "stormveil"],
      "type": "legacy_dungeon",
      "weight": 20,
      "entry_fogs": [
        {"fog_id": "margit_front", "zone": "stormveil_start"},
        {"fog_id": "godrick_front", "zone": "stormveil"},
        {"fog_id": "divine_tower_gate", "zone": "stormveil"}
      ],
      "exit_fogs": [
        {"fog_id": "margit_front", "zone": "stormveil_start"},
        {"fog_id": "godrick_front", "zone": "stormveil"},
        {"fog_id": "divine_tower_gate", "zone": "stormveil"}
      ]
    }
  ]
}
```

### 2.4 Fichier de métadonnées des zones

Le fichier `core/data/zone_metadata.toml` contient les métadonnées supplémentaires non présentes dans fog.txt :

```toml
# Weights par défaut selon le type de zone
[defaults]
legacy_dungeon = 10
catacomb = 4
cave = 4
tunnel = 4
gaol = 4
boss_arena = 2

# Overrides par zone (optionnel)
[zones.stormveil]
weight = 15

[zones.academy]
weight = 12

[zones.volcano_manor]
weight = 18

# ... autres zones avec weights personnalisés
```

Le script utilise d'abord le weight spécifique de la zone si défini, sinon le default selon le type.

---

## 3. Algorithme

### 3.1 Vue d'ensemble

```
1. Parser fog.txt
2. Construire le graphe des zones et world connections
3. Classifier les fogs par zone (entry/exit)
4. Générer tous les clusters possibles
5. Calculer entry_fogs et exit_fogs pour chaque cluster
6. Filtrer et dédupliquer
7. Écrire clusters.json
```

### 3.2 Étape 1 : Parser fog.txt

```python
def parse_fog_txt(path: Path) -> FogData:
    """
    Parse fog.txt et extraire:
    - areas: dict[name, AreaData]
    - entrances: list[EntranceData]
    - warps: list[WarpData]
    """
```

Structures intermédiaires :

```python
@dataclass
class AreaData:
    name: str
    text: str
    maps: list[str]
    tags: list[str]
    to_connections: list[WorldConnection]

@dataclass
class WorldConnection:
    target_area: str
    text: str
    tags: list[str]  # notamment 'drop'
    cond: str | None  # condition (item ou zone)

@dataclass
class EntranceData:
    name: str
    id: int
    aside_area: str
    bside_area: str
    tags: list[str]  # notamment 'unique'
```

### 3.3 Étape 2 : Construire le graphe des world connections

```python
def build_world_graph(areas: dict[str, AreaData]) -> WorldGraph:
    """
    Construire un graphe dirigé des world connections.

    Pour chaque area.to_connections:
      - Si tags contient 'drop' ou pas de lien retour → edge unidirectionnel
      - Si condition est un item (dans KEY_ITEMS) → edge bidirectionnel (garanti)
      - Si condition est une zone → ignorer pour les clusters
      - Sinon → vérifier si lien retour existe pour bidirectionnalité
    """
```

Liste des KEY_ITEMS (à extraire de fog.txt ou définir manuellement) :
```python
KEY_ITEMS = {
    "rustykey", "academyglintstonekey", "discardedpalacekey",
    "drawingroom_key", "dectusmedallion", "roldmedallion",
    "carian_inverted_statue", "darkMoonRing", "purebloodknightsmedal",
    # ... etc
}
```

### 3.4 Étape 3 : Classifier les fogs par zone

```python
def classify_fogs(entrances: list[EntranceData], warps: list[WarpData]) -> dict[str, ZoneFogs]:
    """
    Pour chaque zone, déterminer ses entry_fogs et exit_fogs.

    Pour chaque fog:
      # Ignorer les fogs non-randomisables
      if 'norandom' in fog.tags:
          continue

      aside_zone = fog.aside_area
      bside_zone = fog.bside_area
      is_unique = 'unique' in fog.tags

      if is_unique:
          zones[aside_zone].exit_fogs.add(fog)
          zones[bside_zone].entry_fogs.add(fog)
      else:
          zones[aside_zone].entry_fogs.add(fog)
          zones[aside_zone].exit_fogs.add(fog)
          zones[bside_zone].entry_fogs.add(fog)
          zones[bside_zone].exit_fogs.add(fog)
    """
```

### 3.5 Étape 4 : Générer tous les clusters possibles

```python
def generate_clusters(zones: set[str], world_graph: WorldGraph) -> list[Cluster]:
    """
    Pour chaque zone Z dans notre scope:
      1. Calculer reachable(Z) = zones accessibles depuis Z via world connections
      2. cluster_zones = {Z} ∪ reachable(Z)
      3. Émettre le cluster si non-dupliqué
    """

    seen_clusters: set[frozenset[str]] = set()
    clusters = []

    for zone in zones:
        reachable = flood_fill_reachable(zone, world_graph)
        cluster_zones = frozenset({zone} | reachable)

        if cluster_zones not in seen_clusters:
            seen_clusters.add(cluster_zones)
            clusters.append(Cluster(zones=cluster_zones))

    return clusters
```

Note : `flood_fill_reachable` suit les world connections dans le sens sortant uniquement.

### 3.6 Étape 5 : Calculer entry_fogs et exit_fogs

```python
def compute_cluster_fogs(cluster: Cluster, world_graph: WorldGraph, zone_fogs: dict[str, ZoneFogs]) -> None:
    """
    1. Identifier les zones d'entrée du cluster
    2. entry_fogs = union des entry_fogs des zones d'entrée
    3. exit_fogs = union des exit_fogs de toutes les zones
    """

    # Zones d'entrée = zones sans world connection entrante (unidirectionnelle)
    # depuis une autre zone du cluster
    entry_zones = set(cluster.zones)

    for zone in cluster.zones:
        for other_zone in cluster.zones:
            if other_zone != zone:
                # Si other_zone a un lien unidirectionnel vers zone
                if world_graph.has_unidirectional_edge(other_zone, zone):
                    entry_zones.discard(zone)
                    break

    # Calculer les fogs
    cluster.entry_fogs = []
    cluster.exit_fogs = []

    for zone in entry_zones:
        cluster.entry_fogs.extend(zone_fogs[zone].entry_fogs)

    for zone in cluster.zones:
        cluster.exit_fogs.extend(zone_fogs[zone].exit_fogs)
```

### 3.7 Étape 6 : Filtrer et enrichir

```python
def filter_clusters(clusters: list[Cluster], metadata: ZoneMetadata) -> list[Cluster]:
    """
    Exclure:
    - Clusters sans entry_fogs ou sans exit_fogs
    - Clusters contenant des zones DLC (tags: dlc)
    - Clusters contenant des zones overworld (tags: overworld)
    - Clusters contenant des zones trivial sans fogs

    Enrichir:
    - type: dériver du type de la zone principale
    - weight: somme des weights des zones (depuis zone_metadata.toml)
    - id: générer un identifiant unique ({zone_principale}_{hash_court})
    """

def get_zone_weight(zone: str, zone_type: str, metadata: ZoneMetadata) -> int:
    """Retourne le weight d'une zone depuis les métadonnées."""
    # Override spécifique à la zone
    if zone in metadata.zones:
        return metadata.zones[zone].weight
    # Default selon le type
    return metadata.defaults.get(zone_type, 4)
```

---

## 4. Scope des zones

### 4.1 Zones incluses

- Legacy dungeons (map prefix m10, m11, m13, m14, m15, m16)
- Catacombs (map prefix m30)
- Caves (map prefix m31)
- Tunnels (map prefix m32)
- Gaols (map prefix m39)
- Boss arenas (zones avec DefeatFlag)

### 4.2 Zones exclues

- Overworld (tags: overworld)
- DLC (tags: dlc)
- Trivial sans fogs (tags: trivial et aucun fog)

---

## 5. Exemples détaillés

Cette section présente des exemples représentatifs de tous les cas de figure.

### 5.1 Règle fondamentale : zones liées = même cluster

Si deux zones sont liées par un world connection (bidirectionnel ou garanti par item), elles ne peuvent PAS exister comme clusters séparés. Le joueur peut toujours passer de l'une à l'autre.

**Conséquence** : `[stormveil]` seul et `[stormveil_start]` seul n'existent pas. Seul `[stormveil_start, stormveil]` existe.

### 5.2 `[academy]` — Zone avec fog unique

```
Zone: academy
World connections: aucune sortante (l'entrée depuis academy_entrance est bidirectionnelle,
                   donc academy ne peut pas exister seul → voir 5.3)
Fogs:
  - Red Wolf front (bidirectionnel) → entry + exit
  - Abduction to Volcano Manor (unique) → exit seulement

ATTENTION: Ce cluster N'EXISTE PAS seul car academy_entrance ↔ academy est bidirectionnel.
Voir cluster [academy_entrance, academy] ci-dessous.
```

### 5.3 `[academy_entrance, academy]` — World connection bidirectionnelle

```
Structure:
  academy_entrance ←→ academy (world connection bidirectionnelle, pas de condition)

Zones d'entrée: les deux (bidirectionnel)

Fogs de academy_entrance:
  - Raya Lucaria Main Gate to South (bidirectionnel) → entry + exit
  - Raya Lucaria South Gate to Main (bidirectionnel) → entry + exit

Fogs de academy:
  - Red Wolf front (bidirectionnel) → entry + exit
  - Abduction to Volcano Manor (unique) → exit seulement

Cluster [academy_entrance, academy]:
  entry_fogs: 3 (2 de academy_entrance + 1 Red Wolf front)
              Abduction exclu car tag 'unique' = exit only
  exit_fogs: 4 (2 de academy_entrance + Red Wolf front + Abduction)
  type: legacy_dungeon

NOTE: [academy_entrance] seul et [academy] seul N'EXISTENT PAS car world connection bidirectionnelle.
```

### 5.4 `[academy_redwolf]` — Zone simple sans world connection

```
Zone: academy_redwolf
World connections entrantes: academy_courtyard → academy_redwolf (drop, unidirectionnel)
World connections sortantes: aucune

Fogs:
  - Red Wolf front (bidirectionnel) → entry + exit
  - Red Wolf back (bidirectionnel) → entry + exit

Cluster [academy_redwolf]:
  entry_fogs: 2
  exit_fogs: 2
  type: legacy_dungeon

NOTE: Ce cluster EXISTE car le drop depuis academy_courtyard est unidirectionnel.
      Le joueur arrivant à academy_redwolf ne peut PAS remonter vers academy_courtyard.
```

### 5.5 `[academy_courtyard, academy_redwolf]` — World connection drop

```
Structure:
  academy_courtyard → academy_redwolf (drop, unidirectionnel)

Zones d'entrée: [academy_courtyard] seulement (academy_redwolf reçoit un drop)

Fogs de academy_courtyard:
  - Fog 1 (bidirectionnel) → entry + exit
  - Fog 2 (bidirectionnel) → entry + exit
  - Fog 3 vers academy_rooftops (bidirectionnel) → entry + exit
  - Waygate to Church of Vows (unique) → exit seulement

Fogs de academy_redwolf:
  - Red Wolf front (bidirectionnel) → entry + exit
  - Red Wolf back (bidirectionnel) → entry + exit

Cluster [academy_courtyard, academy_redwolf]:
  entry_fogs: 3 (les 3 fogs bidirectionnels de academy_courtyard)
              Waygate exclu car tag 'unique' = exit only
              Fogs de academy_redwolf exclus car drop unidirectionnel
  exit_fogs: 6 (4 de academy_courtyard + 2 de academy_redwolf)
  type: legacy_dungeon

NOTE: [academy_courtyard] seul N'EXISTE PAS car il a un world connection vers academy_redwolf.
      Le joueur entrant dans academy_courtyard peut toujours dropper vers academy_redwolf.
```

### 5.6 `[academy_rooftops, academy, academy_courtyard, academy_redwolf]` — Cascade complexe

```
Structure:
  academy_rooftops → academy (drop)
  academy_rooftops → academy_courtyard (drop)
  academy_courtyard → academy_redwolf (drop)

Zones d'entrée: [academy_rooftops] seulement (seul sommet de la cascade)

Fogs:
  - academy_rooftops: 1 fog (bidirectionnel)
  - academy: Red Wolf front (bidir) + Abduction (unique)
  - academy_courtyard: 3 fogs (bidir) + Waygate (unique)
  - academy_redwolf: 2 fogs (bidir)

Cluster [academy_rooftops, academy, academy_courtyard, academy_redwolf]:
  entry_fogs: 1 (uniquement le fog de academy_rooftops)
  exit_fogs: 10 (1 + 2 + 4 + 2 + waygates/unique)
             = rooftops(1) + academy(2) + courtyard(4) + redwolf(2) + unique exits
  type: legacy_dungeon

NOTE: Le joueur arrivant à academy_rooftops peut dropper vers toutes les autres zones
      et utiliser n'importe lequel des 10 exit_fogs.
```

### 5.7 `[stormveil_start, stormveil]` — World connection avec condition item

```
Structure:
  stormveil_start → stormveil (Cond: OR scalepass rustykey)
  stormveil → stormveil_start (world connection retour)

La condition "rustykey" est un item → on le donne au joueur → connexion garantie.
Donc la world connection est BIDIRECTIONNELLE.

Zones d'entrée: les deux (bidirectionnel)

Fogs de stormveil_start:
  - Fog vers Margit (bidirectionnel) → entry + exit

Fogs de stormveil:
  - Fog vers Godrick (bidirectionnel) → entry + exit
  - Fog vers Divine Tower (bidirectionnel) → entry + exit

Cluster [stormveil_start, stormveil]:
  entry_fogs: 3 (1 de stormveil_start + 2 de stormveil)
  exit_fogs: 3 (mêmes fogs, tous bidirectionnels)
  type: legacy_dungeon

NOTE: [stormveil_start] seul et [stormveil] seul N'EXISTENT PAS.
```

### 5.8 Résumé des clusters Academy

| Cluster | Zones | Entrées | Sorties | Existe ? |
|---------|-------|---------|---------|----------|
| `[academy]` | academy | - | - | ❌ Non (lié à academy_entrance) |
| `[academy_entrance]` | academy_entrance | - | - | ❌ Non (lié à academy) |
| `[academy_entrance, academy]` | 2 zones | 3 | 4 | ✅ Oui |
| `[academy_redwolf]` | academy_redwolf | 2 | 2 | ✅ Oui |
| `[academy_courtyard]` | academy_courtyard | - | - | ❌ Non (drop vers redwolf) |
| `[academy_courtyard, academy_redwolf]` | 2 zones | 3 | 6 | ✅ Oui |
| `[academy_rooftops, ...]` | 4 zones | 1 | 10 | ✅ Oui |

---

## 6. Utilisation par le générateur DAG

```python
# Charger les clusters
with open("clusters.json") as f:
    data = json.load(f)
    clusters = [Cluster(**c) for c in data["clusters"]]

# Maintenir les zones déjà utilisées
used_zones: set[str] = set()

def select_cluster(
    cluster_type: str,
    min_entries: int,
    min_exits: int,
    target_weight: int
) -> Cluster | None:
    """Sélectionner un cluster compatible."""

    candidates = [
        c for c in clusters
        if c.type == cluster_type
        and len(c.entry_fogs) >= min_entries
        and len(c.exit_fogs) >= min_exits
        and not (set(c.zones) & used_zones)  # Pas de chevauchement
    ]

    if not candidates:
        return None

    # Choisir selon le poids
    cluster = pick_closest_weight(candidates, target_weight)

    # Marquer les zones comme utilisées
    used_zones.update(cluster.zones)

    return cluster
```

---

## 7. Commande

```bash
python tools/generate_clusters.py \
    reference/fogrando-data/fog.txt \
    core/data/clusters.json \
    --exclude-dlc \
    --exclude-overworld
```

---

## 8. Tests

### 8.1 Tests unitaires

- Parser fog.txt correctement
- Classifier les fogs bidirectionnels vs unique
- Détecter les world connections bidirectionnelles vs unidirectionnelles
- Calculer les zones d'entrée d'un cluster

### 8.2 Tests d'intégration

- Générer clusters.json et vérifier :
  - Pas de cluster vide (0 entry ou 0 exit)
  - Pas de duplication
  - Cohérence des types et weights

### 8.3 Validation manuelle

Vérifier quelques clusters connus :
- `stormveil` cluster a bien les bonnes zones
- `academy` cascade a le bon nombre d'entry/exit fogs
- Les mini-dungeons ont bien 2 fogs (entrée + boss)

---

## 9. Notes d'implémentation

### 9.1 Gestion des conditions

Pour les `Cond:` sur les world connections :

```python
def is_guaranteed_connection(cond: str | None) -> bool:
    """Une connexion est garantie si pas de condition ou condition item."""
    if cond is None:
        return True

    # Parser la condition (peut être "OR item1 item2" ou "zone_name")
    tokens = cond.split()

    # Si tous les tokens sont des items connus → garanti
    if tokens[0] in ("OR", "AND"):
        items = tokens[1:]
    else:
        items = tokens

    return all(item in KEY_ITEMS for item in items)
```

### 9.2 Gestion des tags spéciaux

| Tag | Effet |
|-----|-------|
| `unused` | Ignorer complètement |
| `crawlonly` | Ignorer (on n'est pas en mode crawl) |
| `dlc`, `dlc1`, `dlc2` | Exclure |
| `unique` | Fog unidirectionnel |
| `drop` | World connection unidirectionnelle |
| `norandom` | Fog fixe, exclure des entry_fogs et exit_fogs |

### 9.3 IDs des clusters

Format suggéré : `{zone_principale}_{hash_court}`

Exemple : `academy_courtyard_a3f2`

Le hash court permet de distinguer les clusters qui ont la même zone principale mais des compositions différentes.
