# SpeedFog Racing - Design Document

**Date:** 2026-02-04
**Status:** Draft

## 1. Vue d'ensemble et objectifs

**SpeedFog Racing** est une plateforme de courses compétitives pour SpeedFog, permettant à plusieurs joueurs de s'affronter sur une même seed avec tracking en temps réel.

### Objectifs

1. **Joueurs** : Overlay in-game affichant leur progression, le classement live, et les infos de zone
2. **Organisateurs** : Interface web pour créer des races, gérer les participants, distribuer les .zip personnalisés
3. **Spectateurs/Casteurs** : Visualisation du DAG avec positions des joueurs en temps réel (overlay Twitch)

### Scope MVP

- Authentification Twitch
- Création de races (mode synchrone avec countdown)
- Pool de seeds pré-générées (multi-pools avec settings différents)
- Distribution de .zip personnalisés (token par joueur)
- Mod Rust avec overlay in-game (zone, IGT, classement)
- WebSocket temps réel (mod <-> serveur <-> frontend)
- Page spectateur avec DAG horizontal
- Overlays OBS (fond transparent)

### Hors scope MVP (futur)

- Races asynchrones
- Génération de seeds à la demande (nécessite Wine sur serveur)
- Brackets/tournois
- Statistiques historiques par joueur
- Events EMEVD customs pour tracking précis
- Affichage progressif du chemin pour joueurs

---

## 2. Architecture technique

### Repositories

```
speedfog/                    # Existant - Générateur de seeds
├── speedfog/                # Package Python (DAG generation)
├── writer/                  # C# wrappers (FogMod, ItemRandomizer)
└── output/                  # Seeds générées

speedfog-racing/             # Nouveau - Plateforme de courses
├── server/                  # Python/FastAPI
├── web/                     # Svelte/SvelteKit
├── mod/                     # Rust (fork er-fog-vizu)
└── tools/                   # Scripts (generate_pool.py)
```

### Dépendance speedfog-racing -> speedfog

Découplage via CLI. Le script `generate_pool.py` appelle speedfog en subprocess :

```python
subprocess.run(
    ["uv", "run", "speedfog", str(config_file), "-o", str(output_dir)],
    cwd=SPEEDFOG_PATH,  # Env var ou config
    check=True,
)
```

Chaque projet garde son propre venv. Le seul lien est le chemin `SPEEDFOG_PATH`.

### Stack technique

| Composant | Technologie | Justification |
|-----------|-------------|---------------|
| Serveur | FastAPI + SQLAlchemy async | Réutilise patterns er-fog-vizu, WebSocket natif |
| Base de données | PostgreSQL | Robuste, JSON support pour configs |
| Frontend | SvelteKit | Réactivité native, léger, bon pour temps réel |
| Mod | Rust + ImGui | Fork er-fog-vizu, injection DLL |
| Communication | WebSocket | Temps réel bidirectionnel |
| Auth | Twitch OAuth | Cible communauté streaming |

### Flux de données principal

```
Mod Rust <--WebSocket--> Serveur FastAPI <--WebSocket--> Frontend Svelte
   |                           |                              |
   | Envoie:                   | Stocke:                      | Affiche:
   | - IGT                     | - Etat races                 | - DAG + positions
   | - Zone actuelle           | - Progression joueurs        | - Classement
   | - Traversées fog          | - IGT                        | - Stats live
   | - Death count             |                              |
   |                           | Broadcast:                   |
   | Reçoit:                   | - Updates à tous             |
   | - Classement              |   les clients                |
   | - Etat autres joueurs     |                              |
```

---

## 3. Modèle de données

### Entités principales

```
┌─────────────┐       ┌─────────────┐       ┌─────────────┐
│    User     │       │    Race     │       │    Seed     │
├─────────────┤       ├─────────────┤       ├─────────────┤
│ id          │       │ id          │       │ id          │
│ twitch_id   │       │ name        │       │ seed_number │
│ twitch_name │<──────│ organizer_id│       │ pool_name   │
│ avatar_url  │       │ seed_id     │──────>│ graph_json  │
│ api_token   │       │ status      │       │ total_layers│
│ is_admin    │       │ mode        │       │ zip_path    │
│ created_at  │       │ config      │       │ status      │
└─────────────┘       │ scheduled_  │       │ created_at  │
      ^               │   start     │       └─────────────┘
      │               │ created_at  │
      │               └─────────────┘
      │                     │
      │                     │ 1:N
      │                     v
      │               ┌─────────────┐
      │               │ Participant │
      └───────────────├─────────────┤
                      │ id          │
                      │ race_id     │
                      │ user_id     │
                      │ mod_token   │
                      │ current_zone│
                      │ current_layer│
                      │ igt_ms      │
                      │ death_count │
                      │ finished_at │
                      │ status      │
                      └─────────────┘
```

### Statuts

**Race.status** : `draft` -> `open` -> `countdown` -> `running` -> `finished`

**Participant.status** : `registered` -> `ready` -> `playing` -> `finished` | `abandoned`

**Seed.status** : `available` -> `consumed`

### Config Race (JSON)

```json
{
  "show_finished_names": true,
  "countdown_seconds": 10,
  "max_participants": 8
}
```

---

## 4. Workflows utilisateur

### Création d'une race (Organisateur)

1. **Connexion Twitch** : Redirect OAuth -> callback -> session créée
2. **Nouvelle race** :
   - Nom, config (show_finished_names, max_participants)
   - Sélection du pool (Sprint/Standard/Marathon) avec affichage des settings
   - Seed assignée aléatoirement depuis le pool choisi
   - Race créée en status "draft"
3. **Gestion participants** :
   - Ajouter joueurs par pseudo Twitch
   - Si compte existant -> ajouté directement
   - Si pas de compte -> génère lien invitation `/invite/{token}`
4. **Lancement** :
   - Définir scheduled_start (datetime picker avec timezone)
   - Clic "Générer les .zip" -> serveur génère zip personnalisé par joueur
   - Chaque joueur télécharge son .zip
   - Clic "Lancer" quand tout le monde ready -> countdown synchronisé

### Participation à une race (Joueur)

1. **Rejoindre** : Connexion Twitch -> inscription via lien ou ajout par orga
2. **Préparation** :
   - Télécharge son .zip personnalisé
   - Dézip, lance `launch_speedfog.bat`
   - Mod se connecte au serveur (token dans config)
   - Status passe à "ready" quand connecté
3. **Course** :
   - Countdown affiché dans l'overlay (calculé depuis scheduled_start)
   - GO ! -> Nouveau personnage, IGT commence
   - Progression trackée via traversées de fog gates
   - Classement mis à jour en temps réel
4. **Fin** : Boss final vaincu -> status "finished", IGT enregistré

---

## 5. Protocole WebSocket

### Connexions

```
/ws/mod/{race_id}      # Mod Rust -> Serveur (auth par mod_token)
/ws/race/{race_id}     # Frontend -> Serveur (spectateurs, organisateur)
```

### Messages Mod -> Serveur

```typescript
// Authentification
{ type: "auth", mod_token: "abc123" }

// Joueur prêt (connecté, en jeu)
{ type: "ready" }

// Mise à jour périodique (toutes les ~2-5 sec)
{ type: "status_update",
  igt_ms: 123456,
  current_zone: "altus_sagescave",
  current_layer: 3,
  death_count: 7 }

// Traversée de fog gate
{ type: "zone_entered",
  from_zone: "caelid_gaolcave_boss",
  to_zone: "altus_sagescave",
  igt_ms: 98765 }

// Course terminée (boss final vaincu)
{ type: "finished", igt_ms: 6543210 }
```

### Messages Serveur -> Mod

```typescript
// Auth OK + état initial
{ type: "auth_ok",
  race: { name, status, scheduled_start },
  seed: { total_layers },
  participants: [...] }

// GO!
{ type: "race_start" }

// Mise à jour classement (broadcast à tous les mods)
{ type: "leaderboard_update",
  participants: [
    { name: "Player1", layer: 8, igt_ms: null, death_count: 3, finished: false },
    { name: "Player2", layer: 6, igt_ms: 654321, death_count: 5, finished: true }
  ]}
```

### Messages Serveur -> Frontend (spectateurs)

```typescript
// État complet de la race
{ type: "race_state",
  race: { name, status, scheduled_start },
  seed: { graph_json },  // Pour afficher le DAG
  participants: [
    { name, zone_id, layer, igt_ms, death_count, status }
  ]}

// Mise à jour position d'un joueur
{ type: "player_update",
  player: { name, zone_id, layer, igt_ms, death_count, status }}
```

---

## 6. Overlay in-game (Mod Rust)

### Layout

```
┌────────────────────────────────────────┐
│ Altus Sagescave              01:23:45  │  <- Zone | IGT
│ Tier 8                          3/12   │  <- Scaling | Layer
├────────────────────────────────────────┤
│ 1. Player4 [FIN]   01:45:32         ✓  │  <- Terminés en haut (tri IGT)
│ 2. Player1         ██████████    8/12  │  <- En cours (tri layer)
│ 3. Toi             ███████       6/12  │  <- Highlight couleur
│ 4. Player3         █████         5/12  │
├────────────────────────────────────────┤
│ > Exits (F11 pour replier)             │  <- Optionnel/repliable
│   <- Caelid Gaol Cave (origin)         │
│   -> ??? (undiscovered)                │
└────────────────────────────────────────┘
```

### Logique de classement

1. **Joueurs terminés** en haut, triés par IGT de fin (le plus rapide en premier)
2. **Joueurs en cours** en dessous, triés par progression (layer)

### Config organisateur

- `show_finished_names: true/false` - Afficher les noms des joueurs terminés

### Fork er-fog-vizu

**Conservé :**
- `core/` : Types, map_utils, warp_tracker
- `eldenring/` : Memory reading, game_state, animations
- `dll/ui.rs` : Rendu ImGui overlay
- `dll/websocket.rs` : Client WebSocket (à adapter)

**Supprimé :**
- `launcher/` : Pas de GUI launcher

**Config (speedfog_race.toml) :**

```toml
[server]
url = "wss://speedfog-racing.example.com"
mod_token = "player_specific_token_here"
race_id = "uuid-of-race"

[overlay]
show_exits = true
font_size = 16

[keybindings]
toggle_ui = "f9"
toggle_exits = "f11"
```

### Injection

Via ModEngine2 (inclus dans le .zip) :

```toml
# config_speedfog/config.toml (ModEngine2)
[modengine]
external_dlls = ["speedfog_race.dll"]
```

---

## 7. Serveur FastAPI

### Structure

```
speedfog-racing/server/
├── speedfog_racing/
│   ├── __init__.py
│   ├── main.py              # FastAPI app, lifespan, CORS
│   ├── config.py            # Settings (env vars, seeds_pool_dir)
│   ├── database.py          # SQLAlchemy async, models
│   ├── auth.py              # Twitch OAuth
│   │
│   ├── api/
│   │   ├── auth.py          # /api/auth/twitch, /api/auth/callback
│   │   ├── races.py         # CRUD races, participants
│   │   ├── seeds.py         # Stats admin
│   │   └── users.py         # Profil
│   │
│   ├── websocket/
│   │   ├── manager.py       # RaceRoom, connexions par race
│   │   ├── mod.py           # Handler connexions mod
│   │   └── spectator.py     # Handler connexions spectateurs
│   │
│   └── services/
│       ├── race_service.py  # Logique métier races
│       ├── seed_service.py  # Pool management, zip generation
│       └── leaderboard.py   # Calcul classement temps réel
│
├── alembic/                 # Migrations DB
├── tests/
├── pyproject.toml
└── .env.example
```

### Endpoints principaux

```
Auth:
  GET  /api/auth/twitch              -> Redirect OAuth Twitch
  GET  /api/auth/callback            -> Callback, crée session
  GET  /api/auth/me                  -> User courant

Races:
  POST /api/races                    -> Créer race (organizer)
  GET  /api/races/{id}               -> Détails race
  POST /api/races/{id}/participants  -> Ajouter joueur (by twitch name)
  POST /api/races/{id}/generate-zips -> Générer .zip personnalisés
  POST /api/races/{id}/start         -> Définir scheduled_start, lancer
  GET  /api/races/{id}/download/{token} -> Télécharger son .zip

Seeds (admin):
  GET  /api/admin/seeds              -> Stats pool (available/consumed)
  POST /api/admin/seeds/scan         -> Rescan du dossier

WebSocket:
  WS   /ws/mod/{race_id}             -> Connexion mod
  WS   /ws/race/{race_id}            -> Connexion spectateur/orga
```

### Configuration

```bash
# .env
DATABASE_URL=postgresql+asyncpg://user:pass@localhost/speedfog_racing
TWITCH_CLIENT_ID=xxx
TWITCH_CLIENT_SECRET=xxx
TWITCH_REDIRECT_URI=https://speedfog-racing.example.com/api/auth/callback
SEEDS_POOL_DIR=/data/seeds
SECRET_KEY=xxx
WEBSOCKET_URL=wss://speedfog-racing.example.com
```

---

## 8. Frontend SvelteKit

### Structure

```
speedfog-racing/web/
├── src/
│   ├── lib/
│   │   ├── api.ts              # Client API REST
│   │   ├── websocket.ts        # Client WebSocket avec reconnect
│   │   ├── stores/
│   │   │   ├── auth.ts         # User session
│   │   │   ├── race.ts         # État race courante
│   │   │   └── leaderboard.ts  # Classement temps réel
│   │   └── components/
│   │       ├── DagView.svelte       # Visualisation DAG horizontal
│   │       ├── Leaderboard.svelte   # Classement joueurs
│   │       ├── Countdown.svelte     # Timer avant départ
│   │       └── PlayerMarker.svelte  # Marqueur joueur sur DAG
│   │
│   ├── routes/
│   │   ├── +layout.svelte      # Layout global, auth check
│   │   ├── +page.svelte        # Home
│   │   ├── auth/callback/+page.svelte
│   │   ├── race/
│   │   │   ├── new/+page.svelte       # Créer race
│   │   │   └── [id]/
│   │   │       ├── +page.svelte       # Vue race
│   │   │       ├── join/+page.svelte  # Rejoindre
│   │   │       └── manage/+page.svelte
│   │   ├── overlay/[id]/
│   │   │   ├── dag/+page.svelte       # Overlay DAG
│   │   │   └── leaderboard/+page.svelte
│   │   ├── invite/[token]/+page.svelte
│   │   └── admin/+page.svelte
│   │
│   └── app.css
├── static/
├── svelte.config.js
└── package.json
```

### Page race `/race/{id}`

```
┌─────────────────────────────────────────────────────────────────────────┐
│ SPEEDFOG RACE - "Sunday Showdown"                              [Logout] │
├────────────────────┬────────────────────────────────────────────────────┤
│  SIDEBAR           │              ZONE CENTRALE                         │
│                    │                                                    │
│  ┌──────────────┐  │   ┌────────────────────────────────────────────┐   │
│  │ Leaderboard  │  │   │                                            │   │
│  │ 1. P1   8/12 │  │   │         [DAG / PLAN DE METRO]              │   │
│  │ 2. P2   6/12 │  │   │                                            │   │
│  │ ...          │  │   │    (flouté avant le départ)                │   │
│  └──────────────┘  │   │    (visible spectateurs pendant)           │   │
│                    │   │    (flouté joueurs pendant)                │   │
│  ┌──────────────┐  │   │                                            │   │
│  │ OVERLAYS     │  │   └────────────────────────────────────────────┘   │
│  │ > DAG (OBS)  │  │                                                    │
│  │ > Leaderboard│  │                                                    │
│  └──────────────┘  │                                                    │
│                    │                                                    │
│  [Actions rôle]    │                                                    │
└────────────────────┴────────────────────────────────────────────────────┘
```

### Visibilité DAG par rôle

| Phase | Spectateur/Orga | Joueur |
|-------|-----------------|--------|
| Avant départ | Flouté | Flouté |
| Pendant race | DAG complet + positions | Flouté |
| Joueur termine | - | DAG révélé |
| Race terminée | DAG + résultats | DAG + résultats |

### Overlays OBS (fond transparent)

**DAG horizontal** `/overlay/{id}/dag` :

```
               ●───●───●───●───●───●───●───●───●───●───●───●───○
              /        ^            |                          \
●───●───●───●          |player1     |                           ●───○ END
              \        ^            |                          /
               ●───●───●───●───●───●───●───●───●───●───●───●───○
                       |player2
```

**Leaderboard vertical** `/overlay/{id}/leaderboard` :

```
┌─────────────────────────┐
│  SPEEDFOG RACE          │
├─────────────────────────┤
│ 1. Player4 [FIN]    💀5 │
│    01:45:32             │
├─────────────────────────┤
│ 2. Player1    8/12  💀3 │
│ 3. Player2    6/12  💀7 │
│ 4. Player3    5/12  💀2 │
└─────────────────────────┘
```

---

## 9. Gestion du pool de seeds

### Structure multi-pools

```
/data/seeds/
├── pools.toml                    # Définition des pools
│
├── sprint/                       # Pool "Sprint" (~30min)
│   ├── config.toml               # Settings fixes pour ce pool
│   ├── available/
│   │   └── seed_XXXXX/
│   └── consumed/
│
├── standard/                     # Pool "Standard" (~1h)
│   ├── config.toml
│   ├── available/
│   └── consumed/
│
└── marathon/                     # Pool "Marathon" (~2h)
    ├── config.toml
    ├── available/
    └── consumed/
```

### Définition des pools

```toml
# pools.toml
[sprint]
display_name = "Sprint (~30min)"
description = "Course rapide, peu de zones, scaling modéré"

[standard]
display_name = "Standard (~1h)"
description = "Format classique, bon équilibre"

[marathon]
display_name = "Marathon (~2h)"
description = "Course longue, nombreuses zones"
```

### Génération du pool

```bash
# Utilise le config.toml du pool spécifié
python tools/generate_pool.py --pool standard --count 10

# Workflow:
# 1. Charge /data/seeds/standard/config.toml
# 2. Appelle speedfog via CLI (cwd=SPEEDFOG_PATH)
# 3. Ajoute speedfog_race.dll dans l'output
# 4. Crée speedfog_race.toml template
# 5. Place dans standard/available/
```

### Génération des .zip par joueur

```python
async def generate_player_zips(race: Race) -> dict[UUID, Path]:
    seed_dir = Path(race.seed.zip_path)

    for participant in race.participants:
        # Copier la seed
        player_dir = temp_dir / f"{race.id}_{participant.user.twitch_name}"
        shutil.copytree(seed_dir, player_dir)

        # Modifier config avec token du joueur
        config = toml.load(player_dir / "speedfog_race.toml")
        config["server"]["mod_token"] = participant.mod_token
        config["server"]["race_id"] = str(race.id)
        config["server"]["url"] = settings.websocket_url
        toml.dump(config, player_dir / "speedfog_race.toml")

        # Zipper
        shutil.make_archive(...)
```

### Dashboard admin

```
┌─────────────────────────────────────────────┐
│  SEED POOL STATUS                           │
├─────────────────────────────────────────────┤
│  Sprint:    12 available / 3 consumed       │
│  Standard:  47 available / 13 consumed      │
│  Marathon:   8 available / 2 consumed       │
├─────────────────────────────────────────────┤
│  [Rescan pools]                             │
└─────────────────────────────────────────────┘
```

---

## 10. Phases d'implémentation

### Phase 1 : Fondations (MVP minimal)

**Objectif :** Une race fonctionnelle de bout en bout

| Composant | Tâches |
|-----------|--------|
| **Serveur** | Setup FastAPI, DB, Twitch OAuth, modèles |
| **Serveur** | Endpoints REST basiques (races CRUD, auth) |
| **Serveur** | WebSocket basique (mod + spectateur) |
| **Serveur** | Gestion pool simple (1 pool, assign seed, generate zips) |
| **Frontend** | Setup SvelteKit, auth Twitch, pages basiques |
| **Frontend** | Page création race, page race (leaderboard simple) |
| **Mod** | Fork er-fog-vizu, adapter protocole, overlay minimal |

**Résultat :** Orga crée race -> Joueurs download zip -> Course avec leaderboard

### Phase 2 : Expérience complète

| Composant | Tâches |
|-----------|--------|
| **Frontend** | Visualisation DAG horizontal |
| **Frontend** | Overlays OBS (dag + leaderboard) |
| **Frontend** | Multi-pools avec sélection |
| **Mod** | Overlay complet (classement, exits, countdown) |
| **Serveur** | Dashboard admin (stats seeds) |
| **Serveur** | Countdown synchronisé |

**Résultat :** Expérience de visionnage complète pour casteurs

### Phase 3 : Polish et features avancées

| Composant | Tâches |
|-----------|--------|
| **Mod** | Events EMEVD customs (tracking précis) |
| **Frontend** | Affichage progressif chemin pour joueurs |
| **Serveur** | Races asynchrones |
| **Serveur** | Historique / statistiques joueurs |
| **Infra** | Génération seeds à la demande (Wine) |

### Ordre de développement suggéré (Phase 1)

1. Server : setup + auth Twitch + DB
2. Frontend : setup + login Twitch
3. Server : modèles + endpoints races
4. Frontend : création/liste races
5. Mod : fork + connexion WebSocket basique
6. Server : WebSocket mod + leaderboard
7. Server : gestion pool + génération zips
8. Frontend : page race + download zip
9. Mod : overlay complet
10. Tests end-to-end

---

## Annexes

### Timing et classement

- Toujours basé sur l'IGT (In-Game Time) pour équité
- Classement :
  1. Joueurs terminés (triés par IGT croissant)
  2. Joueurs en cours (triés par layer décroissant)

### TODO techniques à explorer

- [ ] Events EMEVD customs dans FogModWrapper pour tracking précis des traversées
- [ ] Mécanisme de détection fin de course (boss final vaincu)
- [ ] Gestion déconnexion/reconnexion mod pendant une race
