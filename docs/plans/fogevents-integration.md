# SpeedFog: Integration de fogevents.txt

## Philosophie SpeedFog

**SpeedFog = FogRando avec un DAG custom et certains settings forcés.**

- **Ce que SpeedFog fait différemment** : Génération du graphe (DAG contrôlé, ~1h de jeu, pas de dead ends)
- **Ce que SpeedFog copie de FogRando** : Tout le reste (events, scripting, comportement in-game)

Cette philosophie implique qu'on doit utiliser le code/data de FogRando au maximum, pas réinventer.

## Contexte

SpeedFog utilise actuellement un fichier `speedfog-events.yaml` custom avec des IDs dans la range 79000xxx. Cette approche pose plusieurs problèmes :

1. **Duplication** : On recopie manuellement les events de FogRando
2. **Divergence** : Risque de bugs si FogRando corrige ses events
3. **Incomplétude** : Seulement 9 events implémentés sur ~40 nécessaires

## Objectif

Utiliser directement `reference/fogrando-data/fogevents.txt` avec les IDs originaux de FogRando.

## Architecture Cible

```
reference/fogrando-data/fogevents.txt    data/speedfog-events.yaml (minimal)
              │                                      │
              └──────────┬───────────────────────────┘
                         ▼
                EventTemplateRegistry
                         │
                         ▼
                   EventBuilder
                         │
                         ▼
                 EMEVD Instructions
```

## Format de fogevents.txt

Le fichier contient 3 sections (23,067 lignes) :

```yaml
NewEvents:        # Templates réutilisables (L1-483) ← ON UTILISE CECI
  - ID: 9005770
    Name: scale
    Comment: X0_4 = entity, X4_4 = speffect
    Commands:
      - IfCharacterBackreadStatus(MAIN, X0_4, true, ...)
      - // Ceci est un commentaire
    Tags: restart

WarpArgs:         # Meta-info (L484-498) ← IGNORÉ
Events:           # Patches vanilla (L499-23067) ← IGNORÉ pour v1
```

### Différences avec notre YAML

| Aspect | fogevents.txt | speedfog-events.yaml |
|--------|---------------|----------------------|
| Structure | Liste avec ID/Name | Dict avec name comme clé |
| Restart | `Tags: restart` | `restart: true` |
| Commentaires | `// inline` | `# YAML` |
| Params doc | Dans Comment | Section `params:` |

## Mapping des IDs

| Template | SpeedFog actuel | FogRando | Action |
|----------|-----------------|----------|--------|
| scale | 79005770 | **9005770** | Migrer |
| showsfx | 79005775 | **9005775** | Migrer |
| fogwarp | 79000003 | **9005777** | Migrer |
| fogwarp_simple | 79000010 | - | **Supprimer** (utiliser fogwarp + init 4280) |
| startboss | 79000004 | **9005776** | Migrer |
| disable | 79000005 | **9005778** | Migrer |
| give_items | 79000006 | - | Remplacer par common_startingitem |

### SpEffect 4280 ("trapped")

fogwarp vérifie que le joueur a le speffect 4280 avant de permettre le warp. Sans ce speffect, le joueur voit un dialogue d'erreur.

**Solution** : Ajouter `SetSpEffect(10000, 4280)` dans l'initialisation du run (event 0 de common.emevd ou via un event dédié).

Cela élimine le besoin de `fogwarp_simple` - on utilise directement `fogwarp` (9005777) de FogRando.

### Nouveaux events disponibles (FogRando)

| ID | Name | Usage SpeedFog |
|----|------|----------------|
| 9005772 | setflag | Ouvrir portes via event flag |
| 9005779 | disablecond | Disable conditionnel |
| 9005780 | stakeflag | Stake of Marika |
| 9005781 | startboss2 | Boss avec encounter flag |
| 9005782 | flaskrefill | Flask NPC |
| 9005783 | flaskrefill2 | Flask via speffect |
| 755850000 | common_makestable | Boss arena respawn |
| 755850202 | common_roundtable | Roundtable au start |
| 755850205 | common_gracetable | Roundtable après grace |
| 755850220 | common_abduction | Immortalité grab Mohg |
| 755850250 | common_bellofreturn | Retour au Chapel |
| 755850280 | common_fingerstart | Start après pickup finger |
| 755850282 | common_fingerdoor | Auto-open Chapel door |
| 755856200 | common_startingitem | Donner items au start |

## Implémentation

### Phase 1: Parser fogevents.txt

**Nouveau fichier** : `writer/SpeedFogWriter/Models/FogEventConfig.cs`

```csharp
public class FogEventConfig
{
    public List<NewEvent> NewEvents { get; set; }
}

public class NewEvent
{
    public int ID { get; set; }
    public string Name { get; set; }
    public string Comment { get; set; }
    public List<string> Commands { get; set; }
    public string Tags { get; set; }

    public bool HasTag(string tag) =>
        Tags?.Split(',').Any(t => t.Trim() == tag) ?? false;
}
```

**Nouveau fichier** : `writer/SpeedFogWriter/Parsers/FogEventsParser.cs`

```csharp
public static class FogEventsParser
{
    public static FogEventConfig Load(string path)
    {
        var yaml = File.ReadAllText(path);
        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(PascalCaseNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();
        return deserializer.Deserialize<FogEventConfig>(yaml);
    }
}
```

### Phase 2: EventTemplateRegistry

**Nouveau fichier** : `writer/SpeedFogWriter/Writers/EventTemplateRegistry.cs`

Registre unifié qui charge :
1. FogRando events depuis `fogevents.txt`
2. SpeedFog-specific events depuis `speedfog-events.yaml` (optionnel)

```csharp
public class EventTemplateRegistry
{
    private readonly Dictionary<string, EventTemplate> _byName = new();
    private readonly Dictionary<int, EventTemplate> _byId = new();

    public EventTemplate GetByName(string name) => _byName[name];
    public EventTemplate GetById(int id) => _byId[id];
    public bool TryGetByName(string name, out EventTemplate t) => _byName.TryGetValue(name, out t);

    public IEnumerable<EventTemplate> GetAllTemplates() => _byId.Values;

    public static EventTemplateRegistry Load(string fogEventsPath, string? supplementalPath = null)
    {
        var registry = new EventTemplateRegistry();

        // 1. Charger FogRando NewEvents
        var fogConfig = FogEventsParser.Load(fogEventsPath);
        foreach (var evt in fogConfig.NewEvents.Where(e => e.Commands != null))
        {
            var template = new EventTemplate
            {
                Id = evt.ID,
                Name = evt.Name,
                Restart = evt.HasTag("restart"),
                Commands = Decomment(evt.Commands)
            };
            registry.Register(template);
        }

        // 2. Charger SpeedFog supplemental (override si même nom)
        if (supplementalPath != null && File.Exists(supplementalPath))
        {
            var sfConfig = SpeedFogEventConfig.Load(supplementalPath);
            foreach (var (name, template) in sfConfig.Templates)
            {
                template.Name = name;
                registry.Register(template); // Override ou ajoute
            }
        }

        return registry;
    }

    private static List<string> Decomment(List<string> commands)
    {
        return commands
            .Select(c => {
                var idx = c.IndexOf("//");
                return idx >= 0 ? c.Substring(0, idx).Trim() : c.Trim();
            })
            .Where(c => !string.IsNullOrWhiteSpace(c) && !c.StartsWith("#"))
            .ToList();
    }
}
```

### Phase 3: Modifier EventBuilder

**Fichier** : `writer/SpeedFogWriter/Writers/EventBuilder.cs`

```csharp
public class EventBuilder
{
    private readonly EventTemplateRegistry _registry;  // Remplace SpeedFogEventConfig
    private readonly Events _events;

    public EventBuilder(EventTemplateRegistry registry, Events events)
    {
        _registry = registry;
        _events = events;
    }

    // Reste identique, utilise _registry au lieu de _config
}
```

### Phase 4: Modifier ModWriter

**Fichier** : `writer/SpeedFogWriter/Writers/ModWriter.cs`

```csharp
// Dans LoadSpeedFogData():
var fogEventsPath = Path.Combine(_dataDir, "..", "reference", "fogrando-data", "fogevents.txt");
var supplementalPath = Path.Combine(_dataDir, "speedfog-events.yaml");
_eventRegistry = EventTemplateRegistry.Load(fogEventsPath, supplementalPath);
_eventBuilder = new EventBuilder(_eventRegistry, _loader!.EventsHelper!);
```

**Placement des events** (comme FogRando) :
- `common_*` → `common.emevd`
- Autres → `common_func.emevd`

```csharp
private void RegisterTemplateEvents()
{
    foreach (var template in _eventRegistry.GetAllTemplates())
    {
        var targetEmevd = template.Name?.StartsWith("common_") == true
            ? "common"
            : "common_func";

        // ... reste de la logique
    }
}
```

### Phase 5: Supprimer speedfog-events.yaml

Avec l'utilisation de `fogwarp` + init speffect 4280, **tous les events viennent de fogevents.txt**.

Le fichier `speedfog-events.yaml` peut être supprimé ou gardé vide pour référence future.

```yaml
# SpeedFog Supplemental Events
# Actuellement vide - tous les events viennent de fogevents.txt
templates: {}
```

### Phase 6: Ajouter les events manquants

Dans `ModWriter`, ajouter l'initialisation des nouveaux events selon la config :

```csharp
private void RegisterCommonEvents()
{
    // Chapel door auto-open
    AddCommonInit("common_fingerstart", 0);
    AddCommonInit("common_fingerdoor", 0);

    // Roundtable access
    if (_config.EnableRoundtable)
    {
        AddCommonInit("common_gracetable", 0);
    }

    // Boss arena respawn (per boss)
    foreach (var boss in _graph.BossNodes)
    {
        AddCommonInit("common_makestable", slot++, boss.StableFlag, boss.DefeatFlag);
    }

    // Starting items
    foreach (var itemLot in _config.StartingItemLots)
    {
        AddCommonInit("common_startingitem", slot++, itemLot);
    }
}
```

## Fichiers à modifier

| Fichier | Action |
|---------|--------|
| `Models/FogEventConfig.cs` | **Créer** - Classes pour parser fogevents.txt |
| `Parsers/FogEventsParser.cs` | **Créer** - Parser YAML |
| `Writers/EventTemplateRegistry.cs` | **Créer** - Registre unifié |
| `Writers/EventBuilder.cs` | **Modifier** - Utiliser registry |
| `Writers/ModWriter.cs` | **Modifier** - Charger fogevents.txt, placer events, init 4280 |
| `Models/EventTemplate.cs` | **Modifier** - Ajouter propriété Name |
| `data/speedfog-events.yaml` | **Supprimer** ou vider (plus nécessaire) |

## Vérification

```bash
# Build
cd writer/SpeedFogWriter && dotnet build

# Test unitaire du parser
dotnet test --filter "FogEventsParser"

# Test intégration
cd writer/test && ./run_integration.sh

# Vérifier les events générés
unzip -p output/mod/event/common_func.emevd.dcx | xxd | head
```

## Risques

| Risque | Mitigation |
|--------|------------|
| Format fogevents.txt change | Tester avec plusieurs versions FogRando |
| Collision IDs | Utiliser 9005790-9005799 pour SpeedFog |
| Events non testés | Ajouter tests pour chaque event utilisé |

## Settings FogRando forcés pour SpeedFog

SpeedFog équivaut à FogRando avec ces settings :

| Setting FogRando | Valeur SpeedFog | Raison |
|------------------|-----------------|--------|
| `roundtable` | `true` | Accès Roundtable dès le début |
| `scale` | `true` | Scaling des ennemis par tier |
| `Feature.ChapelInit` | `true` | Start au Chapel of Anticipation |
| `Feature.NoBossBonfire` | `false` | Graces après boss autorisées |
| `Feature.Segmented` | `false` | Pas de mode segmenté |

## Décisions techniques

1. **Copier ou référencer fogevents.txt ?**
   - Référencer in-place depuis `reference/`

2. **fogwarp_simple ?**
   - **Supprimé** - Utiliser `fogwarp` (9005777) + init speffect 4280 au début

3. **Events activés (équivalent FogRando avec settings ci-dessus)** :
   - `common_fingerstart` (755850280) - Start après pickup finger
   - `common_fingerdoor` (755850282) - Auto-open Chapel door
   - `common_gracetable` (755850205) - Roundtable après grace
   - `common_makestable` (755850000) - Boss arena respawn
   - `common_bellofreturn` (755850250) - Retour au Chapel
   - `common_abduction` (755850220) - Immortalité grab Mohg
   - **+ Init speffect 4280** - Permettre traversée des fogs
