# RailShooter

**Spline-based 3D rail shooter** z nieskończonym proceduralnym terenem, progresją roguelike i niszczalnymi asteroidami voxelowymi.

---

## Spis treści

- [O grze](#o-grze)
- [Pętla rozgrywki](#pętla-rozgrywki)
- [Funkcje](#funkcje)
  - [Lot i walka](#lot-i-walka)
  - [Progresja i meta](#progresja-i-meta)
  - [Wrogowie i przeszkody](#wrogowie-i-przeszkody)
  - [Generowanie proceduralne](#generowanie-proceduralne)
  - [UI i efekty](#ui-i-efekty)
- [Zawartość](#zawartość)
- [Sceny](#sceny)
- [Stack technologiczny](#stack-technologiczny)
- [Struktura projektu](#struktura-projektu)
- [Uruchomienie](#uruchomienie)
- [Narzędzia deweloperskie](#narzędzia-deweloperskie)

---

## O grze

RailShooter łączy klasyczny **rail shooter** (auto-forward + strafe w kadrze kamery) z:

- **nieskończonym światem** generowanym w czasie rzeczywistym,
- **systemem XP i ulepszeń** w stylu roguelike,
- **dwoma trybami środowiska** — otwarty teren (`MainGame`) oraz tunel tutorialowy (`TutorialScene`),
- **niszczalnymi asteroidami** opartymi o marching cubes.

Gracz nie steruje prędkością do przodu — skupia się na manewrowaniu, celowaniu, zarządzaniu energią i przetrwaniu coraz szybszego przebiegu.

---

## Pętla rozgrywki

```
Menu główne → wybór statku → lot wzdłuż splajnu
        ↓
Walka + zbieranie orbów (HP, tarcza, paliwo, XP, boost)
        ↓
Level-up → wybór 1 z 3 ulepszeń (rzadkość: Common → Legendary)
        ↓
Rosnąca prędkość przebiegu → śmierć → statystyki / odblokowania
```

Postęp jest zapisywany lokalnie (`player-progress.json`): aktywny run, statystyki lifetime, odblokowane statki i flaga ukończenia tutorialu.

---

## Funkcje

### Lot i walka

| Mechanika | Opis |
|-----------|------|
| **Spline flight** | Auto-forward na `SplineContainer`; strafe myszą / wirtualnym joystickiem w ramce viewportu |
| **Banking & kamera** | Przechył na zakrętach, Cinemachine roll, dynamiczne FOV od prędkości |
| **Boost** | Zużycie energii na chwilowe przyspieszenie |
| **Spin dodge** | Baryłka z cooldownem, krótka niewrażliwość i miganie modelu |
| **Dual weapons** | Broń podstawowa + specjalna — magazynki, przeładowanie, wzorce ognia (`AllAtOnce` / `Alternating`) |
| **Fire points** | Rozszerzalne punkty ognia (ulepszenia `FirePointLevel`) |
| **Raycast projectiles** | Pooled hitscan z efektami trafienia i filtrowaniem po tagu właściciela |
| **HP + tarcza + energia** | `EntityHealth` z warstwą tarczy; regeneracja energii; pickupy przywracające zasoby |

Sterowanie przez **Unity Input System** (`IA_PlayerControls`): celowanie, roll, ogień, specjal, boost, przeładowanie, spin, menu.

### Progresja i meta

| System | Opis |
|--------|------|
| **`GameStateManager`** | Maszyna stanów: menu, hangar, gra, wybór ulepszeń, pauza, game over |
| **`ProgressionManager`** | Krzywa XP z mnożnikiem wzrostu; eventy level-up |
| **`UpgradeManager`** | 3 karty ulepszeń ważone rzadkością; rerolle; modyfikatory statów (Flat / PercentAdd) |
| **`WeaponManager`** | Pula broni startowych z ważeniem rzadkości |
| **`PlaneSelectMenuController`** | Hangar z podglądem 3D (render texture), paskami statystyk i warunkami odblokowania |
| **`LevelManager`** | Eskalacja docelowej prędkości w czasie przebiegu |
| **`RunProgressTracker`** | Dystans na splajnie → statystyki i warunki unlocków |

### Wrogowie i przeszkody

| System | Opis |
|--------|------|
| **`EnemyAI`** | Pozycjonowanie względem splajnu, separacja, drift, doganianie gracza |
| **`EnemySpawner`** | Spawn pierścieniowy wokół gracza z limitem żywych jednostek |
| **`SplineSpawnManager`** | Data-driven spawn na segmentach splajnu (wrogowie, orb'y, asteroidy) |
| **`DestructibleVoxelChunk`** | Asteroidy voxelowe — marching cubes, wycinanie przy trafieniu (Burst Jobs) |
| **`LaserSpawner`** | Ruchome siatki laserów z falami (`Linear`, `OutsideIn`, `InsideOut`…) |
| **`TerrainObstacle`** | Obrażenia od kolizji ze ścianami terenu / tunelu |

### Generowanie proceduralne

#### Teren otwarty (`MainGame`)

- **`NoiseProvider`** — wielowarstwowy szum (Perlin, Simplex, Worley, Ridged FBm), mapy temperatury i wilgotności, strefy biomów (plaża, skała, śnieg, doliny, szczyty)
- **`TerrainManager`** — nieskończona siatka chunków wokół gracza: pooling, LOD z histerezą, frustum culling, budżet uploadu meshy na klatkę
- **`ChunkGenerator`** — mesh z heightfielda (Burst Jobs), kolory wierzchołków, opcjonalne collidery
- **`SplineGenerator`** — proceduralne przedłużanie trasy z ograniczeniami zakrętów, biasem do dolin i próbkowaniem wysokości terenu
- **`TerrainDecorationManager`** — GPU compute instancing dekoracji biomowych per LOD
- **`BiomeDatabase`** — siatka 9 stref klimatycznych (Cold/Norm/Hot × Dry/Mid/Wet)

#### Tunel (`TutorialScene`)

- **`TunnelGenerator`** — segmenty cylindryczne zdeformowane przez FastNoiseLite, krzywa promienia, kołysanie ścieżki, koloryzacja ścian
- **`TunnelSpawnManager`** — spawn na segmentach z warunkami (szansa, co n-ty segment, progresywna szansa) i placementami (centrum, pierścień, siatka pasów, klaster)

#### Podgląd i benchmarki

- **`TerrainPreviewGenerator`** — wizualizacja warstw szumu / klimatu / terenu
- **`TerrainPerformanceController`** — interaktywny benchmark generacji szumu i chunków

### UI i efekty

- **UI Toolkit** — menu główne, hangar, HUD, wybór ulepszeń, pauza, opcje
- **`InGameHudController`** — paski HP, tarczy, energii, XP, amunicji (DontDestroyOnLoad)
- **`DynamicFovBySpeed`** — FOV Cinemachine + linie prędkości skalowane z velocity
- **`VFXTrailSpeedController`** — Visual Effect Graph sterowany prędkością gracza
- **`HitEffectPool` / `OneShotAudioPool`** — pooled feedback walki

---

## Zawartość

### Statki

| Statek | Dostępność | Charakter |
|--------|------------|-----------|
| **Raven TX-1** | Od startu | Workhorse — wyrozumiały, mechaniczny, bez tarczy |
| **Crimson Hawk** | Po tutorialu | Ciężki kanon, wysokie HP, niska regeneracja energii |
| **Night Phantom** | 30 zabójstw w jednym runie | Stealth — wysoka tarcza, niska energia |
| **Dragonfly** | Osiągnięcie max levelu (30) | Lekki, szybki, wysoka energia |

### Broń podstawowa

Pulse Shot · Frost Lance · Inferno Burst · Toxic Nova · Void Spark

### Broń specjalna

Solar Rocket · EMP Core · Homing Star · Gravity Mine · Cluster Bloom

### Ulepszenia (przykłady)

Modyfikatory dla kadłuba (HP, tarcza, energia, regen, efektywność boostu) oraz broni (obrażenia, fire rate, magazynek, przeładowanie, prędkość pocisku, fire pointy) — z rzadkościami od **Common** do **Legendary**.

### Pickupy

Orb'y: **XP**, **HP**, **tarcza**, **paliwo**, **boost** — z magnetycznym przyciąganiem do gracza.

---

## Sceny

| Scena | W buildzie | Przeznaczenie |
|-------|:----------:|---------------|
| `MainMenu` | ✅ | Menu główne, hangar, kontynuacja gry |
| `TutorialScene` | ✅ | Intro w tunelu — `TutorialFlowController`, zwężanie tunelu, podpowiedzi sterowania |
| `MainGame` | ✅ | Główna rozgrywka — teren PCG, splajn, dekoracje, menedżery progresji |
| `NoisePreviewScene` | ❌ | Dev — podgląd szumu i klimatu |
| `TerrainPerformance` | ❌ | Dev — benchmark wydajności PCG |

Kolejność w build settings: **MainMenu → TutorialScene → MainGame**

---

## Stack technologiczny

| Technologia | Wersja / uwagi |
|-------------|----------------|
| **Unity** | `6000.3.14f1` (Unity 6) |
| **URP** | Universal Render Pipeline 17.3 |
| **Cinemachine** | 3.1.6 |
| **Input System** | 1.19.0 |
| **Splines** | Unity Splines (trasy lotu) |
| **Burst / Jobs / Collections** | Teren, szum, voxel meshing |
| **Visual Effect Graph** | 17.3 |
| **UI Toolkit** | Główny interfejs |
| **TextMesh Pro** | 3.0.9 |
| **NaughtyAttributes** | Atrybuty inspektora na ScriptableObjects |

Color space: **Linear** · Domyślna rozdzielczość: **1920×1080**

---

## Struktura projektu

```
Assets/
├── Code/
│   ├── Scripts/
│   │   ├── Camera/          # SkyFollower, DynamicFovBySpeed
│   │   ├── Combat/          # GunEngine, pociski, pickupy, pule
│   │   ├── Data/            # ScriptableObject definitions
│   │   ├── Enemy/           # EnemyAI, EnemySpawner
│   │   ├── GameFlow/        # Stany gry, save, progresja, tutorial
│   │   ├── Obstacles/       # Lasery, kolizje terenu
│   │   ├── PCG/             # Szum, teren, splajny, tunele, spawn
│   │   ├── Player/          # SplinePlayerController, PlayerController
│   │   ├── Stats/           # System statów i modyfikatorów
│   │   ├── UI/              # Menu, HUD, karty ulepszeń
│   │   ├── VFX/             # Trail speed controller
│   │   └── Voxel/           # Marching cubes, carve jobs
│   └── ScriptableObjects/   # Statki, bronie, ulepszenia, spawn data
├── Editor/                  # Custom inspectory PCG
├── Prefabs/                 # Wrogowie, menedżery, VFX, przeszkody
├── Resources/UI/            # UXML / USS (HUD, menu, selection screens)
├── Scenes/                  # Sceny gry i dev
├── Settings/                # URP, Input Actions
└── Shaders/Terrain/         # Shadery terenu i compute dekoracji

tools/                       # Skrypty pomocnicze (dokumentacja PCG)
Packages/                    # manifest.json + packages-lock.json
ProjectSettings/
```

---

## Uruchomienie

### Wymagania

- **Unity Hub** z edytorem **6000.3.14f1**
- Moduł **macOS** / **Windows** (zależnie od platformy docelowej)

### Kroki

1. Sklonuj repozytorium i otwórz folder projektu w Unity Hub.
2. Poczekaj na import assetów (pierwsze otwarcie po sklonowaniu może potrwać — folder `Library/` nie jest w repo).
3. Otwórz scenę `Assets/Scenes/MainMenu.unity` i naciśnij **Play**.
