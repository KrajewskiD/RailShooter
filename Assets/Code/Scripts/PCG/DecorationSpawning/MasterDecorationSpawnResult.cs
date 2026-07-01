using UnityEngine;
using System.Runtime.InteropServices;





[StructLayout(LayoutKind.Sequential)]
public struct MasterDecorationSpawnResult
{
    public int indexID;
    public int localID;
    public Vector3 position;
    public Vector4 rotation;
    public float scale;
    public float temperature;
    public float moisture;
    public static int Size => 48;
}
