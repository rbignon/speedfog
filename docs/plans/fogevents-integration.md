# SpeedFog: Integration de fogevents.txt

> **STATUS: IMPLEMENTED** - Ce plan a été implémenté. Voir les commits `4d335d6` et `2ce1ca5`.
>
> **ERRATUM**: La section sur le SpEffect 4280 contenait une erreur. Voir "Correction SpEffect 4280" ci-dessous.

## Philosophie SpeedFog

**SpeedFog = FogRando avec un DAG custom et certains settings forcés.**

- **Ce que SpeedFog fait différemment** : Génération du graphe (DAG contrôlé, ~1h de jeu, pas de dead ends)
- **Ce que SpeedFog copie de FogRando** : Tout le reste (events, scripting, comportement in-game)

Cette philosophie implique qu'on doit utiliser le code/data de FogRando au maximum, pas réinventer.

## Résultat de l'implémentation

- `data/fogevents.txt` - Fichier FogRando utilisé directement (copié de reference/)
- `data/speedfog-events.yaml` - **Supprimé** (plus nécessaire)
- Events common initialisés dans `RegisterCommonEvents()` de ModWriter.cs

## Correction SpEffect 4280

**ERREUR ORIGINALE** : Le plan indiquait que le joueur devait AVOIR le SpEffect 4280 pour traverser les fogs.

**CORRECTION** : C'est l'inverse ! Le SpEffect 4280 est l'état "trapped" qui **EMPÊCHE** la traversée.

Le template fogwarp vérifie :
```yaml
IfCharacterHasSpEffect(AND_06, 10000, 4280, false, ...)  # false = joueur N'A PAS 4280
GotoIfConditionGroupStateUncompiled(Label.Label10, PASS, AND_06)  # Si pas 4280, warp OK
```

**Solution correcte** : Ne PAS mettre SetSpEffect(10000, 4280) au démarrage. Le joueur n'a pas 4280 par défaut, ce qui lui permet de traverser les fogs.

Le SpEffect 4280 est mis temporairement pendant certains événements (grab Iron Virgin) puis retiré.

## Events communs utilisés

| Event | ID | Usage |
|-------|-----|-------|
| common_fingerstart | 755850280 | Set flag 1040292051 quand finger pickup (60210) |
| common_fingerdoor | 755850282 | Auto-open Chapel door |
| common_autostart | 755850204 | Donne ItemLot 10010000 (flasks, items de départ) |
| common_roundtable | 755850202 | Roundtable access (attend flag 1040292051) |
| common_bellofreturn | 755850250 | Retour au Chapel avec Bell of Return |
| common_abduction | 755850220 | Immortalité pendant grab Iron Virgin |

**Note** : On utilise `common_roundtable` (pas `common_gracetable`) car il attend le flag 1040292051 mis par `common_fingerstart`.

---

## Plan original (pour référence historique)

### Contexte

SpeedFog utilisait un fichier `speedfog-events.yaml` custom avec des IDs dans la range 79000xxx. Cette approche posait plusieurs problèmes :

1. **Duplication** : On recopiait manuellement les events de FogRando
2. **Divergence** : Risque de bugs si FogRando corrige ses events
3. **Incomplétude** : Seulement 9 events implémentés sur ~40 nécessaires

### Objectif

Utiliser directement `fogevents.txt` de FogRando avec les IDs originaux.

### Format de fogevents.txt

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

### Mapping des IDs

| Template | SpeedFog ancien | FogRando | Action |
|----------|-----------------|----------|--------|
| scale | 79005770 | **9005770** | Migré |
| showsfx | 79005775 | **9005775** | Migré |
| fogwarp | 79000003 | **9005777** | Migré |
| fogwarp_simple | 79000010 | - | **Supprimé** |
| startboss | 79000004 | **9005776** | Migré |
| disable | 79000005 | **9005778** | Migré |

### Settings FogRando équivalents

| Setting FogRando | Valeur SpeedFog | Raison |
|------------------|-----------------|--------|
| `roundtable` | `true` | Accès Roundtable dès le début |
| `scale` | `true` | Scaling des ennemis par tier |
| `Feature.ChapelInit` | `false` | On utilise fingerstart + roundtable |
| `Feature.NoBossBonfire` | `false` | Graces après boss autorisées |
| `Feature.Segmented` | `false` | Pas de mode segmenté |

### Fichiers créés/modifiés

| Fichier | Action |
|---------|--------|
| `Models/FogEventConfig.cs` | Créé - Classes pour parser fogevents.txt |
| `Parsers/FogEventsParser.cs` | Créé - Parser YAML |
| `Writers/EventTemplateRegistry.cs` | Créé - Registre unifié |
| `Writers/EventBuilder.cs` | Modifié - Utilise registry |
| `Writers/ModWriter.cs` | Modifié - Charge fogevents.txt, init events |
| `Models/EventTemplate.cs` | Modifié - Ajout propriété Name |
| `data/speedfog-events.yaml` | Supprimé |
| `data/fogevents.txt` | Ajouté (copié de reference/) |
