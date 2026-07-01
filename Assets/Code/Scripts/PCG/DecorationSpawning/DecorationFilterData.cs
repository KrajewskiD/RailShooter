using System.Runtime.InteropServices;

[StructLayout(LayoutKind.Sequential)]
public struct DecorationFilterData
{
    public int indexID;                  
    public int renderMode;               
    public float densityPer100m2;        
    public float minHeight;              
    public float maxHeight;              
    public float minSlope;               
    public float maxSlope;               
    public float minTemperature;         
    public float maxTemperature;         
    public float minMoisture;            
    public float maxMoisture;            
    public float scaleMin;               
    public float scaleMax;               
    public int randomYRotation;
    public int alignToTerrainNormal;
    public float lod0MaxDist;
    public float lod1MaxDist;
    public float lod2MaxDist;
    public float lod3MaxDist;
    public float lod4MaxDist;
    public static int Size => Marshal.SizeOf(typeof(DecorationFilterData));
}