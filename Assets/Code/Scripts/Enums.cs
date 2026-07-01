using UnityEngine;
public enum LaserDirection { BottomToTop, TopToBottom, Alternate, Double }
public enum WaveStyle { Linear, OutsideIn, InsideOut, OneByOne }

public enum StatCategory 
{ 
    Base,          
    PrimaryWeapon,  
    SpecialWeapon   
}

public enum HullStatType
{
    MaxHealth, MaxEnergy, EnergyRegen, EnergyConsuption, MaxShield,
}

public enum WeaponStatType
{
    Damage,
    FireRate,
    MagazineSize,
    ReloadSpeed,
    MuzzleVelocity,
    FirePointLevel,
}

[System.Flags]
public enum FirePointRole
{
    None    = 0,
    Primary = 1 << 0,
    Special = 1 << 1,
}


public enum GameState
{
    MainMenu,
    PlaneSelection,
    InGame,
    ProjectileSelection,
    UpgradeSelection,
    Pause,
    GameOver
}

public enum StatModType { Flat, PercentAdd }
public enum FirePattern { AllAtOnce, Alternating }
public enum EnergyState { Ready, Draining, Lockdown }
public enum Rarity { Common, Uncommon, Rare, Epic, Legendary }
public enum RotationAxis { LocalX, LocalY, LocalZ }

public enum UnlockConditionType
{
    None,
    CompleteTutorial,
    KillEnemiesInRun,
    KillEnemiesAndFlyDistanceInRun,
    ReachMaxLevel
}

public enum NoiseType { 
    Perlin,
    SimplexNoise,   
    FractalFBm,     
    CellularWorley, 
    RidgedMulti   
}
public enum FractalType { None, FBm, Ridged }

public enum CellularDistance { Euclidean, EuclideanSq, Manhattan, Hybrid }
public enum CellularReturn 
{ 
    [InspectorName("F1 (legacy CellValue)")] CellValue, 
    [InspectorName("F1")] Distance, 
    [InspectorName("F2")] Distance2, 
    [InspectorName("F2 + F1")] Distance2Add, 
    [InspectorName("F2 - F1")] Distance2Sub 
}

public enum DecorationRenderMode
{
    GameObjectPool, 
    GPUInstanced
}
